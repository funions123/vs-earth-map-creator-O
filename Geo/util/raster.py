"""
util/raster.py — shared rasterio helpers.
Replaces gdalwarp, gdal_translate, gdal_calc.py, gdal_rasterize, fill-nodata.
"""
import numpy as np
import rasterio
from rasterio.warp import reproject, Resampling
from rasterio.fill import fillnodata
from rasterio.features import rasterize as rio_rasterize
from rasterio.transform import from_bounds

from util.projection import MasterGrid


def warp_to_grid(
    src_path: str,
    dst_path: str,
    grid: MasterGrid,
    dtype: str,
    resampling=Resampling.cubic_spline,
    nodata=None,
    src_nodata=None,
    band: int = 1,
) -> np.ndarray:
    """
    Warp src_path to the master grid, write to dst_path, return the array.
    Replaces: gdalwarp -t_srs -tr -te -te_srs
    """
    with rasterio.open(src_path) as src:
        dst_arr = np.zeros((grid.height, grid.width), dtype=dtype)
        reproject(
            source=rasterio.band(src, band),
            destination=dst_arr,
            src_transform=src.transform,
            src_crs=src.crs,
            dst_transform=grid.transform,
            dst_crs=grid.crs,
            resampling=resampling,
            src_nodata=src_nodata,
            dst_nodata=nodata,
        )

    profile = {
        "driver": "GTiff",
        "dtype": dtype,
        "width": grid.width,
        "height": grid.height,
        "count": 1,
        "crs": grid.crs,
        "transform": grid.transform,
        "compress": "lzw",
        "predictor": 2,
        "bigtiff": "YES",
    }
    if nodata is not None:
        profile["nodata"] = nodata

    with rasterio.open(dst_path, "w", **profile) as dst:
        dst.write(dst_arr, 1)

    return dst_arr


def rescale(
    arr: np.ndarray,
    src_min: float,
    src_max: float,
    dst_min: float,
    dst_max: float,
    dtype,
) -> np.ndarray:
    """
    Linear rescale from [src_min, src_max] to [dst_min, dst_max].
    Replaces: gdal_translate -scale, gdal_calc linear expressions.
    """
    if src_max == src_min:
        return np.full_like(arr, dst_min, dtype=dtype)
    out = np.interp(arr.astype(np.float64), [src_min, src_max], [dst_min, dst_max])
    return np.clip(out, dst_min, dst_max).astype(dtype)


def rasterize_features(
    shapes,
    grid: MasterGrid,
    dst_path: str,
    burn_value,
    dtype: str,
    all_touched: bool = False,
    fill: int = 0,
) -> np.ndarray:
    """
    Rasterize an iterable of (geometry, value) or geometry objects.
    Geometries must be in the same CRS as the master grid.
    Replaces: gdal_rasterize
    """
    out = rio_rasterize(
        shapes,
        out_shape=(grid.height, grid.width),
        transform=grid.transform,
        fill=fill,
        dtype=dtype,
        all_touched=all_touched,
    )

    profile = {
        "driver": "GTiff",
        "dtype": dtype,
        "width": grid.width,
        "height": grid.height,
        "count": 1,
        "crs": grid.crs,
        "transform": grid.transform,
        "compress": "lzw",
    }
    with rasterio.open(dst_path, "w", **profile) as dst:
        dst.write(out, 1)

    return out


def fill_nodata_arr(
    arr: np.ndarray,
    nodata_value,
    max_distance: int = 500,
) -> np.ndarray:
    """
    Fill NoData holes using nearest-neighbour search.
    Replaces: gdal raster fill-nodata --strategy nearest
    """
    arr = arr.copy().astype(np.float32)
    mask = (arr != nodata_value).astype(np.uint8)
    filled = fillnodata(arr, mask=mask, max_search_distance=max_distance)
    return filled


def save_array(
    arr: np.ndarray,
    dst_path: str,
    grid: MasterGrid,
    dtype: str,
    nodata=None,
    count: int = 1,
    photometric: str = None,
) -> None:
    """
    Write a numpy array (or list of arrays for multi-band) to a GeoTIFF.
    """
    profile = {
        "driver": "GTiff",
        "dtype": dtype,
        "width": grid.width,
        "height": grid.height,
        "count": count,
        "crs": grid.crs,
        "transform": grid.transform,
        "compress": "lzw",
        "predictor": 2,
        "bigtiff": "YES",
    }
    if nodata is not None:
        profile["nodata"] = nodata
    if photometric:
        profile["photometric"] = photometric

    with rasterio.open(dst_path, "w", **profile) as dst:
        if count == 1:
            dst.write(np.asarray(arr, dtype=dtype), 1)
        else:
            for i, band in enumerate(arr, 1):
                dst.write(np.asarray(band, dtype=dtype), i)


def align_to_grid(
    src_path: str,
    dst_path: str,
    grid: MasterGrid,
    resampling=Resampling.nearest,
) -> np.ndarray:
    """
    Reproject src_path to exactly match grid (CRS, transform, dimensions).
    Inline port of aligner.py.
    """
    with rasterio.open(src_path) as src:
        dst_arr = np.zeros((grid.height, grid.width), dtype=src.dtypes[0])
        reproject(
            source=rasterio.band(src, 1),
            destination=dst_arr,
            src_transform=src.transform,
            src_crs=src.crs,
            dst_transform=grid.transform,
            dst_crs=grid.crs,
            resampling=resampling,
            dst_nodata=src.nodata,
        )

        profile = src.profile.copy()
        profile.update(
            width=grid.width,
            height=grid.height,
            crs=grid.crs,
            transform=grid.transform,
            compress="lzw",
            bigtiff="YES",
        )

    with rasterio.open(dst_path, "w", **profile) as dst:
        dst.write(dst_arr, 1)

    return dst_arr


def download_file(
    url: str,
    dst_path: str,
    desc: str = "",
    headers: dict = None,
    timeout: int = 60,
) -> None:
    """
    Download url to dst_path with streaming and progress output.
    Replaces: curl -L -C -
    """
    import requests

    if headers is None:
        headers = {
            "User-Agent": (
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/120.0.0.0 Safari/537.36"
            )
        }

    print(f"  Downloading {desc or url} ...")
    with requests.get(url, stream=True, headers=headers, timeout=timeout) as r:
        r.raise_for_status()
        with open(dst_path, "wb") as f:
            for chunk in r.iter_content(chunk_size=1024 * 1024):
                if chunk:
                    f.write(chunk)
