using System.Net.Http.Json;
using System.Text.Json;
using ILMFoodNBrew.Shared;

namespace ILMFoodNBrew.Scraper;

public class Geocoder
{
    private static readonly HttpClient Http = CreateHttpClient();
    private const string CacheFile = "geocode_cache.json";

    // Bounding box for New Hanover / Pender / Brunswick tri-county area
    // Roughly: south of Jacksonville, north of Myrtle Beach, east to the coast, west to ~78.3
    private const double BoundsMinLat = 33.75;   // southern Brunswick County
    private const double BoundsMaxLat = 34.65;   // northern Pender County
    private const double BoundsMinLon = -78.35;   // western Brunswick County
    private const double BoundsMaxLon = -77.55;   // coast / barrier islands

    // Nominatim viewbox param: left,top,right,bottom (lon,lat,lon,lat)
    private static readonly string Viewbox = $"{BoundsMinLon},{BoundsMaxLat},{BoundsMaxLon},{BoundsMinLat}";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "ILMFoodNBrew/1.0 (food truck tracker)");
        return client;
    }

    public async Task GeocodeLocations(ScrapedData data)
    {
        var cache = await LoadCache();
        var newEntries = 0;
        var invalidated = 0;

        // First pass: invalidate any cached entries that are out of bounds
        var keysToRemove = new List<string>();
        foreach (var (key, coord) in cache)
        {
            if (!IsInBounds(coord.Lat, coord.Lon))
                keysToRemove.Add(key);
        }
        foreach (var key in keysToRemove)
        {
            cache.Remove(key);
            invalidated++;
        }
        if (invalidated > 0)
            Console.WriteLine($"  Invalidated {invalidated} out-of-bounds cached entries");

        foreach (var (key, loc) in data.Locations)
        {
            // Reset any existing out-of-bounds coordinates
            if (loc.Latitude.HasValue && !IsInBounds(loc.Latitude.Value, loc.Longitude!.Value))
            {
                loc.Latitude = null;
                loc.Longitude = null;
            }

            if (loc.Latitude.HasValue && loc.Longitude.HasValue) continue;

            var cacheKey = $"{loc.Name}|{loc.Address}";
            if (cache.TryGetValue(cacheKey, out var cached))
            {
                loc.Latitude = cached.Lat;
                loc.Longitude = cached.Lon;
                continue;
            }

            // Respect Nominatim rate limit (1 req/sec)
            if (newEntries > 0)
                await Task.Delay(1100);

            var result = await TryGeocodeWithStrategies(loc.Name, loc.Address);
            if (result != null)
            {
                loc.Latitude = result.Value.Lat;
                loc.Longitude = result.Value.Lon;
                cache[cacheKey] = result.Value;
                Console.WriteLine($"  Geocoded: {loc.Name} -> ({result.Value.Lat:F4}, {result.Value.Lon:F4})");
            }
            else
            {
                Console.WriteLine($"  NOT FOUND (tri-county): {loc.Name} ({loc.Address})");
            }
            newEntries++;
        }

        // Update appearances with geocoded coordinates
        foreach (var appearance in data.AllAppearances)
        {
            var locKey = NormalizeLocationName(appearance.LocationName);
            if (data.Locations.TryGetValue(locKey, out var loc))
            {
                appearance.Latitude = loc.Latitude;
                appearance.Longitude = loc.Longitude;
                appearance.Address ??= loc.Address;
            }
        }

        if (newEntries > 0 || invalidated > 0)
            await SaveCache(cache);
    }

    /// <summary>
    /// Try multiple geocoding strategies, all bounded to the tri-county area.
    /// </summary>
    private async Task<GeoCoord?> TryGeocodeWithStrategies(string name, string address)
    {
        // Strategy 1: Full address with viewbox bounded to tri-county
        var result = await SearchNominatim($"{address}, NC", bounded: true);
        if (result != null) return result;

        // Strategy 2: Try with city names that are in the tri-county area
        string[] localCities = [
            "Wilmington, NC", "Leland, NC", "Carolina Beach, NC", "Kure Beach, NC",
            "Wrightsville Beach, NC", "Castle Hayne, NC", "Hampstead, NC",
            "Surf City, NC", "Sneads Ferry, NC", "Holly Ridge, NC",
            "Southport, NC", "Oak Island, NC", "Bolivia, NC", "Burgaw, NC",
            "Rocky Point, NC", "Topsail Beach, NC", "Ocean Isle Beach, NC"
        ];

        // Extract city from address if present (e.g. "113 Village Rd. NE, Leland")
        foreach (var city in localCities)
        {
            var cityName = city.Replace(", NC", "");
            if (address.Contains(cityName, StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(1100);
                result = await SearchNominatim($"{address}, NC", bounded: true);
                if (result != null) return result;
                break;
            }
        }

        // Strategy 3: Try the place name + "Wilmington NC" (for businesses)
        await Task.Delay(1100);
        result = await SearchNominatim($"{name}, Wilmington, NC", bounded: true);
        if (result != null) return result;

        // Strategy 4: Just the street address with bounded search (no city appended)
        await Task.Delay(1100);
        result = await SearchNominatim(address, bounded: true);
        if (result != null) return result;

        return null;
    }

    private async Task<GeoCoord?> SearchNominatim(string query, bool bounded)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var apiUrl = $"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=3&countrycodes=us";

            if (bounded)
                apiUrl += $"&viewbox={Viewbox}&bounded=1";

            var results = await Http.GetFromJsonAsync<JsonElement[]>(apiUrl);
            if (results == null || results.Length == 0) return null;

            // Check each result for bounds compliance
            foreach (var r in results)
            {
                var lat = double.Parse(r.GetProperty("lat").GetString()!);
                var lon = double.Parse(r.GetProperty("lon").GetString()!);
                if (IsInBounds(lat, lon))
                    return new GeoCoord(lat, lon);
            }
        }
        catch { }
        return null;
    }

    private static bool IsInBounds(double lat, double lon)
    {
        return lat >= BoundsMinLat && lat <= BoundsMaxLat
            && lon >= BoundsMinLon && lon <= BoundsMaxLon;
    }

    private static async Task<Dictionary<string, GeoCoord>> LoadCache()
    {
        if (!File.Exists(CacheFile)) return new();
        try
        {
            var json = await File.ReadAllTextAsync(CacheFile);
            return JsonSerializer.Deserialize<Dictionary<string, GeoCoord>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static async Task SaveCache(Dictionary<string, GeoCoord> cache)
    {
        var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(CacheFile, json);
    }

    private static string NormalizeLocationName(string name)
    {
        return System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]", "");
    }

    public record struct GeoCoord(double Lat, double Lon);
}
