"""
pipeline/tree.py — global vegetation canopy cover.
Replaces tree.sh.
"""
import zipfile
from pathlib import Path

import numpy as np
import rasterio

from util.projection import MasterGrid, Bounds4326
from util.raster import warp_to_grid, fill_nodata_arr, save_array


def run(work_dir: Path, datasets_dir: Path, grid: MasterGrid, bounds: Bounds4326, cfg):
    """
    Produces:
      - work_dir/tree.tif   (Byte, canopy cover %)
    """
    tree_dir = work_dir / "tree"
    tree_dir.mkdir(parents=True, exist_ok=True)

    # ------------------------------------------------------------------ #
    # 1. Extract dataset
    # ------------------------------------------------------------------ #
    zip_path = datasets_dir / "gm_ve_v1.zip"
    if not zip_path.exists():
        raise FileNotFoundError(
            f"Tree canopy dataset not found at {zip_path}. "
            "Please place gm_ve_v1.zip in the datasets/ directory."
        )

    tree_tif = tree_dir / "gm_ve_v1.tif"
    if not tree_tif.exists():
        with zipfile.ZipFile(zip_path) as z:
            z.extractall(tree_dir)
        # Find the extracted TIF
        candidates = list(tree_dir.glob("gm_ve_v1.tif"))
        if not candidates:
            candidates = list(tree_dir.rglob("*.tif"))
        if not candidates:
            raise FileNotFoundError(f"gm_ve_v1.tif not found after extraction in {tree_dir}")
        if candidates[0] != tree_tif:
            candidates[0].rename(tree_tif)

    # ------------------------------------------------------------------ #
    # 2. Warp to master grid
    # ------------------------------------------------------------------ #
    from rasterio.warp import Resampling
    crop_path = tree_dir / "crop_tree.tif"
    crop_arr = warp_to_grid(
        str(tree_tif),
        str(crop_path),
        grid,
        dtype="uint8",
        resampling=Resampling.cubic_spline,
        nodata=255,
        src_nodata=255,
    )

    # ------------------------------------------------------------------ #
    # 3. Fill NoData
    # ------------------------------------------------------------------ #
    filled = fill_nodata_arr(crop_arr, nodata_value=255, max_distance=500)
    filled = np.clip(filled, 0, 254).astype(np.uint8)  # keep 255 as nodata sentinel

    save_array(filled, str(work_dir / "tree.tif"), grid, dtype="uint8")
    print("[tree] Tree canopy done.")
