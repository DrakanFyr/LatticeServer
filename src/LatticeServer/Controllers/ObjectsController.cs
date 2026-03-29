using LatticeServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace LatticeServer.Controllers;

[ApiController]
public class ObjectsController : ControllerBase
{
    private const long MaxObjectSize = 1L * 1024 * 1024 * 1024; // 1 GiB

    private readonly ILogger<ObjectsController> _logger;
    private readonly ObjectStore _store;

    public ObjectsController(ILogger<ObjectsController> logger, ObjectStore store)
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>
    /// GET /api/v1/objects - List objects
    /// </summary>
    [HttpGet("api/v1/objects")]
    public IActionResult ListObjects(
        [FromQuery] string? prefix,
        [FromQuery] string? sinceTimestamp,
        [FromQuery] string? pageToken,
        [FromQuery] bool? allObjectsInMesh,
        [FromQuery] int? maxPageSize)
    {
        DateTime? since = null;
        if (!string.IsNullOrEmpty(sinceTimestamp) && DateTime.TryParse(sinceTimestamp, out var parsedSince))
        {
            since = parsedSince.ToUniversalTime();
        }

        var pageSize = maxPageSize ?? 1000;
        var objects = _store.List(prefix, since, pageSize);

        var items = objects.Select(o => new
        {
            content_identifier = new
            {
                path = o.Path,
                checksum = o.Checksum,
            },
            size_bytes = o.Data.Length,
            last_updated_at = o.LastUpdatedAt.ToString("O"),
            expiry_time = o.ExpiryTime.ToString("O"),
        }).ToList();

        _logger.LogInformation("REST ListObjects: returned {Count} objects", items.Count);
        return Ok(new { path_metadatas = items });
    }

    /// <summary>
    /// GET /api/v1/objects/{**objectPath} - Get object
    /// </summary>
    [HttpGet("api/v1/objects/{**objectPath}")]
    public IActionResult GetObject(string objectPath)
    {
        if (string.IsNullOrEmpty(objectPath))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "object_path is required" });
        }

        var obj = _store.Get(objectPath);
        if (obj == null)
        {
            return NotFound(new { code = "NOT_FOUND", message = $"Object {objectPath} not found" });
        }

        _logger.LogInformation("REST GetObject: {ObjectPath}", objectPath);

        Response.Headers["X-Object-Checksum"] = obj.Checksum;
        Response.Headers["X-Object-Size"] = obj.Data.Length.ToString();
        Response.Headers["X-Object-Last-Updated"] = obj.LastUpdatedAt.ToString("O");
        Response.Headers["X-Object-Expiry"] = obj.ExpiryTime.ToString("O");

        return File(obj.Data, "application/octet-stream");
    }

    /// <summary>
    /// POST /api/v1/objects/{**objectPath} - Upload object
    /// </summary>
    [HttpPost("api/v1/objects/{**objectPath}")]
    public async Task<IActionResult> UploadObject(string objectPath)
    {
        if (string.IsNullOrEmpty(objectPath))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "object_path is required" });
        }

        // Check content length if provided
        if (Request.ContentLength > MaxObjectSize)
        {
            return StatusCode(413, new { code = "CONTENT_TOO_LARGE", message = "Object must be 1 GiB or smaller" });
        }

        // Read body
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        var data = ms.ToArray();

        if (data.Length > MaxObjectSize)
        {
            return StatusCode(413, new { code = "CONTENT_TOO_LARGE", message = "Object must be 1 GiB or smaller" });
        }

        // Parse optional TTL header
        long? ttlNanoseconds = null;
        if (Request.Headers.TryGetValue("Time-To-Live", out var ttlHeader) &&
            long.TryParse(ttlHeader.FirstOrDefault(), out var parsedTtl))
        {
            ttlNanoseconds = parsedTtl;
        }

        var obj = _store.Upload(objectPath, data, ttlNanoseconds);
        _logger.LogInformation("REST UploadObject: {ObjectPath} ({Size} bytes)", objectPath, data.Length);

        return Ok(new
        {
            content_identifier = new
            {
                path = obj.Path,
                checksum = obj.Checksum,
            },
            size_bytes = obj.Data.Length,
            last_updated_at = obj.LastUpdatedAt.ToString("O"),
            expiry_time = obj.ExpiryTime.ToString("O"),
        });
    }

    /// <summary>
    /// DELETE /api/v1/objects/{**objectPath} - Delete object
    /// </summary>
    [HttpDelete("api/v1/objects/{**objectPath}")]
    public IActionResult DeleteObject(string objectPath)
    {
        if (string.IsNullOrEmpty(objectPath))
        {
            return BadRequest(new { code = "INVALID_ARGUMENT", message = "object_path is required" });
        }

        _store.Delete(objectPath);

        _logger.LogInformation("REST DeleteObject: {ObjectPath}", objectPath);
        return NoContent();
    }

    /// <summary>
    /// HEAD /api/v1/objects/{**objectPath} - Get object metadata
    /// </summary>
    [HttpHead("api/v1/objects/{**objectPath}")]
    public IActionResult GetObjectMetadata(string objectPath)
    {
        if (string.IsNullOrEmpty(objectPath))
        {
            return BadRequest();
        }

        var obj = _store.Get(objectPath);
        if (obj == null)
        {
            return NotFound();
        }

        _logger.LogInformation("REST GetObjectMetadata: {ObjectPath}", objectPath);

        Response.Headers["X-Object-Checksum"] = obj.Checksum;
        Response.Headers["X-Object-Size"] = obj.Data.Length.ToString();
        Response.Headers["X-Object-Last-Updated"] = obj.LastUpdatedAt.ToString("O");
        Response.Headers["X-Object-Expiry"] = obj.ExpiryTime.ToString("O");
        Response.ContentLength = obj.Data.Length;

        return Ok();
    }
}
