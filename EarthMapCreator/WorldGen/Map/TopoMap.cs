using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vintagestory.API.Datastructures;

namespace EarthMapCreator;

/// <summary>
/// Loads a topography map for final terrain shape, which is identical to the heightmap,
/// except it has the lakebeds and river carving.
/// Value of 0 indicates no data for this pixel.
/// </summary>
public class TopoMap : DataMap<Rgb48>
{
    const int SeaLevel = 92;
    private const int MaxHeight = 180;
    const int HeightRange = MaxHeight - SeaLevel;

    public TopoMap(string filePath, string landcoverFile) : base(filePath)
    {
        Image<Rgb24> landcoverBmp = LoadBitmap<Rgb24>(landcoverFile);

        var watch = System.Diagnostics.Stopwatch.StartNew();
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
                        Rgb24 lcPixel = landcoverBmp[posX, posZ];
                        Rgb48 heightPixel = Bitmap[posX, posZ];

                        bool hasTopoData = heightPixel.R > 0;
                        int finalHeight = 0; // Default to 0 (no data)

                        if (hasTopoData)
                        {
                            float heightFraction = (heightPixel.R / 65535.0f);
                            int calculatedHeight = SeaLevel + (int)Math.Round(HeightRange * heightFraction);
                            
                            finalHeight = calculatedHeight - 1;
                        }
                        
                        IntValues[x][z].SetInt(i, j, finalHeight);
                    }
                }
            }
        }

        landcoverBmp.Dispose();
        Bitmap.Dispose();
        watch.Stop();
        
        Console.WriteLine("Created complete topography map in {0}ms", watch.ElapsedMilliseconds);
    }
}