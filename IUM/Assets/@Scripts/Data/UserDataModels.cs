using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Serializable]
public sealed class UserDataDocument
{
    public int SchemaVersion { get; set; } = UserDataMigrator.CurrentSchemaVersion;
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public UserProfileData Profile { get; set; } = new();
    public UserSettingsData Settings { get; set; } = new();
    public UserProgressData Progress { get; set; } = new();
    public bool LegacyPlayerPrefsMigrated { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
}

[Serializable]
public sealed class UserProfileData
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

[Serializable]
public sealed class UserSettingsData
{
    public float MasterVolume { get; set; } = 1f;
}

[Serializable]
public sealed class UserProgressData
{
    public string CurrentChapter { get; set; } = "intro";
    public List<string> CompletedEvents { get; set; } = new();
}
