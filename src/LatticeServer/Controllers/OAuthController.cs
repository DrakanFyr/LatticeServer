using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace LatticeServer.Controllers;

[ApiController]
public class OAuthController : ControllerBase
{
    private readonly ILogger<OAuthController> _logger;

    public OAuthController(ILogger<OAuthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// POST /api/v1/oauth/token - Get OAuth2 token
    /// Gets a new short-lived token using the specified client credentials.
    /// This is a mock implementation that always returns a valid token.
    /// </summary>
    [HttpPost("api/v1/oauth/token")]
    public async Task<IActionResult> GetToken()
    {
        string? grantType = null;
        string? clientId = null;
        string? clientSecret = null;

        // Support both JSON and form-encoded bodies
        if (Request.ContentType?.Contains("application/json") == true)
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("grant_type", out var gt))
                    grantType = gt.GetString();
                if (root.TryGetProperty("client_id", out var ci))
                    clientId = ci.GetString();
                if (root.TryGetProperty("client_secret", out var cs))
                    clientSecret = cs.GetString();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "invalid_request", error_description = $"Invalid JSON: {ex.Message}" });
            }
        }
        else
        {
            // Form-encoded
            var form = await Request.ReadFormAsync();
            grantType = form["grant_type"].FirstOrDefault();
            clientId = form["client_id"].FirstOrDefault();
            clientSecret = form["client_secret"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(grantType))
        {
            return BadRequest(new { error = "invalid_request", error_description = "grant_type is required" });
        }

        if (grantType != "client_credentials")
        {
            return BadRequest(new { error = "unsupported_grant_type", error_description = $"Unsupported grant type: {grantType}" });
        }

        // Simulate client authentication failure when client_secret is "invalid"
        if (clientSecret == "invalid")
        {
            return Unauthorized(new { error = "unauthorized_client", error_description = "Client authentication failed" });
        }

        _logger.LogInformation("REST GetToken: client_id={ClientId}", clientId ?? "(none)");

        // Generate a mock bearer token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var accessToken = Convert.ToBase64String(tokenBytes);

        var responseBody = new Dictionary<string, object>
        {
            ["access_token"] = accessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["refresh_expires_in"] = 0,
            ["not-before-policy"] = 0,
            ["scope"] = "lattice",
        };
        return Ok(responseBody);
    }
}
