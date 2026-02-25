using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vintagestory.API.Datastructures;

namespace EarthMapCreator
{
    public class OceanBathymetryMap : DataMap<Rgb48>
    { 
        public OceanBathymetryMap(string filePath, string landMaskMap) : base(filePath)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int xRegions = Bitmap.Width / 512;
            int zRegions = Bitmap.Height / 512;
            IntValues = new IntDataMap2D[xRegions][];
            
            Image<Rgb24> landcoverBmp = LoadBitmap<Rgb24>(landMaskMap);
            
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
                            
                            // Get the pixel from the bathymetry map.
                            Rgb48 bathyPixel = Bitmap[posX, posZ];

                            // The R channel represents the block height. Since the map is a 16-bit image (Rgb48),
                            // we must scale the 16-bit value (0-65535) down to a standard 8-bit block height (0-255).
                            // This prevents excessively high values from being used as the ocean floor height.
                            // A value of 0 indicates no data for this pixel.
                            if (lcPixel.R < 1 && bathyPixel.R > 1) //pixel outside the land mask and nonzero
                            {
                                int scaledHeight = Math.Clamp((int)Math.Floor(bathyPixel.R / 257.0), 50, 92);
                            
                                IntValues[x][z].SetInt(i, j, (scaledHeight));
                            }
                            else
                            {
                                IntValues[x][z].SetInt(i, j, (92));
                            }
                        }
                    }
                }
            }

            Bitmap.Dispose();
            watch.Stop();
            
            Console.WriteLine("Created direct bathymetry map in {0}ms", watch.ElapsedMilliseconds);
        }
    }
}
