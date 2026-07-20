using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class UserDataService
{
    const string LegacyVolumeKey = "IUM.MasterVolume";
    const string FileName = "user.json";

    readonly JsonDataSerializer _serializer;
    readonly UserDataMigrator _migrator;
    readonly SemaphoreSlim _saveLock = new(1, 1);
    readonly string _directoryPath;
    readonly string _filePath;
    readonly string _backupPath;
    readonly string _temporaryPath;

    public UserDataDocument Current { get; private set; }
    public string FilePath => _filePath;
    public event Action<UserDataDocument> Changed;

    public UserDataService(JsonDataSerializer serializer, UserDataMigrator migrator)
    {
        _serializer = serializer;
        _migrator = migrator;
        _directoryPath = Path.Combine(Application.persistentDataPath, "UserData");
        _filePath = Path.Combine(_directoryPath, FileName);
        _backupPath = _filePath + ".backup";
        _temporaryPath = _filePath + ".tmp";
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directoryPath);
        var shouldSave = false;
        JObject raw = null;

        if (File.Exists(_filePath))
        {
            try
            {
                raw = await ReadObjectAsync(_filePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UserData] Primary save is invalid: {exception.Message}");
                PreserveCorruptPrimary();
                shouldSave = true;
            }
        }

        if (raw == null && File.Exists(_backupPath))
        {
            try
            {
                raw = await ReadObjectAsync(_backupPath);
                Debug.LogWarning("[UserData] Restored data from backup.");
                shouldSave = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UserData] Backup save is invalid: {exception.Message}");
            }
        }

        if (raw == null)
        {
            Current = new UserDataDocument();
            shouldSave = true;
        }
        else
        {
            raw = _migrator.Migrate(raw, out var migrated);
            Current = _serializer.Convert<UserDataDocument>(raw);
            Normalize(Current);
            shouldSave |= migrated;
        }

        if (!Current.LegacyPlayerPrefsMigrated)
        {
            if (PlayerPrefs.HasKey(LegacyVolumeKey))
                Current.Settings.MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(LegacyVolumeKey, 1f));
            Current.LegacyPlayerPrefsMigrated = true;
            shouldSave = true;
        }

        if (shouldSave)
        {
            await SaveAsync();
            if (PlayerPrefs.HasKey(LegacyVolumeKey))
            {
                PlayerPrefs.DeleteKey(LegacyVolumeKey);
                PlayerPrefs.Save();
            }
        }
    }

    public void Update(Action<UserDataDocument> mutation)
    {
        if (Current == null)
            throw new InvalidOperationException("UserData has not been initialized.");
        mutation?.Invoke(Current);
        Normalize(Current);
        Changed?.Invoke(Current);
    }

    public async Task SaveAsync()
    {
        if (Current == null)
            throw new InvalidOperationException("UserData has not been initialized.");

        Current.SchemaVersion = UserDataMigrator.CurrentSchemaVersion;
        Current.SavedAtUtc = DateTimeOffset.UtcNow;
        var json = _serializer.Serialize(Current);

        await _saveLock.WaitAsync();
        try
        {
            await Task.Run(() => WriteAtomic(json));
        }
        finally
        {
            _saveLock.Release();
        }
    }

    async Task<JObject> ReadObjectAsync(string path)
    {
        var json = await Task.Run(() => File.ReadAllText(path, Encoding.UTF8));
        return _serializer.DeserializeToken(json) as JObject
               ?? throw new DataLoadException($"UserData file '{path}' must contain a JSON object.");
    }

    void WriteAtomic(string json)
    {
        Directory.CreateDirectory(_directoryPath);
        var utf8 = new UTF8Encoding(false);
        using (var stream = new FileStream(_temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, utf8))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        if (!File.Exists(_filePath))
        {
            File.Move(_temporaryPath, _filePath);
            return;
        }

        try
        {
            File.Replace(_temporaryPath, _filePath, _backupPath, true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceFallback();
        }
        catch (IOException)
        {
            ReplaceFallback();
        }
    }

    void ReplaceFallback()
    {
        File.Copy(_filePath, _backupPath, true);
        File.Delete(_filePath);
        File.Move(_temporaryPath, _filePath);
    }

    void PreserveCorruptPrimary()
    {
        if (!File.Exists(_filePath)) return;
        var corruptPath = Path.Combine(
            _directoryPath,
            $"user.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.Move(_filePath, corruptPath);
    }

    static void Normalize(UserDataDocument document)
    {
        document.Profile ??= new UserProfileData();
        document.Settings ??= new UserSettingsData();
        document.Progress ??= new UserProgressData();
        document.Progress.CompletedEvents ??= new System.Collections.Generic.List<string>();
        document.Settings.MasterVolume = Mathf.Clamp01(document.Settings.MasterVolume);
    }
}

public sealed class UserDataMigrator
{
    public const int CurrentSchemaVersion = 1;

    public JObject Migrate(JObject document, out bool changed)
    {
        document ??= new JObject();
        changed = false;
        var version = document.Value<int?>("schemaVersion") ?? 0;

        if (version > CurrentSchemaVersion)
            throw new DataLoadException(
                $"UserData schema {version} is newer than supported schema {CurrentSchemaVersion}.");

        while (version < CurrentSchemaVersion)
        {
            switch (version)
            {
                case 0:
                    MigrateVersion0To1(document);
                    version = 1;
                    changed = true;
                    break;
                default:
                    throw new DataLoadException($"No UserData migration exists for schema {version}.");
            }
        }

        return document;
    }

    static void MigrateVersion0To1(JObject document)
    {
        document["schemaVersion"] = 1;
        document["profile"] ??= JObject.FromObject(new UserProfileData());
        document["settings"] ??= JObject.FromObject(new UserSettingsData());
        document["progress"] ??= JObject.FromObject(new UserProgressData());
        document["legacyPlayerPrefsMigrated"] ??= false;
    }
}
