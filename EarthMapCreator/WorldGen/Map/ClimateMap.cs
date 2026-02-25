using SixLabors.ImageSharp.PixelFormats;
using Vintagestory.API.Datastructures;

namespace EarthMapCreator;

public class ClimateMap : DataMap<Rgb24>
{
    public ClimateMap(string filePath) : base(filePath)
    {
        // This class now reads a Köppen-Geiger color map
        // and converts its colors to your 8 fixed climate zones.
        
        int xRegions = Bitmap.Width / 512;
        int zRegions = Bitmap.Height / 512;
        IntValues = new IntDataMap2D[xRegions][];
        
        for (int x = 0; x < xRegions; x++)
        {
            IntValues[x] = new IntDataMap2D[zRegions];
            for (int z = 0; z < zRegions; z++)
            {
                IntValues[x][z] = IntDataMap2D.CreateEmpty();
                IntValues[x][z].Size = 512;
                IntValues[x][z].Data = new int[512 * 512];
                
                for (int i = 0; i < 512; i++)
                {
                    for (int j = 0; j < 512; j++)
                    {
                        int posX = x * 512 + i;
                        int posZ = z * 512 + j;
                        
                        // 1. Read the pixel color from the map
                        Rgb24 pixel = Bitmap[posX, posZ];

                        // 2. Get the fixed climate zone ID from the ClimateMatcher
                        // We pass the pixel directly to the matcher.
                        ClimateZone zone = ClimateMatcher.GetZoneFromKoppenColor(pixel);
                        int zoneId = (int)zone;

                        // 3. Store the Zone ID (0-7) in the map
                        IntValues[x][z].SetInt(i, j, zoneId);
                    }
                }
            }
        }
        
        Bitmap.Dispose();
    }
}