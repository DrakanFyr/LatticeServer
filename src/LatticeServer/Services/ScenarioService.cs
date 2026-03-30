using System.Text.Json;
using Anduril.Entitymanager.V1;
using Anduril.Tasks.V2;
using Google.Protobuf.WellKnownTypes;
using LatticeServer.Models;

namespace LatticeServer.Services;

/// <summary>
/// Background service that reads a scenario config file and auto-spawns the specified entities,
/// keeping their expiry_time and provenance.source_update_time fresh on a configurable interval.
/// If an entity is deleted externally it will no longer be refreshed.
/// </summary>
public class ScenarioService : BackgroundService
{
    private readonly ILogger<ScenarioService> _logger;
    private readonly EntityStore _entityStore;
    private readonly IConfiguration _configuration;

    public ScenarioService(
        ILogger<ScenarioService> logger,
        EntityStore entityStore,
        IConfiguration configuration)
    {
        _logger = logger;
        _entityStore = entityStore;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configPath = _configuration["ScenarioConfigPath"];
        if (string.IsNullOrEmpty(configPath))
        {
            _logger.LogInformation("ScenarioConfigPath not configured; scenario auto-spawn disabled.");
            return;
        }

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("Scenario config file not found at '{Path}'; auto-spawn disabled.", configPath);
            return;
        }

        ScenarioConfig config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath, stoppingToken);
            config = JsonSerializer.Deserialize<ScenarioConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new ScenarioConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse scenario config at '{Path}'.", configPath);
            return;
        }

        if (!config.Scenarios.TryGetValue(config.ActiveScenario, out var scenario))
        {
            _logger.LogWarning("Active scenario '{Key}' not found in config.", config.ActiveScenario);
            return;
        }

        _logger.LogInformation(
            "Scenario '{Name}': spawning {Count} entities.",
            scenario.Name, scenario.Entities.Count);

        // Per-entity state: has it been published, and has it been deleted externally?
        var published = new HashSet<string>();
        var deleted = new HashSet<string>();
        var nextRefresh = new Dictionary<string, DateTime>();

        foreach (var entity in scenario.Entities)
        {
            if (string.IsNullOrEmpty(entity.EntityId))
            {
                _logger.LogWarning("Skipping scenario entity with no entityId.");
                continue;
            }
            nextRefresh[entity.EntityId] = DateTime.MinValue; // publish immediately
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            foreach (var cfg in scenario.Entities)
            {
                if (string.IsNullOrEmpty(cfg.EntityId)) continue;
                if (deleted.Contains(cfg.EntityId)) continue;

                if (!nextRefresh.TryGetValue(cfg.EntityId, out var due) || now < due) continue;

                // If published before but missing from the store → deleted externally
                if (published.Contains(cfg.EntityId) && _entityStore.GetEntity(cfg.EntityId) == null)
                {
                    _logger.LogInformation(
                        "Scenario entity '{Id}' ({Name}) was removed externally; stopping refresh.",
                        cfg.EntityId, cfg.Name);
                    deleted.Add(cfg.EntityId);
                    continue;
                }

                try
                {
                    _entityStore.PublishEntity(BuildEntity(cfg));
                    if (published.Add(cfg.EntityId))
                        _logger.LogInformation("Spawned scenario entity '{Id}' ({Name}).", cfg.EntityId, cfg.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish scenario entity '{Id}'.", cfg.EntityId);
                }

                nextRefresh[cfg.EntityId] = now.AddSeconds(cfg.RefreshIntervalSeconds);
            }

            await Task.Delay(500, stoppingToken);
        }
    }

    private static Entity BuildEntity(EntitySpawnConfig cfg)
    {
        var now = DateTime.UtcNow;

        var entity = new Entity
        {
            EntityId = cfg.EntityId,
            IsLive = true,
            ExpiryTime = Timestamp.FromDateTime(now.AddSeconds(cfg.ExpirySeconds)),
            Aliases = new Aliases { Name = cfg.Name },
            Location = BuildLocation(cfg),
            MilView = new MilView
            {
                Disposition = ParseDisposition(cfg.Disposition),
                Environment = ParseEnvironment(cfg.Environment),
            },
            Provenance = new Provenance
            {
                IntegrationName = cfg.IntegrationName,
                DataType = cfg.DataType,
                SourceUpdateTime = Timestamp.FromDateTime(now),
            },
            Ontology = new Ontology
            {
                Template = ParseTemplate(cfg.Template),
                PlatformType = cfg.PlatformType ?? "",
            },
        };

        if (cfg.TaskSpecificationUrls.Count > 0)
        {
            entity.TaskCatalog = new TaskCatalog();
            foreach (var url in cfg.TaskSpecificationUrls)
                entity.TaskCatalog.TaskDefinitions.Add(new TaskDefinition { TaskSpecificationUrl = url });
        }

        return entity;
    }

    private static Location BuildLocation(EntitySpawnConfig cfg)
    {
        var position = new Position
        {
            LatitudeDegrees = cfg.Latitude,
            LongitudeDegrees = cfg.Longitude,
        };

        if (cfg.AltitudeHaeMeters.HasValue)
            position.AltitudeHaeMeters = cfg.AltitudeHaeMeters.Value;

        var location = new Location { Position = position };

        if (cfg.SpeedMps.HasValue)
            location.SpeedMps = cfg.SpeedMps.Value;

        if (cfg.VelocityE.HasValue || cfg.VelocityN.HasValue || cfg.VelocityU.HasValue)
        {
            location.VelocityEnu = new Anduril.Type.ENU
            {
                E = cfg.VelocityE ?? 0,
                N = cfg.VelocityN ?? 0,
                U = cfg.VelocityU ?? 0,
            };
        }

        return location;
    }

    private static Anduril.Ontology.V1.Disposition ParseDisposition(string value) =>
        value.ToLowerInvariant() switch
        {
            "friendly" => Anduril.Ontology.V1.Disposition.Friendly,
            "hostile" => Anduril.Ontology.V1.Disposition.Hostile,
            "suspicious" => Anduril.Ontology.V1.Disposition.Suspicious,
            "assumedfriendly" or "assumed_friendly" => Anduril.Ontology.V1.Disposition.AssumedFriendly,
            "neutral" => Anduril.Ontology.V1.Disposition.Neutral,
            "pending" => Anduril.Ontology.V1.Disposition.Pending,
            _ => Anduril.Ontology.V1.Disposition.Unknown,
        };

    private static Anduril.Ontology.V1.Environment ParseEnvironment(string value) =>
        value.ToLowerInvariant() switch
        {
            "air" => Anduril.Ontology.V1.Environment.Air,
            "surface" => Anduril.Ontology.V1.Environment.Surface,
            "subsurface" or "sub_surface" => Anduril.Ontology.V1.Environment.SubSurface,
            "land" => Anduril.Ontology.V1.Environment.Land,
            "space" => Anduril.Ontology.V1.Environment.Space,
            _ => Anduril.Ontology.V1.Environment.Unknown,
        };

    private static Template ParseTemplate(string value) =>
        value.ToLowerInvariant() switch
        {
            "track" => Template.Track,
            "asset" => Template.Asset,
            "sensorpointofinterest" or "sensor_point_of_interest" => Template.SensorPointOfInterest,
            "geo" => Template.Geo,
            "signalofinterest" or "signal_of_interest" => Template.SignalOfInterest,
            _ => Template.Invalid,
        };
}
