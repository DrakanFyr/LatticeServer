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
    public List<ScenarioEntityRef> Entities { get; set; } = [];
}

/// <summary>
/// References a template by ID with optional spawn-time overrides.
/// Replaces the old <c>EntitySpawnConfig</c> which contained inline entity definitions.
/// </summary>
public class ScenarioEntityRef
{
    /// <summary>The template folder name (template ID) to spawn.</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>Optional display name override (aliases.name).</summary>
    public string? NameOverride { get; set; }

    public double? LatitudeDegrees { get; set; }
    public double? LongitudeDegrees { get; set; }
    public double? AltitudeHaeMeters { get; set; }
}
