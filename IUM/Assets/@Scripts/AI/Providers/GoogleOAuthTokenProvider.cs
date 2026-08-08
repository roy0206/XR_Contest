using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

/// <summary>Client id and secret from the OAuth client JSON downloaded in the Cloud console.</summary>
[Serializable]
public sealed class GoogleOAuthClientDetail
{
    [JsonProperty("client_id")] public string ClientId { get; set; }
    [JsonProperty("client_secret")] public string ClientSecret { get; set; }
    [JsonProperty("auth_uri")] public string AuthUri { get; set; } = "https://accounts.google.com/o/oauth2/auth";
    [JsonProperty("token_uri")] public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
}

/// <summary>
/// The OAuth client file, used unchanged. Google writes the details under "installed" for
/// desktop clients and under "web" for web clients.
/// </summary>
[Serializable]
public sealed class GoogleOAuthClient
{
    [JsonProperty("installed")] public GoogleOAuthClientDetail Installed { get; set; }
    [JsonProperty("web")] public GoogleOAuthClientDetail Web { get; set; }

    [JsonIgnore] public GoogleOAuthClientDetail Detail => Installed ?? Web;

    [JsonIgnore]
    public bool IsUsable =>
        Detail != null &&
        !string.IsNullOrWhiteSpace(Detail.ClientId) &&
        !string.IsNullOrWhiteSpace(Detail.ClientSecret);
}

/// <summary>
/// Refresh-token flow, used when the organisation blocks service account keys. Consent happens
/// once on a development machine; the resulting refresh token is long-lived, so the build never
/// shows a login screen and the headset never needs a browser.
/// </summary>
public sealed class GoogleOAuthTokenProvider : IGoogleTokenProvider
{
    const int DefaultLifetimeSeconds = 3600;
    const int RefreshMarginSeconds = 300;

    readonly GoogleOAuthClient _client;
    readonly string _refreshToken;
    readonly SemaphoreSlim _lock = new(1, 1);
    readonly float _timeoutSeconds;

    string _token;
    DateTime _expiresAtUtc;

    public GoogleOAuthTokenProvider(GoogleOAuthClient client, string refreshToken, float timeoutSeconds = 15f)
    {
        _client = client;
        _refreshToken = refreshToken;
        _timeoutSeconds = timeoutSeconds;
    }

    public bool IsUsable =>
        _client != null && _client.IsUsable && !string.IsNullOrWhiteSpace(_refreshToken);

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsUsable)
            throw new AiServiceException("The Google OAuth client or refresh token is missing.");

        if (_token != null && DateTime.UtcNow < _expiresAtUtc)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token != null && DateTime.UtcNow < _expiresAtUtc)
                return _token;

            var detail = _client.Detail;
            var form =
                "grant_type=refresh_token" +
                "&client_id=" + UnityWebRequest.EscapeURL(detail.ClientId) +
                "&client_secret=" + UnityWebRequest.EscapeURL(detail.ClientSecret) +
                "&refresh_token=" + UnityWebRequest.EscapeURL(_refreshToken);

            var response = await AiHttp.PostFormAsync(
                detail.TokenUri, form, null, _timeoutSeconds, cancellationToken);

            if (!response.Success)
                throw response.ToException("Google OAuth");

            _token = ParseToken(response.Text, out var lifetime);
            _expiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, lifetime - RefreshMarginSeconds));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Dictionary<string, string>> BuildAuthorizationHeadersAsync(CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }

    static string ParseToken(string json, out int lifetimeSeconds)
    {
        lifetimeSeconds = DefaultLifetimeSeconds;
        if (string.IsNullOrWhiteSpace(json))
            throw new AiServiceException("Google OAuth returned an empty response.");

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            throw new AiServiceException($"Google OAuth returned unreadable JSON: {exception.Message}");
        }

        var error = root.Value<string>("error");
        if (!string.IsNullOrEmpty(error))
        {
            // invalid_grant means the refresh token was revoked or expired; re-run consent.
            throw new AiServiceException(
                $"Google OAuth error: {error} {root.Value<string>("error_description")}");
        }

        var token = root.Value<string>("access_token");
        if (string.IsNullOrWhiteSpace(token))
            throw new AiServiceException("Google OAuth response contained no access token.");

        var expires = root["expires_in"];
        if (expires != null) lifetimeSeconds = expires.Value<int>();

        return token;
    }
}
