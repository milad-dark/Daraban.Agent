using Daraban.Agent.Core.Config;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Daraban.Agent.Core.Transport;

/// <summary>Obtains and caches a short-lived OAuth2 client-credentials access token.</summary>
internal sealed class OAuthTokenProvider(HttpClient http, AgentOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.OAuthTokenEndpoint))
            return null;

        if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _accessToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _accessToken;

            if (string.IsNullOrWhiteSpace(options.OAuthClientId) || string.IsNullOrWhiteSpace(options.OAuthClientSecret))
                throw new InvalidOperationException("OAuthClientId and OAuthClientSecret are required when OAuthTokenEndpoint is configured.");

            using var request = new HttpRequestMessage(HttpMethod.Post, options.OAuthTokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = options.OAuthClientId,
                    ["client_secret"] = options.OAuthClientSecret,
                    ["scope"] = options.OAuthScope ?? "daraban.agent.inventory",
                })
            };
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("OAuth token endpoint returned an empty response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("OAuth token endpoint did not return access_token.");

            _accessToken = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; } = 300;
    }
}
