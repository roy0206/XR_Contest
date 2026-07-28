using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Service account key file, as downloaded from the Google Cloud console.
/// Property names are the ones Google writes, so the loader reads the file unchanged.
/// </summary>
[Serializable]
public sealed class GoogleServiceAccount
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("project_id")] public string ProjectId { get; set; }
    [JsonProperty("private_key_id")] public string PrivateKeyId { get; set; }
    [JsonProperty("private_key")] public string PrivateKey { get; set; }
    [JsonProperty("client_email")] public string ClientEmail { get; set; }
    [JsonProperty("token_uri")] public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";

    [JsonIgnore]
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(PrivateKey) &&
        !string.IsNullOrWhiteSpace(ClientEmail) &&
        !string.IsNullOrWhiteSpace(TokenUri);
}

/// <summary>
/// Turns a service account key into OAuth 2.0 access tokens, which is the authentication
/// method Google documents for Cloud Text-to-Speech and Speech-to-Text. Tokens live for an
/// hour, so they are cached and refreshed shortly before expiry instead of per request.
/// </summary>
public sealed class GoogleServiceAccountTokenProvider : IGoogleTokenProvider
{
    const string Scope = "https://www.googleapis.com/auth/cloud-platform";
    const int TokenLifetimeSeconds = 3600;

    /// <summary>Refresh this early so a request never starts with a token about to expire.</summary>
    const int RefreshMarginSeconds = 300;

    readonly GoogleServiceAccount _account;
    readonly SemaphoreSlim _lock = new(1, 1);
    readonly float _timeoutSeconds;

    string _token;
    DateTime _expiresAtUtc;

    public GoogleServiceAccountTokenProvider(GoogleServiceAccount account, float timeoutSeconds = 15f)
    {
        _account = account;
        _timeoutSeconds = timeoutSeconds;
    }

    public bool IsUsable => _account != null && _account.IsUsable;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsUsable)
            throw new AiServiceException("The Google service account key is missing or incomplete.");

        if (_token != null && DateTime.UtcNow < _expiresAtUtc)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // A second caller that queued behind the refresh must not fetch again.
            if (_token != null && DateTime.UtcNow < _expiresAtUtc)
                return _token;

            var assertion = await Task.Run(BuildAssertion, cancellationToken);
            var form = "grant_type=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(
                           "urn:ietf:params:oauth:grant-type:jwt-bearer") +
                       "&assertion=" + assertion;

            var response = await AiHttp.PostFormAsync(
                _account.TokenUri, form, null, _timeoutSeconds, cancellationToken);

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

    /// <summary>Builds and signs the JWT bearer assertion. Runs off the main thread.</summary>
    string BuildAssertion()
    {
        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new JObject
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT"
        };

        var claims = new JObject
        {
            ["iss"] = _account.ClientEmail,
            ["scope"] = Scope,
            ["aud"] = _account.TokenUri,
            ["iat"] = issuedAt,
            ["exp"] = issuedAt + TokenLifetimeSeconds
        };

        var payload = $"{Base64Url(header.ToString(Formatting.None))}.{Base64Url(claims.ToString(Formatting.None))}";

        using var rsa = CreateRsa(_account.PrivateKey);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{payload}.{Base64Url(signature)}";
    }

    static RSA CreateRsa(string privateKeyPem)
    {
        var der = Convert.FromBase64String(StripPem(privateKeyPem));
        var rsa = RSA.Create();
        try
        {
            // Service account keys are PKCS#8 ("BEGIN PRIVATE KEY").
            rsa.ImportPkcs8PrivateKey(der, out _);
            return rsa;
        }
        catch (Exception)
        {
            rsa.Dispose();
            throw;
        }
    }

    static string StripPem(string pem)
    {
        var builder = new StringBuilder(pem.Length);
        foreach (var line in pem.Split('\n'))
        {
            var trimmed = line.Trim().Trim('\r');
            if (trimmed.Length == 0 || trimmed.StartsWith("-----", StringComparison.Ordinal)) continue;
            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    static string ParseToken(string json, out int lifetimeSeconds)
    {
        lifetimeSeconds = TokenLifetimeSeconds;
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
            throw new AiServiceException($"Google OAuth error: {error} {root.Value<string>("error_description")}");

        var token = root.Value<string>("access_token");
        if (string.IsNullOrWhiteSpace(token))
            throw new AiServiceException("Google OAuth response contained no access token.");

        var expires = root["expires_in"];
        if (expires != null) lifetimeSeconds = expires.Value<int>();

        return token;
    }

    static string Base64Url(string value) => Base64Url(Encoding.UTF8.GetBytes(value));

    static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Header set used by both Google services once a token is available.</summary>
    public async Task<Dictionary<string, string>> BuildAuthorizationHeadersAsync(CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }
}
