using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMapCreator;

/// <summary>
/// A custom MapLayer that, instead of generating noise, reads its data directly 
/// from a pre-loaded IntDataMap2D sourced from an image file.
/// </summary>
public class MapLayerFromImage : MapLayerBase
{
    private IntDataMap2D[][] _sourceMap;
    private int _regionSize;
    private int _scale; // The scaling factor used by the map layer (e.g., TerraGenConfig.climateMapScale)
    private System.Func<int, int, int, int> _postProcessFunc;

    public MapLayerFromImage(long seed, IntDataMap2D[][] sourceMap, ICoreServerAPI api, int scale, System.Func<int, int, int, int> postProcessFunc = null) : base(seed)
    {
        _sourceMap = sourceMap;
        _regionSize = api.WorldManager.RegionSize;
        _scale = scale;
        _postProcessFunc = postProcessFunc;
    }

    public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ)
    {
        int[] result = new int[sizeX * sizeZ];
        
        int blockXStart = xCoord * _scale;
        int blockZStart = zCoord * _scale;

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                // Calculate the final block coordinates for the current point in the layer.
                int currentBlockX = blockXStart + (x * _scale);
                int currentBlockZ = blockZStart + (z * _scale);

                int regionX = currentBlockX / _regionSize;
                int regionZ = currentBlockZ / _regionSize;

                // --- SAFETY CHECK ---
                if (regionX < 0 || regionX >= _sourceMap.Length ||
                    regionZ < 0 || regionZ >= _sourceMap[0].Length)
                {
                    // If outside our map, this point gets a value of 0.
                    result[z * sizeX + x] = 0;
                    continue;
                }

                IntDataMap2D regionMap = _sourceMap[regionX][regionZ];
                
                int relativeX = currentBlockX % _regionSize;
                int relativeZ = currentBlockZ % _regionSize;
                
                if (relativeX < 0) relativeX += _regionSize;
                if (relativeZ < 0) relativeZ += _regionSize;
                
                int rawValue = regionMap.GetInt(relativeX, relativeZ);

                // If a processing function was provided, use it. Otherwise, use the raw value.
                result[z * sizeX + x] = _postProcessFunc != null 
                    ? _postProcessFunc(rawValue, currentBlockX, currentBlockZ) 
                    : rawValue;
            }
        }

        return result;
    }
}
