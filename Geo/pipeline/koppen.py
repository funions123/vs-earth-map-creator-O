"""
pipeline/koppen.py — Köppen-Geiger climate classification.
Replaces koppen.sh.
"""
import zipfile
from pathlib import Path

import numpy as np
import rasterio
from rasterio.warp import Resampling

from util.projection import MasterGrid, Bounds4326
from util.raster import warp_to_grid, save_array, download_file


# Beck et al. (2023) color table: class_index → (R, G, B)
KOPPEN_LUT = {
    0:  (0,   0,   0),    # NoData
    1:  (0,   0,   255),  # Af  - Tropical, rainforest
    2:  (0,   120, 255),  # Am  - Tropical, monsoon
    3:  (70,  170, 250),  # Aw  - Tropical, savannah
    4:  (255, 0,   0),    # BWh - Arid, desert, hot
    5:  (255, 150, 150),  # BWk - Arid, desert, cold
    6:  (245, 165, 0),    # BSh - Arid, steppe, hot
    7:  (255, 220, 100),  # BSk - Arid, steppe, cold
    8:  (255, 255, 0),    # Csa - Temperate, dry summer, hot summer
    9:  (200, 200, 0),    # Csb - Temperate, dry summer, warm summer
    10: (150, 150, 0),    # Csc - Temperate, dry summer, cold summer
    11: (150, 255, 150),  # Cwa - Temperate, dry winter, hot summer
    12: (100, 200, 100),  # Cwb - Temperate, dry winter, warm summer
    13: (50,  150, 50),   # Cwc - Temperate, dry winter, cold summer
    14: (200, 255, 80),   # Cfa - Temperate, no dry season, hot summer
    15: (100, 255, 80),   # Cfb - Temperate, no dry season, warm summer
    16: (50,  200, 0),    # Cfc - Temperate, no dry season, cold summer
    17: (255, 0,   255),  # Dsa - Cold, dry summer, hot summer
    18: (200, 0,   200),  # Dsb - Cold, dry summer, warm summer
    19: (150, 50,  150),  # Dsc - Cold, dry summer, cold summer
    20: (150, 100, 150),  # Dsd - Cold, dry summer, very cold winter
    21: (170, 175, 255),  # Dwa - Cold, dry winter, hot summer
    22: (90,  120, 220),  # Dwb - Cold, dry winter, warm summer
    23: (75,  80,  180),  # Dwc - Cold, dry winter, cold summer
    24: (50,  0,   135),  # Dwd - Cold, dry winter, very cold winter
    25: (0,   255, 255),  # Dfa - Cold, no dry season, hot summer
    26: (55,  200, 255),  # Dfb - Cold, no dry season, warm summer
    27: (0,   125, 125),  # Dfc - Cold, no dry season, cold summer
    28: (0,   70,  95),   # Dfd - Cold, no dry season, very cold winter
    29: (178, 178, 178),  # ET  - Polar, tundra
    30: (102, 102, 102),  # EF  - Polar, frost
}


def _get_koppen_tif(datasets_dir: Path, koppen_dir: Path, cfg) -> Path:
    """
    Locate or download the Köppen source TIF.
    Checks in order:
      1. Standalone .tif in datasets/ (any koppen_geiger_*.tif)
      2. Zip file (koppen_geiger_tif.zip) in datasets/ — extract from it
      3. Download zip from cfg.KOPPEN_URL
    Returns path to a local .tif file.
    """
    # 1. Standalone TIF already in datasets/
    candidates = sorted(datasets_dir.glob("koppen_geiger_*.tif"))
    if candidates:
        return candidates[0]

    # 2. Zip in datasets/
    koppen_zip = datasets_dir / "koppen_geiger_tif.zip"

    if not koppen_zip.exists():
        download_file(cfg.KOPPEN_URL, str(koppen_zip), desc="koppen_geiger_tif.zip")

    dest_tif = koppen_dir / "koppen_source.tif"
    if not dest_tif.exists():
        with zipfile.ZipFile(koppen_zip) as z:
            tif_names = [n for n in z.namelist() if n.endswith(".tif")]
            if not tif_names:
                raise FileNotFoundError("No .tif file found in Köppen zip")
            # Prefer high-res (0p00833333 ≈ 1km); fall back to first .tif
            preferred = [n for n in tif_names if "0p00833333" in n]
            pick = preferred[0] if preferred else tif_names[0]
            z.extract(pick, koppen_dir)
            (koppen_dir / pick).rename(dest_tif)

    return dest_tif


def run(work_dir: Path, datasets_dir: Path, grid: MasterGrid, bounds: Bounds4326, cfg):
    """
    Produces:
      - work_dir/koppen_climate_rgb.tif   (3-band Byte RGB)
    """
    koppen_dir = work_dir / "koppen_climate"
    koppen_dir.mkdir(parents=True, exist_ok=True)

    # ------------------------------------------------------------------ #
    # 1. Get data
    # ------------------------------------------------------------------ #
    source_tif = _get_koppen_tif(datasets_dir, koppen_dir, cfg)

    # ------------------------------------------------------------------ #
    # 2. Warp to master grid with nearest resampling (preserve categories)
    # ------------------------------------------------------------------ #
    indexed_path = koppen_dir / "koppen_indexed.tif"
    indexed_arr = warp_to_grid(
        str(source_tif),
        str(indexed_path),
        grid,
        dtype="uint8",
        resampling=Resampling.nearest,
        nodata=0,
        src_nodata=0,
    )

    # ------------------------------------------------------------------ #
    # 3. Apply color lookup table → RGB
    # ------------------------------------------------------------------ #
    lut = np.zeros((256, 3), dtype=np.uint8)
    for idx, rgb in KOPPEN_LUT.items():
        if idx < 256:
            lut[idx] = rgb

    r = lut[indexed_arr, 0]
    g = lut[indexed_arr, 1]
    b = lut[indexed_arr, 2]

    save_array(
        [r, g, b],
        str(work_dir / "koppen_climate_rgb.tif"),
        grid,
        dtype="uint8",
        count=3,
        photometric="RGB",
    )
    print("[koppen] Köppen-Geiger done.")
