using SixLabors.ImageSharp.PixelFormats;

namespace EarthMapCreator;

// 1. A clean enum to identify your new fixed zones
// (This remains unchanged)
public enum ClimateZone
{
    IceCap = 0,
    Tundra = 1,
    HotDesert = 2,
    Steppe = 3,
    Boreal = 4,
    WarmTemperate = 5,
    CoolTemperate = 6,
    Savannah = 7,
    TropicalRainforest = 8,
    Mediterranean = 9,
    ColdDesert = 10,
    SemiDesert = 11
}

// 2. The helper lookup function
// This class is now responsible for mapping 
// Köppen-Geiger colors to your ClimateZone enum.
public static class ClimateMatcher
{
    // This function takes a pixel and returns the corresponding ClimateZone
    public static ClimateZone GetZoneFromKoppenColor(Rgb24 pixel)
    {
        // A switch on a (R, G, B) tuple is a very clean
        // and efficient way to handle this lookup.
        switch ((pixel.R, pixel.G, pixel.B))
        {
            // Köppen 1: Af (Tropical rainforest) [0, 0, 255]
            case (0, 0, 255):
            // Köppen 2: Am (Tropical monsoon) [0, 120, 255]
            case (0, 120, 255):
                return ClimateZone.TropicalRainforest;

            // Köppen 3: Aw (Tropical savanna) [70, 170, 250]
            case (70, 170, 250):
                return ClimateZone.Savannah;

            // Köppen 4: BWh (Hot desert) [255, 0, 0]
            case (255, 0, 0):
                return ClimateZone.HotDesert;
            
            // Köppen 5: BWk (Cold desert) [255, 150, 150]
            case (255, 150, 150):
                return ClimateZone.ColdDesert;
            
            // Köppen 6: BSh (Hot semi-arid) [245, 165, 0]
            case (245, 165, 0):
                return ClimateZone.SemiDesert;

            // Köppen 7: BSk (Cold semi-arid) [255, 220, 100]
            case (255, 220, 100):
                return ClimateZone.Steppe;
            
            case (255, 255, 0):   // 8: Csa
            case (200, 200, 0):   // 9: Csb
            case (150, 150, 0):   // 10: Csc
                return ClimateZone.Mediterranean;
                
            case (150, 255, 150): // 11: Cwa
            case (100, 200, 100): // 12: Cwb
            case (50, 150, 50):   // 13: Cwc
            case (200, 255, 80):  // 14: Cfa
            case (100, 255, 80):  // 15: Cfb
            case (50, 200, 0):    // 16: Cfc
                return ClimateZone.WarmTemperate;
            
            case (0, 255, 255):   // 25: Dfa
            case (55, 200, 255):  // 26: Dfb
            case (255, 0, 255):   // 17: Dsa
            case (200, 0, 200):   // 18: Dsb
            case (170, 175, 255): // 21: Dwa
            case (90, 120, 220):  // 22: Dwb
                return ClimateZone.CoolTemperate;

            case (150, 50, 150):  // 19: Dsc
            case (150, 100, 150): // 20: Dsd
            case (75, 80, 180):   // 23: Dwc
            case (50, 0, 135):    // 24: Dwd
            case (0, 125, 125):   // 27: Dfc
            case (0, 70, 95):     // 28: Dfd
                return ClimateZone.Boreal;

            // Köppen 29: ET (Tundra) [178, 178, 178]
            case (178, 178, 178):
                return ClimateZone.Tundra;

            // Köppen 30: EF (Ice cap) [102, 102, 102]
            case (102, 102, 102):
                return ClimateZone.IceCap;

            // Default fallback
            // This will catch the 'nv' (0,0,0) nodata value
            // and any other unexpected colors.
            default:
                return ClimateZone.Mediterranean;
        }
    }
    
    /// <summary>
    /// Converts a ClimateZone ID back into representative in-game
    /// temperature and rainfall values.
    /// </summary>
    /// <param name="zone">The ClimateZone enum (or int ID)</param>
    /// <returns>A tuple containing the (temp, rainRel) floats</returns>
    public static (float temp, float rainRel) GetClimateValues(ClimateZone zone)
    {
        switch (zone)
        {
            // Target: -50f to -17f, 0f to 1f
            case ClimateZone.IceCap:
                return (temp: -20f, rainRel: 0.5f);

            // Target: -17f to -10f, 0f to 1f
            case ClimateZone.Tundra:
                return (temp: -13f, rainRel: 0.3f);

            // Target: -50f to 100f, 0f to 0.12f
            case ClimateZone.HotDesert:
                return (temp: 25f, rainRel: 0.1f); // Hot and dry
            
            case ClimateZone.ColdDesert:
                return (temp: 15f, rainRel: 0.1f); // Hot and dry
            
            case ClimateZone.SemiDesert:
                return (temp: 22f, rainRel: 0.2f);

            // Target: -50f to 100f, 0.12f to 0.31f
            case ClimateZone.Steppe:
                return (temp: 17f, rainRel: 0.35f); // Mild and semi-dry

            // Target: -10f to 12f, 0.20f to 1f
            case ClimateZone.Boreal:
                return (temp: 1f, rainRel: 0.6f);

            // Target: 28f to 100f, 0.31f to 0.55f
            case ClimateZone.Savannah:
                return (temp: 27f, rainRel: 0.3f);

            // Target: 26f to 100f, 0.72f to 1f
            case ClimateZone.TropicalRainforest:
                return (temp: 27f, rainRel: 0.9f);
            
            case ClimateZone.Mediterranean:
                return (temp: 19f, rainRel: 0.35f);

            // Fallback: Temperate Forest
            // (Based on C-climate ranges, e.g. 2f-22f, 0.37f-1f)
            case ClimateZone.WarmTemperate:
            default:
                return (temp: 15f, rainRel: 0.7f);
        }
    }
}