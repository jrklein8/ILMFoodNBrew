using System.Text.Json;
using ILMFoodNBrew.Api;
using ILMFoodNBrew.Shared;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5200";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddHostedService<ScraperBackgroundService>();

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

app.MapGet("/api/trucks", async () =>
{
    var dataPath = Path.Combine(app.Environment.WebRootPath, "data", "trucks.json");
    if (!File.Exists(dataPath))
        return Results.Json(new { error = "No data available. Run the scraper first." }, statusCode: 404);

    var json = await File.ReadAllTextAsync(dataPath);
    var data = JsonSerializer.Deserialize<ScrapedData>(json, jsonOptions);
    return Results.Ok(data);
});

app.MapGet("/api/trucks/today", async () =>
{
    var dataPath = Path.Combine(app.Environment.WebRootPath, "data", "trucks.json");
    if (!File.Exists(dataPath))
        return Results.Json(new { error = "No data available. Run the scraper first." }, statusCode: 404);

    var json = await File.ReadAllTextAsync(dataPath);
    var data = JsonSerializer.Deserialize<ScrapedData>(json, jsonOptions);
    if (data == null) return Results.NotFound();

    var today = DateOnly.FromDateTime(DateTime.Now);
    var todayAppearances = data.AllAppearances
        .Where(a => a.Date == today)
        .ToList();

    return Results.Ok(new { date = today.ToString("yyyy-MM-dd"), appearances = todayAppearances });
});

app.MapGet("/api/trucks/date/{date}", async (string date) =>
{
    var dataPath = Path.Combine(app.Environment.WebRootPath, "data", "trucks.json");
    if (!File.Exists(dataPath))
        return Results.Json(new { error = "No data available. Run the scraper first." }, statusCode: 404);

    if (!DateOnly.TryParse(date, out var targetDate))
        return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

    var json = await File.ReadAllTextAsync(dataPath);
    var data = JsonSerializer.Deserialize<ScrapedData>(json, jsonOptions);
    if (data == null) return Results.NotFound();

    var appearances = data.AllAppearances
        .Where(a => a.Date == targetDate)
        .ToList();

    return Results.Ok(new { date = targetDate.ToString("yyyy-MM-dd"), appearances });
});

app.Run();
