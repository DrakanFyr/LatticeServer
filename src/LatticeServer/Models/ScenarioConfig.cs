namespace LatticeServer.Models;

public class ScenarioConfig
{
    public string ActiveScenario { get; set; } = "default";
    public Dictionary<string, Scenario> Scenarios { get; set; } = [];
}

public class Scenario
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<EntitySpawnConfig> Entities { get; set; } = [];
}

public class EntitySpawnConfig
{
    /// <summary>Stable GUID for this entity. Must not change between restarts.</summary>
    public string EntityId { get; set; } = "";

    /// <summary>Ontology template: Track, Asset, SensorPointOfInterest, Geo, SignalOfInterest.</summary>
    public string Template { get; set; } = "Track";

    /// <summary>Display name (aliases.name).</summary>
    public string Name { get; set; } = "";

    /// <summary>Provenance integration name.</summary>
    public string IntegrationName { get; set; } = "lattice-server";

    /// <summary>Provenance data type.</summary>
    public string DataType { get; set; } = "";

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Optional altitude in meters above ellipsoid (HAE).</summary>
    public double? AltitudeHaeMeters { get; set; }

    /// <summary>Optional speed in meters per second.</summary>
    public double? SpeedMps { get; set; }

    /// <summary>Optional ENU velocity components (meters per second).</summary>
    public double? VelocityE { get; set; }
    public double? VelocityN { get; set; }
    public double? VelocityU { get; set; }

    /// <summary>Mil-view disposition: Unknown, Friendly, Hostile, Suspicious, AssumedFriendly, Neutral, Pending.</summary>
    public string Disposition { get; set; } = "Unknown";

    /// <summary>Mil-view environment: Unknown, Air, Surface, SubSurface, Land, Space.</summary>
    public string Environment { get; set; } = "Surface";

    /// <summary>Optional ontology platform type (e.g. "USV").</summary>
    public string? PlatformType { get; set; }

    /// <summary>Task specification URLs for the entity's task catalog (assets only).</summary>
    public List<string> TaskSpecificationUrls { get; set; } = [];

    /// <summary>How many seconds ahead to set expiry_time on each publish.</summary>
    public int ExpirySeconds { get; set; } = 15;

    /// <summary>How often (in seconds) to re-publish the entity to keep it fresh.</summary>
    public int RefreshIntervalSeconds { get; set; } = 5;
}
