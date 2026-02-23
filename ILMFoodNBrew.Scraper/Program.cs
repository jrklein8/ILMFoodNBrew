using System.Text.Json;
using ILMFoodNBrew.Scraper;
using ILMFoodNBrew.Shared;

// Find solution directory by walking up from the binary location
string? solutionDir = AppContext.BaseDirectory;
while (solutionDir != null && !File.Exists(Path.Combine(solutionDir, "ILMFoodNBrew.sln")))
    solutionDir = Path.GetDirectoryName(solutionDir);
solutionDir ??= Directory.GetCurrentDirectory();
var outputDir = args.Length > 0 ? args[0] : Path.Combine(solutionDir, "ILMFoodNBrew.Api", "wwwroot", "data");
Directory.CreateDirectory(outputDir);

Console.WriteLine("ILM Food N Brew - Scraper");
Console.WriteLine("=========================");

var scraper = new FoodTruckScraper();

// Step 1: Find the latest Food Truck Tracker article
Console.WriteLine("Finding latest Food Truck Tracker article...");
var articleUrl = await scraper.FindLatestArticleUrl();
if (articleUrl == null)
{
    Console.WriteLine("ERROR: Could not find a Food Truck Tracker article.");
    return 1;
}
Console.WriteLine($"Found article: {articleUrl}");

// Step 2: Scrape the article
Console.WriteLine("Scraping article...");
var data = await scraper.ScrapeArticle(articleUrl);
Console.WriteLine($"Found {data.Trucks.Count} trucks with {data.AllAppearances.Count} total appearances");
Console.WriteLine($"Found {data.Locations.Count} known locations");

// Step 3: Geocode locations that don't have coordinates
Console.WriteLine("Geocoding locations...");
var geocoder = new Geocoder();
await geocoder.GeocodeLocations(data);
var geocoded = data.Locations.Values.Count(l => l.Latitude.HasValue);
Console.WriteLine($"Geocoded {geocoded}/{data.Locations.Count} locations");

// Step 4: Write output
var outputPath = Path.Combine(outputDir, "trucks.json");
var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});
await File.WriteAllTextAsync(outputPath, json);
Console.WriteLine($"Data written to {outputPath}");

return 0;
