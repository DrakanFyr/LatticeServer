using LatticeServer.Models;
using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace LatticeServer.Controllers;

[ApiController]
public class TemplatesController : ControllerBase
{
    private readonly ILogger<TemplatesController> _logger;
    private readonly TemplateRegistry _registry;
    private readonly SpawnedEntityManager _manager;

    public TemplatesController(
        ILogger<TemplatesController> logger,
        TemplateRegistry registry,
        SpawnedEntityManager manager)
    {
        _logger = logger;
        _registry = registry;
        _manager = manager;
    }

    /// <summary>GET /api/v1/templates — List all loaded templates.</summary>
    [HttpGet("api/v1/templates")]
    public IActionResult ListTemplates()
    {
        var items = _registry.GetAll()
            .Select(t => new TemplateListItem(t.TemplateId, t.BehaviorType != null, t.Config.TickIntervalMs))
            .ToList();
        return Ok(items);
    }

    /// <summary>GET /api/v1/templates/{templateId} — Get raw entity JSON for a template.</summary>
    [HttpGet("api/v1/templates/{templateId}")]
    public IActionResult GetTemplate(string templateId)
    {
        if (!_registry.TryGet(templateId, out var template))
            return NotFound(new { code = "NOT_FOUND", message = $"Template '{templateId}' not found." });

        return Content(template.RawEntityJson, "application/json");
    }

    /// <summary>GET /api/v1/templates/instances — List all live spawned instances.</summary>
    [HttpGet("api/v1/templates/instances")]
    public IActionResult ListInstances()
    {
        var items = _manager.GetAll()
            .Select(i => new SpawnedInstanceInfo(i.EntityId, i.TemplateId))
            .ToList();
        return Ok(items);
    }

    /// <summary>POST /api/v1/templates/{templateId}/spawn — Spawn a new instance.</summary>
    [HttpPost("api/v1/templates/{templateId}/spawn")]
    public IActionResult Spawn(string templateId, [FromBody] SpawnOptions? options)
    {
        if (!_registry.TryGet(templateId, out var template))
            return NotFound(new { code = "NOT_FOUND", message = $"Template '{templateId}' not found." });

        string entityId;
        try
        {
            entityId = _manager.Spawn(template, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn template '{TemplateId}'.", templateId);
            return StatusCode(500, new { code = "INTERNAL", message = ex.Message });
        }

        _logger.LogInformation("REST: spawned entity '{EntityId}' from template '{TemplateId}'.", entityId, templateId);
        return Ok(new { entityId });
    }

    /// <summary>DELETE /api/v1/templates/instances/{entityId} — Despawn one instance.</summary>
    [HttpDelete("api/v1/templates/instances/{entityId}")]
    public IActionResult Despawn(string entityId)
    {
        var ok = _manager.Despawn(entityId);
        if (!ok)
            return NotFound(new { code = "NOT_FOUND", message = $"Spawned instance '{entityId}' not found." });

        _logger.LogInformation("REST: despawned entity '{EntityId}'.", entityId);
        return Ok(new { entityId, despawned = true });
    }

    /// <summary>DELETE /api/v1/templates/instances — Despawn all instances.</summary>
    [HttpDelete("api/v1/templates/instances")]
    public IActionResult DespawnAll()
    {
        var count = _manager.DespawnAll();
        _logger.LogInformation("REST: despawned {Count} entities.", count);
        return Ok(new { despawnedCount = count });
    }
}
