using System.Text.Json.Serialization;

namespace ILMFoodNBrew.Shared;

public class FoodTruck
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? FacebookUrl { get; set; }
    public List<TruckAppearance> Appearances { get; set; } = [];
}

public class TruckAppearance
{
    public string TruckName { get; set; } = "";
    public string Description { get; set; } = "";
    public string? FacebookUrl { get; set; }
    public DateOnly Date { get; set; }
    public string LocationName { get; set; } = "";
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
}

public class LocationInfo
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class ScrapedData
{
    public DateTime ScrapedAt { get; set; }
    public string SourceUrl { get; set; } = "";
    public string DateRange { get; set; } = "";
    public List<FoodTruck> Trucks { get; set; } = [];
    public Dictionary<string, LocationInfo> Locations { get; set; } = [];
    public List<TruckAppearance> AllAppearances { get; set; } = [];
}
