using System.Text.Json;
using LatticeServer.Models;

namespace LatticeServer.Services;

/// <summary>
/// Background service that reads a scenario config file and spawns the referenced templates.
/// Entity lifecycle (refresh, expiry) is delegated to <see cref="SpawnedEntityManager"/>.
/// </summary>
public class ScenarioService : BackgroundService
{
    private readonly ILogger<ScenarioService> _logger;
    private readonly SpawnedEntityManager _spawnedEntityManager;
    private readonly TemplateRegistry _templateRegistry;
    private readonly IConfiguration _configuration;

    public ScenarioService(
        ILogger<ScenarioService> logger,
        SpawnedEntityManager spawnedEntityManager,
        TemplateRegistry templateRegistry,
        IConfiguration configuration)
    {
        _logger = logger;
        _spawnedEntityManager = spawnedEntityManager;
        _templateRegistry = templateRegistry;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dataDir = _configuration["DataDirectory"] ?? ".";
        var rawPath = _configuration["ScenarioConfigPath"];
        if (string.IsNullOrEmpty(rawPath))
        {
            _logger.LogInformation("ScenarioConfigPath not configured; scenario auto-spawn disabled.");
            return;
        }

        var configPath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.GetFullPath(Path.Combine(dataDir, rawPath));

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
            "Scenario '{Name}': spawning {Count} template references.",
            scenario.Name, scenario.Entities.Count);

        // Wait for TemplateRegistry to finish its initial directory scan
        await _templateRegistry.InitialScanComplete.WaitAsync(stoppingToken);

        var spawnedEntityIds = new List<string>();

        foreach (var entityRef in scenario.Entities)
        {
            if (string.IsNullOrEmpty(entityRef.TemplateId))
            {
                _logger.LogWarning("Skipping scenario entity ref with no templateId.");
                continue;
            }

            if (!_templateRegistry.TryGet(entityRef.TemplateId, out var template))
            {
                _logger.LogWarning(
                    "Scenario entity ref: template '{TemplateId}' not found in registry. Skipping.",
                    entityRef.TemplateId);
                continue;
            }

            try
            {
                var options = new SpawnOptions
                {
                    NameOverride = entityRef.NameOverride,
                    LatitudeDegrees = entityRef.LatitudeDegrees,
                    LongitudeDegrees = entityRef.LongitudeDegrees,
                    AltitudeHaeMeters = entityRef.AltitudeHaeMeters,
                };

                var entityId = _spawnedEntityManager.Spawn(template, options);
                spawnedEntityIds.Add(entityId);
                _logger.LogInformation(
                    "Scenario spawned entity '{EntityId}' from template '{TemplateId}'.",
                    entityId, entityRef.TemplateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to spawn template '{TemplateId}' for scenario.", entityRef.TemplateId);
            }
        }

        _logger.LogInformation("Scenario '{Name}' active with {Count} entities.", scenario.Name, spawnedEntityIds.Count);

        // Keep alive until shutdown
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Despawn scenario entities on shutdown
        foreach (var entityId in spawnedEntityIds)
        {
            _spawnedEntityManager.Despawn(entityId);
        }
    }
}
