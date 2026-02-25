# config.py — edit this before each run

# --- Bounding Box (EPSG:4326) ---
# Ionian Sea
LAT_MIN = 33.1
LAT_MAX = 44.2
LON_MIN = 19.0
LON_MAX = 32.0

# Eastern Mediterranean
# LAT_MIN = 25.0; LAT_MAX = 50.0; LON_MIN = 5.0; LON_MAX = 40.0

# Great Lakes
# LAT_MIN = 35.013755; LAT_MAX = 50.627996; LON_MIN = -90.084961; LON_MAX = -75.350586

# Ireland
# LAT_MIN = 51.013755; LAT_MAX = 55.627996; LON_MIN = -12.084961; LON_MAX = -4.350586

# Full planet
# LAT_MIN = -84.0; LAT_MAX = 84.0; LON_MIN = -179.0; LON_MAX = 179.0

# --- Output Resolution ---
FINAL_WIDTH = 10240    # final PNG width in pixels (must be multiple of 512)
FINAL_LENGTH = 10240   # final PNG height in pixels (must be multiple of 512)
FINAL_RES = 300        # internal raster pixel size in metres

# --- Projection ---
# Set to None to auto-select UTM (for regions < 13° wide) or EPSG:3857 (wider).
# Set to a CRS string (e.g. "EPSG:32633", "ESRI:54080") to override.
FORCE_FINAL_PROJ = "ESRI:54080"

# --- Resize final PNGs to FINAL_WIDTH x FINAL_LENGTH ---
RESIZE_MAP = True

# --- Bathymetry Scaling ---
ENABLE_BATHY_CUSTOM_SCALE = True

# Vintage Story sea level byte value (0–255); default 92
BATHY_SCALE_SEALEVEL = 92

# Vintage Story maximum ocean depth byte value (0–255); default 50
BATHY_SCALE_MAXDEPTH = 50

# Set True for piecewise scaling (exaggerates shallow water), False for linear
BATHY_USE_PIECEWISE_SCALE = True

# Raw elevation (negative metres) where the piecewise function switches
BATHY_EXAGGERATE_THRESHOLD = -100

# Output byte value at the threshold (must be between MAXDEPTH and SEALEVEL)
BATHY_EXAGGERATE_MIDPOINT = 80

# --- Rivers ---
# Half-width of rendered rivers in blocks at sea level
MAJOR_RIVER_WIDTH = 7

# --- Dataset caching ---
# Cache downloaded datasets in Geo/datasets/ for reuse across runs
DOWNLOAD_DATASETS_LOCALLY = True
FORCE_LOCAL_DATASETS_UPDATE = False
GET_DATASETS_LOCALLY = True

# --- Dataset URLs ---
OSM_LANDPOLYGONS_URL = "https://osmdata.openstreetmap.de/download/land-polygons-complete-4326.zip"
KOPPEN_URL = "https://figshare.com/ndownloader/files/45057352"
