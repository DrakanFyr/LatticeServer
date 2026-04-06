using System.IO.Compression;
using System.Text.Json;
using Google.Protobuf.Reflection;
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
    private readonly FileSystemTemplateSource? _fsSource;

    public TemplatesController(
        ILogger<TemplatesController> logger,
        TemplateRegistry registry,
        SpawnedEntityManager manager,
        ITemplateSource templateSource)
    {
        _logger = logger;
        _registry = registry;
        _manager = manager;
        _fsSource = templateSource as FileSystemTemplateSource;
    }

    /// <summary>GET /api/v1/templates — List all loaded templates.</summary>
    [HttpGet("api/v1/templates")]
    public IActionResult ListTemplates()
    {
        var items = _registry.GetAll()
            .Select(t => new TemplateListItem(
                t.TemplateId,
                t.BehaviorType != null,
                t.Config.TickIntervalMs,
                t.Config.Category,
                t.Config.DisplayName,
                t.CustomTaskDescriptors.Count > 0))
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

    /// <summary>
    /// GET /api/v1/templates/{templateId}/task-configurations
    /// Returns the merged task configuration schemas for this template:
    /// auto-derived from registered proto descriptors, with overrides from
    /// task-configurations.json applied on top.
    /// </summary>
    [HttpGet("api/v1/templates/{templateId}/task-configurations")]
    public IActionResult GetTaskConfigurations(string templateId)
    {
        if (!_registry.TryGet(templateId, out var template))
            return NotFound(new { code = "NOT_FOUND", message = $"Template '{templateId}' not found." });

        if (template.CustomTaskDescriptors.Count == 0)
            return Ok(Array.Empty<object>());

        var schemas = template.CustomTaskDescriptors
            .Select(d => DeriveSchema(d))
            .ToList();

        ApplyOverrides(schemas, template.RawTaskConfigurationsJson);

        return Ok(schemas);
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

    // -------------------------------------------------------------------------
    // Template package upload / delete / download
    // -------------------------------------------------------------------------

    /// <summary>POST /api/v1/templates — Upload a template ZIP package.</summary>
    [HttpPost("api/v1/templates")]
    public async Task<IActionResult> UploadTemplate(IFormFile? package)
    {
        if (_fsSource == null)
            return StatusCode(501, new { code = "NOT_SUPPORTED", message = "Template upload is not supported on this host." });

        if (package == null || package.Length == 0)
            return BadRequest(new { code = "BAD_REQUEST", message = "A file named 'package' must be provided." });

        // Determine template ID
        string templateId;
        if (Request.Headers.TryGetValue("X-Template-Id", out var idHeader) && !string.IsNullOrWhiteSpace(idHeader))
        {
            templateId = idHeader.ToString().Trim();
        }
        else
        {
            var filename = Path.GetFileName(package.FileName ?? "");
            templateId = filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(filename)
                : filename;
        }

        // Validate template ID
        if (string.IsNullOrWhiteSpace(templateId) ||
            templateId.Contains('/') || templateId.Contains('\\') ||
            templateId.Contains("..") ||
            templateId.Length > 64)
        {
            return BadRequest(new { code = "BAD_REQUEST", message = "Template ID is invalid. Must be non-empty, no path separators, and ≤ 64 characters." });
        }

        // 409 if a directory-based template with the same ID already exists
        if (_fsSource.IsDirectoryTemplate(templateId))
            return Conflict(new { code = "CONFLICT", message = $"A directory-based template '{templateId}' already exists and cannot be overwritten via upload." });

        // Buffer into MemoryStream so we can both validate (requires seeking) and write
        using var buffer = new MemoryStream((int)package.Length);
        await package.CopyToAsync(buffer);
        buffer.Position = 0;

        // Validate ZIP contents
        try
        {
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.GetEntry("entity.json") == null)
                return BadRequest(new { code = "BAD_REQUEST", message = "The ZIP package must contain entity.json at the root level." });
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            return BadRequest(new { code = "BAD_REQUEST", message = "The uploaded file is not a valid ZIP archive." });
        }

        // Write atomically: write to .tmp, then move
        var watchDir = _fsSource.WatchDirectory;
        var tmpPath = Path.Combine(watchDir, $"{templateId}.zip.tmp");
        var finalPath = Path.Combine(watchDir, $"{templateId}.zip");

        try
        {
            buffer.Position = 0;
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            {
                await buffer.CopyToAsync(fs);
            }
            System.IO.File.Move(tmpPath, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write uploaded template '{TemplateId}' to disk.", templateId);
            try { System.IO.File.Delete(tmpPath); } catch { }
            return StatusCode(500, new { code = "INTERNAL", message = "Failed to save the uploaded package." });
        }

        _logger.LogInformation("REST: uploaded template package '{TemplateId}'.", templateId);
        return Accepted(new { templateId });
    }

    /// <summary>DELETE /api/v1/templates/{templateId} — Remove an uploaded ZIP template.</summary>
    [HttpDelete("api/v1/templates/{templateId}")]
    public IActionResult DeleteTemplate(string templateId)
    {
        if (_fsSource == null)
            return StatusCode(501, new { code = "NOT_SUPPORTED", message = "Template deletion is not supported on this host." });

        if (_fsSource.IsDirectoryTemplate(templateId))
            return Conflict(new { code = "CONFLICT", message = $"Template '{templateId}' is directory-based and cannot be deleted via API." });

        var zipPath = Path.Combine(_fsSource.WatchDirectory, $"{templateId}.zip");
        if (!System.IO.File.Exists(zipPath))
            return NotFound(new { code = "NOT_FOUND", message = $"No uploaded ZIP template '{templateId}' found." });

        try
        {
            System.IO.File.Delete(zipPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template '{TemplateId}'.", templateId);
            return StatusCode(500, new { code = "INTERNAL", message = "Failed to delete the package." });
        }

        _logger.LogInformation("REST: deleted template package '{TemplateId}'.", templateId);
        return NoContent();
    }

    /// <summary>GET /api/v1/templates/{templateId}/download — Download a packaged ZIP template.</summary>
    [HttpGet("api/v1/templates/{templateId}/download")]
    public IActionResult DownloadTemplate(string templateId)
    {
        if (_fsSource == null)
            return StatusCode(501, new { code = "NOT_SUPPORTED", message = "Template download is not supported on this host." });

        if (_fsSource.IsDirectoryTemplate(templateId))
            return Conflict(new { code = "CONFLICT", message = $"Template '{templateId}' is directory-based and cannot be downloaded as a ZIP." });

        var zipPath = Path.Combine(_fsSource.WatchDirectory, $"{templateId}.zip");
        if (!System.IO.File.Exists(zipPath))
            return NotFound(new { code = "NOT_FOUND", message = $"No uploaded ZIP template '{templateId}' found." });

        return PhysicalFile(zipPath, "application/zip", $"{templateId}.zip");
    }

    // -------------------------------------------------------------------------
    // Schema derivation helpers
    // -------------------------------------------------------------------------

    private static string SnakeCaseToTitleCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return string.Join(' ', name.Split('_')
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static string? MapFieldType(FieldType ft) => ft switch
    {
        FieldType.Float or FieldType.Double => "float_opt",
        FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 or FieldType.Fixed32 => "int32_opt",
        FieldType.UInt32 => "uint32_opt",
        FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 or FieldType.Fixed64 => "int64_opt",
        FieldType.UInt64 => "uint64_opt",
        FieldType.String => "string",
        _ => null,
    };

    private static Dictionary<string, object?>? DeriveField(FieldDescriptor f)
    {
        var uiType = MapFieldType(f.FieldType);
        if (uiType == null) return null;
        return new Dictionary<string, object?>
        {
            ["key"] = f.Name,
            ["label"] = SnakeCaseToTitleCase(f.Name),
            ["type"] = uiType,
            ["required"] = false,
        };
    }

    private static Dictionary<string, object?> DeriveSchema(MessageDescriptor d)
    {
        var fields = d.Fields.InFieldNumberOrder()
            .Select(f => DeriveField(f))
            .Where(f => f != null)
            .Cast<Dictionary<string, object?>>()
            .ToList();

        return new Dictionary<string, object?>
        {
            ["typeUrl"] = $"type.googleapis.com/{d.FullName}",
            ["displayName"] = SnakeCaseToTitleCase(d.Name),
            ["description"] = null,
            ["executionTag"] = null,
            ["fields"] = fields,
            ["_descriptor"] = d,
        };
    }

    private static void ApplyOverrides(List<Dictionary<string, object?>> schemas, string? rawJson)
    {
        if (rawJson == null) return;

        JsonElement overridesArray;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            overridesArray = doc.RootElement.Clone();
        }
        catch
        {
            return;
        }

        if (overridesArray.ValueKind != JsonValueKind.Array) return;

        foreach (var overrideEntry in overridesArray.EnumerateArray())
        {
            if (!overrideEntry.TryGetProperty("typeUrl", out var typeUrlEl)) continue;
            var typeUrl = typeUrlEl.GetString();
            if (typeUrl == null) continue;

            var schema = schemas.FirstOrDefault(s => (string?)s["typeUrl"] == typeUrl);
            if (schema == null) continue;

            // Apply top-level overrides
            if (overrideEntry.TryGetProperty("displayName", out var dn))
                schema["displayName"] = dn.GetString();
            if (overrideEntry.TryGetProperty("description", out var desc))
                schema["description"] = desc.GetString();
            if (overrideEntry.TryGetProperty("executionTag", out var et))
                schema["executionTag"] = et.GetString();

            // Apply field overrides
            if (overrideEntry.TryGetProperty("fieldOverrides", out var fieldOverrides)
                && fieldOverrides.ValueKind == JsonValueKind.Object)
            {
                var fields = (List<Dictionary<string, object?>>)schema["fields"]!;
                var descriptor = schema["_descriptor"] as MessageDescriptor;

                foreach (var prop in fieldOverrides.EnumerateObject())
                {
                    var fieldKey = prop.Name;
                    var existingField = fields.FirstOrDefault(f => (string?)f["key"] == fieldKey);

                    if (existingField != null)
                    {
                        // Update existing field
                        ApplyFieldOverrides(existingField, prop.Value);
                    }
                    else if (descriptor != null)
                    {
                        // Field was skipped by auto-derivation — add it at the correct position
                        var newField = new Dictionary<string, object?>
                        {
                            ["key"] = fieldKey,
                            ["label"] = SnakeCaseToTitleCase(fieldKey),
                            ["required"] = false,
                        };
                        ApplyFieldOverrides(newField, prop.Value);

                        // Determine insertion position by proto field number order
                        var protoField = descriptor.FindFieldByName(fieldKey);
                        if (protoField != null)
                        {
                            var allProtoFields = descriptor.Fields.InFieldNumberOrder().ToList();
                            var targetIndex = allProtoFields.IndexOf(protoField);
                            // Find the right position among existing derived fields
                            int insertAt = fields.Count;
                            for (int i = 0; i < fields.Count; i++)
                            {
                                var existingProtoField = descriptor.FindFieldByName((string?)fields[i]["key"] ?? "");
                                if (existingProtoField != null)
                                {
                                    var existingIndex = allProtoFields.IndexOf(existingProtoField);
                                    if (existingIndex > targetIndex)
                                    {
                                        insertAt = i;
                                        break;
                                    }
                                }
                            }
                            fields.Insert(insertAt, newField);
                        }
                        else
                        {
                            fields.Add(newField);
                        }
                    }
                }
            }
        }

        // Remove hidden fields and strip internal _descriptor key
        foreach (var schema in schemas)
        {
            var fields = (List<Dictionary<string, object?>>)schema["fields"]!;
            fields.RemoveAll(f => f.TryGetValue("hidden", out var h) && h is true);
            foreach (var f in fields) f.Remove("hidden");
            schema.Remove("_descriptor");
        }
    }

    private static void ApplyFieldOverrides(Dictionary<string, object?> field, JsonElement overrides)
    {
        if (overrides.TryGetProperty("label", out var label))
            field["label"] = label.GetString();
        if (overrides.TryGetProperty("type", out var type))
            field["type"] = type.GetString();
        if (overrides.TryGetProperty("required", out var required))
            field["required"] = required.GetBoolean();
        if (overrides.TryGetProperty("group", out var group))
            field["group"] = group.GetString();
        if (overrides.TryGetProperty("hidden", out var hidden))
            field["hidden"] = hidden.GetBoolean();
        if (overrides.TryGetProperty("latKey", out var latKey))
            field["latKey"] = latKey.GetString();
        if (overrides.TryGetProperty("lonKey", out var lonKey))
            field["lonKey"] = lonKey.GetString();
        if (overrides.TryGetProperty("altKey", out var altKey))
            field["altKey"] = altKey.GetString();
    }
}
