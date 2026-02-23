using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ILMFoodNBrew.Shared;

namespace ILMFoodNBrew.Scraper;

public class FoodTruckScraper
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    /// <summary>
    /// Finds the latest "Food Truck Tracker" article URL from the brews-and-bites section.
    /// </summary>
    public async Task<string?> FindLatestArticleUrl()
    {
        var html = await Http.GetStringAsync("https://portcitydaily.com/brews-and-bites/");

        // Find all dated food-truck-tracker URLs (e.g. /brews-and-bites/2026/02/21/food-truck-tracker-feb-20-27/)
        var matches = Regex.Matches(html,
            @"https://portcitydaily\.com/brews-and-bites/\d{4}/\d{2}/\d{2}/food-truck-tracker[^""']*",
            RegexOptions.IgnoreCase);

        var urls = new HashSet<string>();
        foreach (Match m in matches)
            urls.Add(m.Value.TrimEnd('/'));

        // Sort descending by date in URL to get the latest
        var sorted = urls
            .OrderByDescending(u => u)
            .ToList();

        return sorted.FirstOrDefault();
    }

    /// <summary>
    /// Scrapes a Food Truck Tracker article and extracts all truck data.
    /// </summary>
    public async Task<ScrapedData> ScrapeArticle(string url)
    {
        var html = await Http.GetStringAsync(url);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new ScrapedData
        {
            ScrapedAt = DateTime.UtcNow,
            SourceUrl = url
        };

        // Extract date range from title
        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "";
        var rangeMatch = Regex.Match(title, @"Food Truck Tracker:\s*(.+?)\s*\|");
        if (rangeMatch.Success)
            data.DateRange = WebUtility.HtmlDecode(rangeMatch.Groups[1].Value).Trim();

        // The article content is structured as:
        // <p><strong><a href="...">TRUCK NAME</a></strong> Description</p>
        // <ul class="wp-block-list"><li>Date — Location, Time</li>...</ul>
        // ...
        // <h2>Find a location</h2>
        // <ul><li>Location — Address</li>...</ul>

        // Get all <p> and <ul> nodes in document order from the article body
        // We'll process them sequentially to pair truck info with their schedules
        var body = doc.DocumentNode;

        // First, parse the "Find a location" section to build a location dictionary
        ParseLocations(body, data);

        // Add manually known locations not listed on the site
        AddManualLocations(data);

        // Then parse truck schedules
        ParseTrucks(body, data);

        return data;
    }

    private void AddManualLocations(ScrapedData data)
    {
        // Locations not listed on Port City Daily's "Find a location" section
        var manualLocations = new Dictionary<string, LocationInfo>
        {
            ["lbcbottleshop"] = new()
            {
                Name = "LBC Bottle Shop",
                Address = "15670 US Highway 17, Hampstead, NC 28443",
                Latitude = 34.3878,
                Longitude = -77.6822
            },
            ["broomtailcraftbrewery"] = new()
            {
                Name = "Broomtail Craft Brewery",
                Address = "6404 Amsterdam Way, Wilmington, NC 28405",
                Latitude = 34.2599,
                Longitude = -77.8478
            },
            ["capefearcommunitycollegenorthcampus"] = new()
            {
                Name = "Cape Fear Community College North Campus",
                Address = "4500 Blue Clay Rd, Castle Hayne, NC 28429",
                Latitude = 34.3221,
                Longitude = -77.8777
            },
        };

        foreach (var (key, loc) in manualLocations)
        {
            if (!data.Locations.ContainsKey(key) ||
                (!data.Locations[key].Latitude.HasValue && loc.Latitude.HasValue))
                data.Locations[key] = loc;
        }
    }

    private void ParseLocations(HtmlNode body, ScrapedData data)
    {
        // Find the "Find a location" heading
        var allNodes = body.SelectNodes("//h2 | //h1 | //h3");
        if (allNodes == null) return;
        HtmlNode? locationHeader = null;

        foreach (var node in allNodes)
        {
            var text = WebUtility.HtmlDecode(node.InnerText).Trim();
            if (text.Contains("Find a location", StringComparison.OrdinalIgnoreCase))
            {
                locationHeader = node;
                break;
            }
        }

        if (locationHeader == null) return;

        // The <ul> immediately following the heading contains location entries
        var nextSibling = locationHeader.NextSibling;
        while (nextSibling != null && nextSibling.Name != "ul")
        {
            nextSibling = nextSibling.NextSibling;
        }

        if (nextSibling == null) return;

        var listItems = nextSibling.SelectNodes(".//li");
        if (listItems == null) return;

        foreach (var li in listItems)
        {
            var text = WebUtility.HtmlDecode(li.InnerText).Trim();
            // Format: "Location Name — Address" (em dash U+2014 is the separator)
            var emIdx = text.IndexOf('\u2014');
            if (emIdx < 0) emIdx = text.IndexOf('\u2013'); // fallback to en dash
            if (emIdx > 0)
            {
                var name = text[..emIdx].Trim();
                var address = text[(emIdx + 1)..].Trim();
                var key = NormalizeLocationName(name);
                data.Locations[key] = new LocationInfo
                {
                    Name = name,
                    Address = address
                };
            }
        }
    }

    private void ParseTrucks(HtmlNode body, ScrapedData data)
    {
        // Find the "Weekly Schedules" heading to start parsing
        var allElements = body.SelectNodes("//*[self::h1 or self::h2 or self::h3 or self::p or self::ul]");
        if (allElements == null) return;

        var startParsing = false;
        FoodTruck? currentTruck = null;

        foreach (var elem in allElements)
        {
            var text = WebUtility.HtmlDecode(elem.InnerText).Trim();

            // Start after "Weekly Schedules"
            if (text.Contains("Weekly Schedules", StringComparison.OrdinalIgnoreCase))
            {
                startParsing = true;
                continue;
            }

            // Stop at "Find a location"
            if (text.Contains("Find a location", StringComparison.OrdinalIgnoreCase))
                break;

            if (!startParsing) continue;

            // <p> tags contain truck name (in <strong>/<a>) and description
            if (elem.Name == "p")
            {
                var strongNode = elem.SelectSingleNode(".//strong");
                if (strongNode == null) continue;

                var truckName = WebUtility.HtmlDecode(strongNode.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(truckName)) continue;

                // Get the Facebook/website URL
                var linkNode = elem.SelectSingleNode(".//a[@href]");
                var fbUrl = linkNode?.GetAttributeValue("href", null);

                // Get description - text after the strong/a tag
                var fullText = WebUtility.HtmlDecode(elem.InnerText).Trim();
                var description = fullText.Length > truckName.Length
                    ? fullText[(truckName.Length)..].Trim()
                    : "";

                currentTruck = new FoodTruck
                {
                    Name = truckName,
                    Description = description,
                    FacebookUrl = fbUrl
                };
                data.Trucks.Add(currentTruck);
            }
            // <ul> tags contain schedule entries
            else if (elem.Name == "ul" && currentTruck != null)
            {
                var listItems = elem.SelectNodes(".//li");
                if (listItems == null) continue;

                foreach (var li in listItems)
                {
                    var liText = WebUtility.HtmlDecode(li.InnerText).Trim();
                    ParseScheduleEntry(liText, currentTruck, data);
                }
            }
        }
    }

    private void ParseScheduleEntry(string text, FoodTruck truck, ScrapedData data)
    {
        // Format: "February 20 — Location Name, Time Range"
        // Date/location separator is em dash (U+2014), time range uses en dash (U+2013)
        // Split ONLY on em dash to preserve the time range
        var emDashIdx = text.IndexOf('\u2014');
        if (emDashIdx < 0)
        {
            // Fallback: try splitting on " - " (space-dash-space)
            emDashIdx = text.IndexOf(" - ");
            if (emDashIdx < 0) return;
        }

        var dateStr = text[..emDashIdx].Trim();
        var scheduleStr = text[(emDashIdx + 1)..].Trim();

        // Parse the date
        var date = ParseDate(dateStr, data.DateRange);
        if (date == null) return;

        // Handle multiple stops separated by semicolons
        var stops = scheduleStr.Split(';');
        foreach (var stop in stops)
        {
            var trimmed = stop.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Parse: "Location Name, StartTime – EndTime"
            // Time separator is en dash (U+2013) or hyphen
            var timeMatch = Regex.Match(trimmed,
                @"^(.+?),\s*(\d{1,2}(?::\d{2})?\s*[ap]\.?m\.?|noon|midnight)\s*[\u2013\u2014\-]+\s*(\d{1,2}(?::\d{2})?\s*[ap]\.?m\.?|noon|midnight)",
                RegexOptions.IgnoreCase);

            string locationName;
            string startTime = "";
            string endTime = "";

            if (timeMatch.Success)
            {
                locationName = timeMatch.Groups[1].Value.Trim();
                startTime = NormalizeTime(timeMatch.Groups[2].Value.Trim());
                endTime = NormalizeTime(timeMatch.Groups[3].Value.Trim());
            }
            else
            {
                // Fallback: everything is the location name
                locationName = trimmed;
            }

            // Look up address
            var locKey = NormalizeLocationName(locationName);
            string? address = null;
            double? lat = null, lng = null;

            if (data.Locations.TryGetValue(locKey, out var locInfo))
            {
                address = locInfo.Address;
                lat = locInfo.Latitude;
                lng = locInfo.Longitude;
            }

            var appearance = new TruckAppearance
            {
                TruckName = truck.Name,
                Description = truck.Description,
                FacebookUrl = truck.FacebookUrl,
                Date = date.Value,
                LocationName = locationName,
                Address = address,
                Latitude = lat,
                Longitude = lng,
                StartTime = startTime,
                EndTime = endTime
            };

            truck.Appearances.Add(appearance);
            data.AllAppearances.Add(appearance);
        }
    }

    private DateOnly? ParseDate(string dateStr, string dateRange)
    {
        // Extract year from date range or use current year
        var year = DateTime.Now.Year;
        var yearMatch = Regex.Match(dateRange, @"(\d{4})");
        if (yearMatch.Success)
            year = int.Parse(yearMatch.Value);

        // Try parsing "February 20" format
        var formats = new[]
        {
            "MMMM d", "MMMM dd", "MMM d", "MMM dd",
            "MMMM d, yyyy", "MMM d, yyyy"
        };

        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact($"{dateStr}, {year}", $"{fmt}, yyyy",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return DateOnly.FromDateTime(dt);
            }
            if (DateTime.TryParseExact(dateStr, fmt,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return DateOnly.FromDateTime(new DateTime(year, dt.Month, dt.Day));
            }
        }

        return null;
    }

    private static string NormalizeTime(string time)
    {
        if (time.Equals("noon", StringComparison.OrdinalIgnoreCase)) return "12:00 PM";
        if (time.Equals("midnight", StringComparison.OrdinalIgnoreCase)) return "12:00 AM";

        // Clean up "5 p.m." -> "5:00 PM"
        var cleaned = time.Replace(".", "").Replace(" ", "").ToUpper();
        // "5PM" -> "5:00 PM", "11:30AM" -> "11:30 AM"
        var match = Regex.Match(cleaned, @"(\d{1,2})(?::(\d{2}))?(AM|PM)");
        if (match.Success)
        {
            var hour = match.Groups[1].Value;
            var minutes = match.Groups[2].Success ? match.Groups[2].Value : "00";
            var ampm = match.Groups[3].Value;
            return $"{hour}:{minutes} {ampm}";
        }

        return time;
    }

    private static string NormalizeLocationName(string name)
    {
        return Regex.Replace(name.ToLowerInvariant().Trim(), @"[^a-z0-9]", "");
    }
}
