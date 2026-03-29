using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LatticeServer.Tests;

/// <summary>
/// Integration tests for POST /api/v1/oauth/token (Get OAuth2 token).
/// Doc: Docs/rest/oauth/get-token.md
/// </summary>
public class OAuthControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OAuthControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static FormUrlEncodedContent FormContent(Dictionary<string, string> pairs) =>
        new(pairs);

    // ───────────────────────────────────────────────
    // POST /api/v1/oauth/token - Happy path (JSON)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetToken_JsonBody_ValidClientCredentials_Returns200()
    {
        // Doc: "Gets a new short-lived token using the specified client credentials"
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = "my-client",
            client_secret = "my-secret",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_JsonBody_ResponseContainsRequiredFields()
    {
        // Doc: access_token (required), token_type (required)
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = "my-client",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("access_token", out var accessToken));
        Assert.False(string.IsNullOrEmpty(accessToken.GetString()));

        Assert.True(root.TryGetProperty("token_type", out var tokenType));
        Assert.Equal("Bearer", tokenType.GetString());
    }

    [Fact]
    public async Task GetToken_JsonBody_ResponseContainsOptionalFields()
    {
        // Doc: expires_in, refresh_expires_in, not-before-policy, scope
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { grant_type = "client_credentials" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("expires_in", out var expiresIn));
        Assert.True(expiresIn.GetInt32() > 0);

        Assert.True(root.TryGetProperty("refresh_expires_in", out _));

        Assert.True(root.TryGetProperty("not-before-policy", out _),
            "Response must include 'not-before-policy' field per OAuth spec");

        Assert.True(root.TryGetProperty("scope", out _));
    }

    [Fact]
    public async Task GetToken_JsonBody_AccessTokenIsUnique()
    {
        // Each call should generate a new random token
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { grant_type = "client_credentials" });

        var response1 = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body1 = await response1.Content.ReadAsStringAsync();
        var token1 = JsonDocument.Parse(body1).RootElement.GetProperty("access_token").GetString();

        var response2 = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body2 = await response2.Content.ReadAsStringAsync();
        var token2 = JsonDocument.Parse(body2).RootElement.GetProperty("access_token").GetString();

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public async Task GetToken_JsonBody_ResponseContentTypeIsJson()
    {
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { grant_type = "client_credentials" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetToken_JsonBody_WithoutClientId_Returns200()
    {
        // Doc: client_id is optional
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { grant_type = "client_credentials" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/oauth/token - Happy path (Form-encoded)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetToken_FormEncoded_ValidClientCredentials_Returns200()
    {
        // Doc: Content-Type: application/x-www-form-urlencoded
        var client = CreateClient();
        var content = FormContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "my-client",
            ["client_secret"] = "my-secret",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_FormEncoded_ResponseContainsRequiredFields()
    {
        var client = CreateClient();
        var content = FormContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "test-client",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", content);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("access_token", out var accessToken));
        Assert.False(string.IsNullOrEmpty(accessToken.GetString()));

        Assert.True(root.TryGetProperty("token_type", out var tokenType));
        Assert.Equal("Bearer", tokenType.GetString());
    }

    [Fact]
    public async Task GetToken_FormEncoded_WithoutOptionalFields_Returns200()
    {
        // Doc: client_id and client_secret are optional
        var client = CreateClient();
        var content = FormContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/oauth/token - Error: missing grant_type (400)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetToken_JsonBody_MissingGrantType_Returns400()
    {
        // Doc: grant_type is required
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { client_id = "my-client" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_FormEncoded_MissingGrantType_Returns400()
    {
        var client = CreateClient();
        var content = FormContent(new Dictionary<string, string>
        {
            ["client_id"] = "my-client",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_EmptyJsonBody_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/oauth/token - Error: unsupported grant_type (400)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetToken_UnsupportedGrantType_Returns400()
    {
        // Doc: grant_type enum only includes "client_credentials"
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { grant_type = "authorization_code" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_UnsupportedGrantType_ErrorResponseMatchesSchema()
    {
        // Doc: GetTokenRequestBadRequestError has required 'error' and optional 'error_description'
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { grant_type = "password" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrEmpty(error.GetString()));

        // error_description is optional per schema but should be present
        Assert.True(root.TryGetProperty("error_description", out _));
    }

    [Fact]
    public async Task GetToken_MissingGrantType_ErrorResponseMatchesSchema()
    {
        // Doc: GetTokenRequestBadRequestError schema
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new { client_id = "test" });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrEmpty(error.GetString()));
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/oauth/token - Error: unauthorized (401)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetToken_InvalidClientSecret_Returns401()
    {
        // Doc: 401 Unauthorized - client authentication failed
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = "my-client",
            client_secret = "invalid",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_InvalidClientSecret_ErrorResponseMatchesSchema()
    {
        // Doc: GetTokenRequestUnauthorizedError has required 'error' and optional 'error_description'
        var client = CreateClient();
        var json = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = "my-client",
            client_secret = "invalid",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent(json));
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrEmpty(error.GetString()));

        // error_description is optional per schema but should be present
        Assert.True(root.TryGetProperty("error_description", out _));
    }

    [Fact]
    public async Task GetToken_FormEncoded_InvalidClientSecret_Returns401()
    {
        // Doc: 401 via form-encoded request
        var client = CreateClient();
        var content = FormContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "my-client",
            ["client_secret"] = "invalid",
        });

        var response = await client.PostAsync("/api/v1/oauth/token", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ───────────────────────────────────────────────
    // POST /api/v1/oauth/token - Error: invalid JSON (400)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetToken_InvalidJson_Returns400()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent("{bad json}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetToken_InvalidJson_ErrorResponseMatchesSchema()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/api/v1/oauth/token", JsonContent("{bad json}"));
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("error", out _));
        Assert.True(root.TryGetProperty("error_description", out _));
    }
}
