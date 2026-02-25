using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace EarthMapCreator;

public class Terrain : ModSystem
{
    private ICoreServerAPI _api;
    
    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        api.Event.ChunkColumnGeneration(Event_OnChunkColumnGeneration, EnumWorldGenPass.Terrain, "standard");
    }

    private void Event_OnChunkColumnGeneration(IChunkColumnGenerateRequest request)
    {
        int halfChunkSize = _api.WorldManager.ChunkSize / 2;
        
        int chunkX = request.ChunkX;
        int chunkZ = request.ChunkZ;
        
        int regionX = chunkX / halfChunkSize;
        int regionZ = chunkZ / halfChunkSize;
        
        GenerateTerrain(request, regionX, regionZ);
    }

    protected void GenerateTerrain(IChunkColumnGenerateRequest request, int regionX, int regionZ)
    {
        var layers = EarthMapCreator.Layers;
        int chunkSize = _api.WorldManager.ChunkSize;

        // --- SAFETY CHECK ---
        if (regionX < 0 || regionX >= layers.HeightMap.IntValues.Length || 
            regionZ < 0 || regionZ >= layers.HeightMap.IntValues[0].Length)
        {
            return;
        }

        // Get all required maps for this region
        IntDataMap2D completeTopoMap = layers.CompleteTopoMap.IntValues[regionX][regionZ];
        IntDataMap2D heightMap = layers.HeightMap.IntValues[regionX][regionZ];
        IntDataMap2D lakeMaskMap = layers.LakeMaskMap.IntValues[regionX][regionZ];
        IntDataMap2D landMaskMap = layers.LandMaskMap.IntValues[regionX][regionZ];
        IntDataMap2D oceanBathyMap = layers.OceanBathyMap.IntValues[regionX][regionZ];
        IntDataMap2D riverMap = layers.RiverMap.IntValues[regionX][regionZ]; 
        
        // Get chunk data and config
        IServerChunk[] chunks = request.Chunks;
        var chunkX = request.ChunkX;
        var chunkZ = request.ChunkZ;
        var config = GlobalConfig.GetInstance(_api);
        var blockLayerConfig = BlockLayerConfig.GetInstance(_api);
        int bedrock = config.mantleBlockId;
        int rock = config.defaultRockId;
        int water = config.waterBlockId;
        int saltWater = config.saltWaterBlockId;
        int seaLevel = 92;
        
        // Cut maps to chunk size
        int[,] bisectedCompleteTopoMap = CutHeightMapForChunk(completeTopoMap, new Vec2i(chunkX, chunkZ), new Vec2i(regionX, regionZ));
        int[,] bisectedHeightMap = CutHeightMapForChunk(heightMap, new Vec2i(chunkX, chunkZ), new Vec2i(regionX, regionZ));
        int[,] bisectedLakeMaskMap = CutHeightMapForChunk(lakeMaskMap, new Vec2i(chunkX, chunkZ), new Vec2i(regionX, regionZ));
        int[,] bisectedLandMaskMap = CutHeightMapForChunk(landMaskMap, new Vec2i(chunkX, chunkZ), new Vec2i(regionX, regionZ));
        int[,] bisectedOceanBathyMap = CutHeightMapForChunk(oceanBathyMap, new Vec2i(chunkX, chunkZ), new Vec2i(regionX, regionZ));
        int[,] bisectedRiverMap = CutHeightMapForChunk(riverMap, new Vec2i(chunkX, chunkZ), new Vec2i(regionX, regionZ));
        
        // --- Determine max Y for loop boundary ---
        var maxY = int.MinValue;
        for (int lx = 0; lx < chunkSize; lx++)
        {
            for (int lz = 0; lz < chunkSize; lz++)
            {
                bool isLand = bisectedLandMaskMap[lx, lz] > 0;
                int surfaceHeight;

                if (!isLand) { // Ocean surface is always sea level
                    surfaceHeight = seaLevel;
                } else { // Land or Lake surface is from the heightmap
                    surfaceHeight = bisectedHeightMap[lx, lz];
                }
                
                if (surfaceHeight > maxY) maxY = surfaceHeight;
            }
        }

        int mapSizeY = _api.WorldManager.MapSizeY;
        
        ushort[] rainHeightMap = chunks[0].MapChunk.RainHeightMap;
        ushort[] terrainHeightMap = chunks[0].MapChunk.WorldGenTerrainHeightMap;
        
        // Bedrock Layer
        chunks[0].Data.SetBlockBulk(0, chunkSize, chunkSize, bedrock);
        
        // --- Fill layers column by column from bedrock up ---
        for (int lx = 0; lx < chunkSize; lx++)
        {
            for (int lz = 0; lz < chunkSize; lz++)
            {
                int mapIdx = ChunkIndex2d(lx, lz);
                
                bool isLand = bisectedLandMaskMap[lx, lz] > 0;
                bool isLake = bisectedLakeMaskMap[lx, lz] > 0;
                bool isRiver = bisectedRiverMap[lx, lz] > 0;

                int groundHeight;
                int surfaceHeight;
                int fluidBlockId = 0; // 0 means no fluid

                // Determine ground, surface, and fluid type for the current column
                if (!isLand) // Case 1: Ocean
                {
                    groundHeight = bisectedOceanBathyMap[lx, lz] - 1;
                    surfaceHeight = seaLevel;
                    fluidBlockId = saltWater;
                }
                else if (isRiver && !isLake) // Case 2: River
                {
                    // 1. Carve the heightmap down with the river pixels
                    //    We'll set groundHeight to the new carved riverbed height.
                    int originalHeight = bisectedHeightMap[lx, lz];
                    int rawBrightness = bisectedRiverMap[lx, lz];
                    float normalizedDepth = (float)rawBrightness / 255.0f;
                    int carveDepth = (int)Math.Round(normalizedDepth * (float)Config.RiverDepth);
                    if (rawBrightness > 0 && carveDepth == 0)
                    {
                        carveDepth = 1;
                    }
                    groundHeight = originalHeight - carveDepth;

                    // 2. if the carve causes the riverbed to go below sea level,
                    //    place water up to sea level to fill the river channel
                    if (groundHeight < seaLevel)
                    {
                        surfaceHeight = seaLevel;
                        fluidBlockId = water;
                        // groundHeight remains the carved riverbed height
                    }
                    else // Otherwise, the river is a dry channel above sea level
                    {
                        surfaceHeight = groundHeight; // Surface is the same as the ground
                        fluidBlockId = 0; // No water
                    }
                }
                else if (!isLake) // Case 3: Dry Land
                {
                    groundHeight = bisectedHeightMap[lx, lz];
                    surfaceHeight = groundHeight;
                }
                else // Case 4: Lake
                {
                    int topoHeight = bisectedCompleteTopoMap[lx, lz];
                    groundHeight = (topoHeight > 0) ? topoHeight : bisectedOceanBathyMap[lx, lz];
                    surfaceHeight = bisectedHeightMap[lx, lz];
                    fluidBlockId = water;
                }

                // Set the engine's heightmaps
                terrainHeightMap[mapIdx] = (ushort)groundHeight;
                rainHeightMap[mapIdx] = (ushort)surfaceHeight;

                // Generate the column based on the determined heights
                for (int yy = 1; yy <= maxY + 1; yy++)
                {
                    if (yy >= mapSizeY) continue;
                    int chunkIndex = yy / chunkSize;
                    if (chunkIndex >= chunks.Length) continue;
                    var chunkData = chunks[chunkIndex].Data;
                    int ly = yy % chunkSize;
                    int chunkIdx = ChunkIndex3d(lx, ly, lz);

                    if (yy <= groundHeight)
                    {
                        chunkData[chunkIdx] = rock;
                    }
                    else if (yy <= surfaceHeight)
                    {
                        // This block will only be entered for ocean or lakes
                        chunkData.SetFluid(chunkIdx, fluidBlockId);
                    }
                    else
                    {
                        chunkData[chunkIdx] = 0; // Air
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ChunkIndex3d(int x, int y, int z)
    {
        int chunkSize = _api.WorldManager.ChunkSize;
        return (y * chunkSize + z) * chunkSize + x;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ChunkIndex2d(int x, int z)
    {
        int chunksize = _api.WorldManager.ChunkSize;
        return z * chunksize + x;
    }
    
    private int[,] CutHeightMapForChunk(IntDataMap2D heightMap, Vec2i chunkCoords, Vec2i regionCoords)
    {
        int chunkSize = _api.WorldManager.ChunkSize;
        int chunkSized2 = chunkSize / 2;
        
        int[,] chunkElevation = new int[chunkSize, chunkSize];

        // top left is least most 
        // e.g. region x/z              1000, 1000
        // topleft chunk x/z            16000,16000 
        // bottom right chunk x/z       16015,16015
        // global top left chunk coordinate of this region
        Vec2i regionTopLeftChunk = new Vec2i(
            chunkSized2 * regionCoords.X,
            chunkSized2 * regionCoords.Y
        );
        
        // subtract 
        Vec2i localChunk = chunkCoords - regionTopLeftChunk;
        
        int maxX = (1+localChunk.X) * chunkSize;
        int maxZ = (1+localChunk.Y) * chunkSize;

        int minX = localChunk.X * chunkSize;
        int minZ = localChunk.Y * chunkSize;
        
        int lx = 0;
        int lz = 0;
        for (int x = minX; x < maxX; x++)
        {
            for (int z = minZ; z < maxZ; z++)
            {
                int height = heightMap.GetInt(x, z);
                chunkElevation[lx, lz] = height;
                lz++;
            }

            lx++;
            lz = 0;
        }
        
        return chunkElevation;
    }

    private bool IsFreshWaterHere(GlobalConfig config, IntDataMap2D riverMap, int x, int z)
    {
        return riverMap.GetInt(x, z) > 0;
    }
}