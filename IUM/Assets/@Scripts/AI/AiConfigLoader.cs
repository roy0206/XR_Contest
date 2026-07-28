using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Reads the AI data files. Swapping this for an Addressables-backed source is the only
/// change needed to move the AI tables next to the rest of the static data.
/// </summary>
public interface IAiTextFileSource
{
    /// <summary>Returns null when the file does not exist. Missing files are not an error.</summary>
    Task<string> ReadAsync(string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// StreamingAssets with a persistentDataPath override. The override lets a build be
/// re-keyed on device (adb push) without rebuilding, which is how secrets reach the Quest.
/// </summary>
public sealed class AiLocalTextFileSource : IAiTextFileSource
{
    public const string OverrideDirectoryName = "AiConfig";

    public static string OverrideDirectory =>
        Path.Combine(Application.persistentDataPath, OverrideDirectoryName);

    public async Task<string> ReadAsync(string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var overridePath = Path.Combine(OverrideDirectory, fileName);
        try
        {
            if (File.Exists(overridePath))
                return await Task.Run(() => File.ReadAllText(overridePath), cancellationToken);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AI] Override file '{fileName}' could not be read: {exception.Message}");
        }

        var streamingPath = Path.Combine(Application.streamingAssetsPath, fileName);

        // On Android StreamingAssets lives inside the APK, so only UnityWebRequest can read it.
        if (streamingPath.Contains("://"))
            return await AiHttp.ReadStreamingTextAsync(streamingPath, cancellationToken);

        try
        {
            if (File.Exists(streamingPath))
                return await Task.Run(() => File.ReadAllText(streamingPath), cancellationToken);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AI] StreamingAssets file '{fileName}' could not be read: {exception.Message}");
        }

        return null;
    }
}

/// <summary>Everything the conversation pipeline needs, resolved once at startup.</summary>
public sealed class AiRuntimeSettings
{
    public AiConfig Config { get; set; } = new();
    public AiSecrets Secrets { get; set; } = new();

    /// <summary>Google service account key, the documented auth method for Cloud TTS/STT.</summary>
    public GoogleServiceAccount GoogleServiceAccount { get; set; } = new();

    /// <summary>OAuth client, used with a refresh token when service account keys are blocked.</summary>
    public GoogleOAuthClient GoogleOAuthClient { get; set; } = new();
    public AiKnowledgeBase Knowledge { get; set; } = AiKnowledgeBase.CreateEmpty();
    public AiDialogueBank Dialogue { get; set; } = AiDialogueBank.CreateDefault();
}

public static class AiConfigLoader
{
    public const string ConfigFileName = "ai_config.json";
    public const string SecretsFileName = "ai_secrets.json";

    /// <summary>The key file downloaded from the Google Cloud console, used unchanged.</summary>
    public const string ServiceAccountFileName = "ai_service_account.json";

    /// <summary>The OAuth client file downloaded from the Cloud console, used unchanged.</summary>
    public const string OAuthClientFileName = "ai_oauth_client.json";
    public const string KnowledgeFileName = "ai_knowledge.json";
    public const string DialogueFileName = "ai_dialogue.json";

    /// <summary>
    /// Never throws. A missing or broken file degrades to defaults so the pipeline
    /// still runs in mock mode instead of blocking the process.
    /// </summary>
    public static async Task<AiRuntimeSettings> LoadAsync(
        IAiTextFileSource source,
        CancellationToken cancellationToken = default)
    {
        source ??= new AiLocalTextFileSource();
        var serializer = new JsonDataSerializer();

        var config = await ReadAsync(source, serializer, ConfigFileName, new AiConfig(), cancellationToken);
        config.Clamp();

        var secrets = await ReadAsync(source, serializer, SecretsFileName, new AiSecrets(), cancellationToken);
        var serviceAccount = await ReadAsync(
            source, serializer, ServiceAccountFileName, new GoogleServiceAccount(), cancellationToken);
        var oauthClient = await ReadAsync(
            source, serializer, OAuthClientFileName, new GoogleOAuthClient(), cancellationToken);
        var knowledge = await ReadAsync(source, serializer, KnowledgeFileName, AiKnowledgeBase.CreateEmpty(), cancellationToken);
        var dialogue = await ReadAsync(source, serializer, DialogueFileName, AiDialogueBank.CreateDefault(), cancellationToken);

        knowledge.Prepare();
        dialogue.Prepare();

        return new AiRuntimeSettings
        {
            Config = config,
            Secrets = secrets,
            GoogleServiceAccount = serviceAccount,
            GoogleOAuthClient = oauthClient,
            Knowledge = knowledge,
            Dialogue = dialogue
        };
    }

    static async Task<T> ReadAsync<T>(
        IAiTextFileSource source,
        JsonDataSerializer serializer,
        string fileName,
        T fallback,
        CancellationToken cancellationToken) where T : class
    {
        string json;
        try
        {
            json = await source.ReadAsync(fileName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AI] '{fileName}' could not be loaded: {exception.Message}");
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(json))
            return fallback;

        try
        {
            return serializer.Deserialize<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[AI] '{fileName}' is invalid and was ignored: {exception.Message}");
            return fallback;
        }
    }
}
