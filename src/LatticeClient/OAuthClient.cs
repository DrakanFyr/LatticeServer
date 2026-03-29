using System.Text;
using System.Text.Json;

namespace LatticeClient;

/// <summary>
/// Response from an OAuth2 token request.
/// </summary>
public record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? Scope);

/// <summary>
/// Client for the Lattice OAuth2 REST API. Used to obtain bearer tokens
/// for authenticating other API calls.
/// This API is REST-only (no gRPC equivalent).
/// </summary>
public class OAuthClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public OAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    public OAuthClient(string baseUrl)
    {
        _httpClient = RestEntityManagerClient.CreateHttpClient(baseUrl, bearerToken: null);
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Get a new OAuth2 bearer token using client credentials.
    /// </summary>
    public async Task<TokenResponse> GetTokenAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(new
        {
            grant_type = "client_credentials",
            client_id = clientId,
            client_secret = clientSecret,
        });

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/v1/oauth/token", content, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorDoc = JsonDocument.Parse(responseJson);
            var error = errorDoc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            var errorDesc = errorDoc.RootElement.TryGetProperty("error_description", out var ed) ? ed.GetString() : null;
            throw new HttpRequestException(
                $"OAuth error ({(int)response.StatusCode}): {error}" +
                (errorDesc != null ? $" - {errorDesc}" : ""));
        }

        return ParseTokenResponse(responseJson);
    }

    /// <summary>
    /// Get a new OAuth2 bearer token using form-encoded client credentials.
    /// </summary>
    public async Task<TokenResponse> GetTokenFormEncodedAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        var formData = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
        ]);

        using var response = await _httpClient.PostAsync("api/v1/oauth/token", formData, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorDoc = JsonDocument.Parse(responseJson);
            var error = errorDoc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            var errorDesc = errorDoc.RootElement.TryGetProperty("error_description", out var ed) ? ed.GetString() : null;
            throw new HttpRequestException(
                $"OAuth error ({(int)response.StatusCode}): {error}" +
                (errorDesc != null ? $" - {errorDesc}" : ""));
        }

        return ParseTokenResponse(responseJson);
    }

    private static TokenResponse ParseTokenResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString() ?? "";
        var tokenType = root.GetProperty("token_type").GetString() ?? "Bearer";
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 0;
        var scope = root.TryGetProperty("scope", out var s) ? s.GetString() : null;

        return new TokenResponse(accessToken, tokenType, expiresIn, scope);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
