using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace EarthMapCreator;

public class EarthWorldGenerator : ModSystem
{
    private ICoreServerAPI _api;
    public static List<Vec2i> GeneratingRegions = new List<Vec2i>();

    public override void StartServerSide(ICoreServerAPI api)
    {
        this._api = api;
        InitCommands();
    }

    private void InitCommands()
    {
        _api.ChatCommands.GetOrCreate("earthmap")
            .WithDescription("Earth map commands")
            .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("generate")
                    .WithDescription("Generates the world map")
                    .HandleWith(Cmd_OnGenerate)
                .EndSubCommand()
                .BeginSubCommand("pos")
                    .RequiresPlayer()
                    .WithDescription("Info about current position")
                    .HandleWith(Cmd_OnPos)
                .EndSubCommand();
    }

    private void Generate() {
        Mod.Logger.Debug("Generating earth map");
        
        GeneratingRegions.Clear();
        
        // TODO: parallel
        int regionSize = _api.WorldManager.RegionSize;

        int startx = 0;
        int startz = 0;
        
        int width = EarthMapCreator.config.MapWidthBlocks / regionSize;
        int height = EarthMapCreator.config.MapHeightBlocks / regionSize;
        
        List<Vec2i> regionCoords = new List<Vec2i>();
        for (int x = startx; x < width + startx; x++) 
        {
            for (int z = startz; z < height + startz; z++) 
            {       
                var coords = new Vec2i(x, z);
                regionCoords.Add(coords);
            }
        }
        
        regionCoords.ForEach(v => GeneratingRegions.Add(v));
        TerraGenConfig.GenerateStructures = false;
        _api.WorldManager.AutoGenerateChunks = false;
        _api.WorldManager.SendChunks = false;

        int regionsLeftToLoad = regionCoords.Count;
        foreach (Vec2i regionCoord in regionCoords) 
        {
            _api.WorldManager.DeleteMapRegion(regionCoord.X, regionCoord.Y);
            List<Vec2i> chunkCoords = ComputeChunksForMapRegion(regionCoord.X, regionCoord.Y);

            foreach (Vec2i chunkCoord in chunkCoords)
            {
                _api.WorldManager.DeleteChunkColumn(chunkCoord.X, chunkCoord.Y);
            }
            
            int leftToLoad = chunkCoords.Count;
            
            foreach (Vec2i chunkCoord in chunkCoords)
            {
                _api.WorldManager.LoadChunkColumnPriority(chunkCoord.X, chunkCoord.Y, new ChunkLoadOptions()
                {
                    OnLoaded = () =>
                    {
                        leftToLoad--;

                        if (leftToLoad <= 0)
                        {
                            regionsLeftToLoad--;
                            GeneratingRegions.Remove(regionCoord);
                            if (regionsLeftToLoad <= 0)
                            {
                                _api.WorldManager.SendChunks = true;
                                Mod.Logger.Debug("Finished generating all regions");
                                _api.WorldManager.AutoGenerateChunks = true;
                            }
                            else
                            {
                                Mod.Logger.Debug($"Finished generating region {regionCoord.X}, {regionCoord.Y}, {regionsLeftToLoad} regions left to generate");
                            }
                        }
                    }
                });
            }
        }
    }
    private TextCommandResult Cmd_OnGenerate(TextCommandCallingArgs args) {
        try
        {
            if (!_api.Server.PauseThread("chunkdbthread"))
            {
                return TextCommandResult.Error("Failed to pause chunk gen thread");
            }

            Generate();
            _api.Server.ResumeThread("chunkdbthread");
            return TextCommandResult.Success("Generating map");
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to generate earth map.");
            Mod.Logger.Error(e);
            return TextCommandResult.Error("Operation failed");
        }
    }
    
    private TextCommandResult Cmd_OnPos(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player;
        BlockPos pos = player.Entity.Pos.AsBlockPos;
        
        int regionX = pos.X / _api.WorldManager.RegionSize;
        int regionZ = pos.Z / _api.WorldManager.RegionSize;

        int relativeX = pos.X - regionX * _api.WorldManager.RegionSize;
        int relativeZ = pos.Z - regionZ * _api.WorldManager.RegionSize;
        
        IntDataMap2D tree = EarthMapCreator.Layers.TreeMap.IntValues[regionX][regionZ];
        int treeHere = tree.GetInt(relativeX, relativeZ); 
        
        IntDataMap2D terrain = EarthMapCreator.Layers.HeightMap.IntValues[regionX][regionZ];
        int terrainHere = terrain.GetInt(relativeX, relativeZ);
        
        IntDataMap2D bathyMap = EarthMapCreator.Layers.OceanBathyMap.IntValues[regionX][regionZ];
        int bathyHere = bathyMap.GetInt(relativeX, relativeZ);
        
        IntDataMap2D topo = EarthMapCreator.Layers.CompleteTopoMap.IntValues[regionX][regionZ];
        int topoHere = topo.GetInt(relativeX, relativeZ);
        
        IntDataMap2D landMaskMap = EarthMapCreator.Layers.LandMaskMap.IntValues[regionX][regionZ];
        int landMaskHere = landMaskMap.GetInt(relativeX, relativeZ);
        
        IntDataMap2D lakeMaskMap = EarthMapCreator.Layers.LakeMaskMap.IntValues[regionX][regionZ];
        int lakeMaskHere = lakeMaskMap.GetInt(relativeX, relativeZ);
        
        int zoneId = EarthMapCreator.Layers.ClimateMap.IntValues[regionX][regionZ].GetInt(relativeX, relativeZ);
        ClimateZone zone = (ClimateZone)zoneId;
        (float temp, float rainRel) climate = ClimateMatcher.GetClimateValues(zone);
        
        String msg = $"At {pos.X}, {pos.Z}, (region {regionX}, {regionZ})\n" +
                     $"Climate - {zone}\n" +
                     $"Tree: {treeHere}\n";

        msg += $"Bathy (Height: {bathyHere})\n";
        msg += $"Land (Height: {terrainHere})\n";
        msg += $"Topo (Height: {topoHere})\n";
        msg += $"Land Mask: {landMaskHere}\n";
        msg += $"Lake Mask: {lakeMaskHere}\n";
        
        return TextCommandResult.Success(msg);
    }

    private List<Vec2i> ComputeChunksForMapRegion(int rx, int ry) 
    {
        List<Vec2i> chunkCoords = new List<Vec2i>();

        int regionChunkSize = _api.WorldManager.RegionSize / _api.WorldManager.ChunkSize;

        int cxrx = rx * regionChunkSize;
        int cyry = ry * regionChunkSize;

        for (int cx = cxrx; cx < cxrx + regionChunkSize; cx++) {
            for (int cy = cyry; cy < cyry + regionChunkSize; cy++) {
                Vec2i coordinate = new Vec2i(cx, cy);
                chunkCoords.Add(coordinate);
            }
        }

        return chunkCoords;
    }
}
