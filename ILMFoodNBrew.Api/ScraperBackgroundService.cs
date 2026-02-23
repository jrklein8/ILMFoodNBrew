using System.Text.Json;
using ILMFoodNBrew.Scraper;
using ILMFoodNBrew.Shared;

namespace ILMFoodNBrew.Api;

public class ScraperBackgroundService : BackgroundService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ScraperBackgroundService> _logger;

    public ScraperBackgroundService(IWebHostEnvironment env, ILogger<ScraperBackgroundService> logger)
    {
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once on startup
        await RunScraper(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = GetNextRunTime(now);
            var delay = nextRun - now;

            _logger.LogInformation("Next scrape scheduled for {NextRun} (in {Hours:F1} hours)",
                nextRun, delay.TotalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await RunScraper(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunScraper(CancellationToken ct)
    {
        _logger.LogInformation("Starting food truck scraper...");
        try
        {
            var scraper = new FoodTruckScraper();
            var articleUrl = await scraper.FindLatestArticleUrl();
            if (articleUrl == null)
            {
                _logger.LogWarning("Could not find a Food Truck Tracker article");
                return;
            }

            _logger.LogInformation("Scraping article: {Url}", articleUrl);
            var data = await scraper.ScrapeArticle(articleUrl);
            _logger.LogInformation("Found {Trucks} trucks with {Appearances} appearances",
                data.Trucks.Count, data.AllAppearances.Count);

            var geocoder = new Geocoder();
            await geocoder.GeocodeLocations(data);

            var outputDir = Path.Combine(_env.WebRootPath, "data");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, "trucks.json");

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(outputPath, json, ct);
            _logger.LogInformation("Scraper complete. Data written to {Path}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scraper failed");
        }
    }

    private static DateTime GetNextRunTime(DateTime now)
    {
        // Schedule: run every Friday at 9 AM, and also every 6 hours on Fridays
        // On non-Fridays, run once per day at 6 AM (to catch any late updates)

        if (now.DayOfWeek == DayOfWeek.Friday)
        {
            // On Fridays, run every 3 hours starting at 9 AM
            var fridaySlots = new[] { 9, 12, 15, 18, 21 };
            foreach (var hour in fridaySlots)
            {
                var candidate = now.Date.AddHours(hour);
                if (candidate > now)
                    return candidate;
            }
        }

        // Next occurrence: either next Friday at 9 AM, or tomorrow at 6 AM
        var tomorrow6am = now.Date.AddDays(1).AddHours(6);

        // Find next Friday
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilFriday == 0) daysUntilFriday = 7; // already past Friday's last slot
        var nextFriday9am = now.Date.AddDays(daysUntilFriday).AddHours(9);

        // Return whichever is sooner
        return tomorrow6am < nextFriday9am ? tomorrow6am : nextFriday9am;
    }
}
