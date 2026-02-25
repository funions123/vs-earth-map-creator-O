"""
pipeline/translate.py — convert intermediate TIFs to final PNGs.
Replaces translate.sh. Uses rasterio + Pillow for TIF→PNG conversion.
ImageMagick (magick) is called via subprocess for river post-processing.
"""
import subprocess
import shutil
from pathlib import Path

import numpy as np
import rasterio
from PIL import Image

from util.projection import MasterGrid


def run(work_dir: Path, grid: MasterGrid, cfg):
    """
    Produces 8 PNGs in work_dir/build/:
      bathymetry_heightmap.png, complete_topo.png, heightmap.png,
      lake_mask.png, landmask.png, climate.png, tree.png, river.png
    """
    build_dir = work_dir / "build"
    build_dir.mkdir(parents=True, exist_ok=True)

    out_w = cfg.FINAL_WIDTH if cfg.RESIZE_MAP else None
    out_h = cfg.FINAL_LENGTH if cfg.RESIZE_MAP else None

    # 1. Bathymetry (Byte, no rescale needed)
    _tif_to_png(
        work_dir / "bathymetry.tif",
        build_dir / "bathymetry_heightmap.png",
        out_w, out_h,
    )

    # 2. Complete topography (UInt16 → rescale to Byte)
    _tif_to_png(
        work_dir / "complete_topo.tif",
        build_dir / "complete_topo.png",
        out_w, out_h,
        src_range=(0, 65535), dst_range=(0, 255),
    )

    # 3. Lake surface heightmap (UInt16 → rescale to Byte)
    _tif_to_png(
        work_dir / "cropped_dem.tif",
        build_dir / "heightmap.png",
        out_w, out_h,
        src_range=(0, 65535), dst_range=(0, 255),
    )

    # 4. Lake mask (0/1 → 0/255)
    _tif_to_png(
        work_dir / "lakes_mask.tif",
        build_dir / "lake_mask.png",
        out_w, out_h,
        src_range=(0, 1), dst_range=(0, 255),
    )

    # 5. Land mask (Byte, pass-through)
    _tif_to_png(
        work_dir / "land_osm_mask.tif",
        build_dir / "landmask.png",
        out_w, out_h,
    )

    # 6. Climate / Köppen RGB (3-band Byte)
    koppen_rgb = work_dir / "koppen_climate_rgb.tif"
    _tif_to_png(koppen_rgb, build_dir / "climate.png", out_w, out_h, multiband=True)

    # 7. Tree (Byte)
    _tif_to_png(work_dir / "tree.tif", build_dir / "tree.png", out_w, out_h)

    # 8. Rivers (Byte) — then ImageMagick post-process
    river_png = build_dir / "river.png"
    _tif_to_png(work_dir / "rivers.tif", river_png, out_w, out_h)
    _postprocess_rivers(river_png)

    print("[translate] All PNGs written to", build_dir)


# ------------------------------------------------------------------ #
# Helpers
# ------------------------------------------------------------------ #

def _tif_to_png(
    src: Path,
    dst: Path,
    out_w, out_h,
    src_range=None,
    dst_range=None,
    multiband: bool = False,
):
    """Read a TIF, optionally rescale, optionally resize, save as PNG."""
    if not src.exists():
        print(f"  Warning: {src.name} not found, skipping.")
        return

    with rasterio.open(str(src)) as r:
        if multiband and r.count >= 3:
            bands = [r.read(i + 1).astype(np.float64) for i in range(3)]
            if src_range:
                bands = [
                    np.clip(
                        (b - src_range[0]) / (src_range[1] - src_range[0]) * (dst_range[1] - dst_range[0]) + dst_range[0],
                        dst_range[0], dst_range[1],
                    )
                    for b in bands
                ]
            arr_rgb = np.stack([b.astype(np.uint8) for b in bands], axis=-1)
            img = Image.fromarray(arr_rgb, mode="RGB")
        else:
            arr = r.read(1).astype(np.float64)
            if src_range:
                lo, hi = src_range
                t_lo, t_hi = dst_range
                if hi == lo:
                    arr = np.full_like(arr, t_lo)
                else:
                    arr = (arr - lo) / (hi - lo) * (t_hi - t_lo) + t_lo
            arr = np.clip(arr, 0, 255).astype(np.uint8)
            img = Image.fromarray(arr, mode="L")

    if out_w and out_h:
        # Use LANCZOS for RGB (climate), BILINEAR for scalar maps (no overshoot)
        resample = Image.LANCZOS if img.mode == "RGB" else Image.BILINEAR
        img = img.resize((out_w, out_h), resample)

    img.save(str(dst))


def _postprocess_rivers(river_png: Path):
    """
    Apply ImageMagick post-processing to the river mask PNG.
    Replicates: magick river.png -background black -alpha remove -alpha off
                -threshold 90% -blur 0x5 -posterize 10 -level 0%,100%,1.0 river.png
    """
    if not river_png.exists():
        return

    magick = shutil.which("magick") or shutil.which("convert")
    if not magick:
        print("  Warning: ImageMagick not found; skipping river post-processing.")
        _postprocess_rivers_pillow(river_png)
        return

    cmd = [
        magick, str(river_png),
        "-background", "black",
        "-alpha", "remove",
        "-alpha", "off",
        "-threshold", "90%",
        "-blur", "0x5",
        "-posterize", "10",
        "-level", "0%,100%,1.0",
        str(river_png),
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"  Warning: ImageMagick river post-processing failed: {result.stderr.strip()}")
        _postprocess_rivers_pillow(river_png)


def _postprocess_rivers_pillow(river_png: Path):
    """
    Fallback Pillow-based river post-processing when ImageMagick is unavailable.
    Approximates: threshold 90% → blur → posterize.
    """
    from PIL import ImageFilter

    img = Image.open(str(river_png)).convert("L")
    arr = np.array(img, dtype=np.float32)

    # Threshold at 90%
    arr = np.where(arr >= 0.9 * 255, 255.0, 0.0)

    # Gaussian blur σ=5
    img2 = Image.fromarray(arr.astype(np.uint8), mode="L")
    img2 = img2.filter(ImageFilter.GaussianBlur(radius=5))

    # Posterize to 10 levels
    arr2 = np.array(img2, dtype=np.float32)
    levels = 10
    arr2 = np.round(arr2 / 255.0 * (levels - 1)) / (levels - 1) * 255.0

    img3 = Image.fromarray(arr2.astype(np.uint8), mode="L")
    img3.save(str(river_png))
