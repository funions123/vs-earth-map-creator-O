"""
pipeline/climate.py — WorldClim precipitation + temperature approach.
Currently disabled in launch.py (use koppen.py instead).
"""
import zipfile
from pathlib import Path

import numpy as np
import rasterio
from rasterio.warp import Resampling

from util.projection import MasterGrid, Bounds4326
from util.raster import warp_to_grid, rescale, save_array


def run(work_dir: Path, datasets_dir: Path, grid: MasterGrid, bounds: Bounds4326, cfg):
    """
    Produces:
      - work_dir/climate.tif   (3-band Byte: R=precip, G=precip2, B=temp)

    Input datasets (must be in datasets/):
      - wc2.1_30s_prec_tavg.zip  — WorldClim average monthly precipitation
      - wc2.1_30s_tavg.zip       — WorldClim average monthly temperature
    """
    climate_dir = work_dir / "climate"
    climate_dir.mkdir(parents=True, exist_ok=True)

    print("[climate] Processing WorldClim precipitation...")
    prec_arr = _process_worldclim_precip(datasets_dir, climate_dir, grid)

    print("[climate] Processing WorldClim temperature...")
    temp_arr = _process_worldclim_temp(datasets_dir, climate_dir, grid)

    print("[climate] Writing climate.tif (R=precip, G=precip2, B=temp)...")
    # Replicate the bash approach: R and G both get precip variants, B gets temp
    save_array(
        [prec_arr, prec_arr, temp_arr],
        str(work_dir / "climate.tif"),
        grid,
        dtype="uint8",
        count=3,
        photometric="RGB",
    )
    print("[climate] WorldClim climate done.")


def _process_worldclim_precip(datasets_dir: Path, out_dir: Path, grid: MasterGrid) -> np.ndarray:
    """Average monthly precip TIFs → piecewise-scaled Byte array."""
    zip_path = datasets_dir / "wc2.1_30s_prec_tavg.zip"
    if not zip_path.exists():
        raise FileNotFoundError(f"WorldClim precip not found at {zip_path}")

    extract_dir = out_dir / "prec_extracted"
    if not extract_dir.exists():
        extract_dir.mkdir()
        with zipfile.ZipFile(zip_path) as z:
            z.extractall(extract_dir)

    tif_files = sorted(extract_dir.rglob("*.tif"))
    if not tif_files:
        raise FileNotFoundError(f"No .tif files found in {extract_dir}")

    # Average all monthly TIFs
    arrays = []
    for tif in tif_files:
        arr = warp_to_grid(str(tif), str(out_dir / f"prec_{tif.stem}.tif"), grid, "float32",
                           Resampling.cubic_spline, nodata=-9999, src_nodata=-9999)
        valid = np.where(arr == -9999, np.nan, arr)
        arrays.append(valid)

    avg = np.nanmean(arrays, axis=0)
    avg = np.nan_to_num(avg, nan=0.0)

    # Piecewise scaling: stretch low precip values
    # Scale 0..50mm → 0..128, 50..max → 128..255
    mid = 50.0
    max_val = float(np.nanmax(avg)) or 1.0
    low = np.where(avg <= mid, avg / mid * 128.0, 128.0)
    high = np.where(avg > mid, 128.0 + (avg - mid) / (max_val - mid) * 127.0, 0.0)
    combined = low + high
    return np.clip(combined, 0, 255).astype(np.uint8)


def _process_worldclim_temp(datasets_dir: Path, out_dir: Path, grid: MasterGrid) -> np.ndarray:
    """Average monthly temp TIFs → linearly-scaled Byte array."""
    zip_path = datasets_dir / "wc2.1_30s_tavg.zip"
    if not zip_path.exists():
        raise FileNotFoundError(f"WorldClim temp not found at {zip_path}")

    extract_dir = out_dir / "temp_extracted"
    if not extract_dir.exists():
        extract_dir.mkdir()
        with zipfile.ZipFile(zip_path) as z:
            z.extractall(extract_dir)

    tif_files = sorted(extract_dir.rglob("*.tif"))
    if not tif_files:
        raise FileNotFoundError(f"No .tif files found in {extract_dir}")

    arrays = []
    for tif in tif_files:
        arr = warp_to_grid(str(tif), str(out_dir / f"temp_{tif.stem}.tif"), grid, "float32",
                           Resampling.cubic_spline, nodata=-9999, src_nodata=-9999)
        valid = np.where(arr == -9999, np.nan, arr)
        arrays.append(valid)

    avg = np.nanmean(arrays, axis=0)
    avg = np.nan_to_num(avg, nan=0.0)

    # Linear scale: typical range -50°C to +50°C → 0..255
    return rescale(avg, -50.0, 50.0, 0, 255, np.uint8)
