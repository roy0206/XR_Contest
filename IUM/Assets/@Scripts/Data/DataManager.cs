using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class DataManager : Singleton<DataManager>
{
    public const string ManifestAddress = "ium/data/manifest";
    const string UserFileName = "user.json";

    readonly SemaphoreSlim _saveLock = new(1, 1);
    JsonDataSerializer _serializer;
    Task _initializationTask;
    string _userFilePath;
    string _userTempPath;

    public bool IsReady { get; private set; }
    public Exception InitializationError { get; private set; }
    public DataManifest Manifest { get; private set; }
    public StaticDataService Static { get; private set; }
    public UserDataDocument User { get; private set; }
    public UserSettingsData Settings => User?.Settings;
    public UserProgressData Progress => User?.Progress;

    public event Action Ready;
    public event Action<Exception> InitializationFailed;
    public event Action<Exception> SaveFailed;

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;
        _ = InitializeAsync();
    }

    public Task InitializeAsync() => _initializationTask ??= InitializeInternalAsync();

    async Task InitializeInternalAsync()
    {
        try
        {
            _serializer = new JsonDataSerializer();
            var userDirectory = Path.Combine(Application.persistentDataPath, "UserData");
            _userFilePath = Path.Combine(userDirectory, UserFileName);
            _userTempPath = _userFilePath + ".tmp";

            IDataTextProvider provider = new AddressableDataProvider();
            await provider.InitializeAsync();
            var manifestJson = await provider.ReadTextAsync(ManifestAddress);
            Manifest = _serializer.Deserialize<DataManifest>(manifestJson);
            ValidateManifest(Manifest);

            Static = new StaticDataService(_serializer, provider);
            await Static.LoadAsync(Manifest.Files);

            User = LoadUser();
            ApplyAudioSettings();

            IsReady = true;
            Ready?.Invoke();
            Debug.Log($"[DataManager] Initialized schema {Manifest.SchemaVersion}.");
        }
        catch (Exception exception)
        {
            InitializationError = exception;
            InitializationFailed?.Invoke(exception);
            Debug.LogException(exception);
            throw;
        }
    }

    /// <summary>
    /// Applies stored volumes to the mixer. Call again after an options change.
    ///
    /// 두 곳에 싣는다. 구 <see cref="AudioManager"/>는 효과음·배경음 재생을 아직 맡고 있고,
    /// <c>Core.Audio</c> 버스는 대사와 영상 음량의 권한이다 (ISSUE-002, ISSUE-015). 후자가
    /// 없으면 조용히 건너뛴다 — 볼륨 적용 실패가 게임을 막을 이유는 없다.
    /// </summary>
    public void ApplyAudioSettings()
    {
        if (User == null) return;

        var audio = AudioManager.Instance;
        audio.MasterVolume = User.Settings.MasterVolume;
        audio.BGMVolume = User.Settings.MusicVolume;
        audio.SFXVolume = User.Settings.EnvironmentVolume;

        if (!Core.Audio.AudioManager.TryGetInstance(out var buses)) return;

        var mixer = buses.Mixer;
        mixer.MasterVolume = User.Settings.MasterVolume;
        mixer.BgmVolume = User.Settings.MusicVolume;
        mixer.SfxVolume = User.Settings.EnvironmentVolume;
        mixer.DialogueVolume = User.Settings.DialogueVolume;
        mixer.VideoVolume = User.Settings.VideoVolume;
    }

    /// <summary>
    /// True when a save file existed but could not be read. Play continues from defaults; this only
    /// lets 메인 화면 tell the player why 이어하기 is unavailable (F-001 1.8).
    /// </summary>
    public bool UserDataCorrupted { get; private set; }

    /// <summary>A missing or unreadable save starts from defaults instead of blocking play.</summary>
    UserDataDocument LoadUser()
    {
        UserDataDocument document = null;
        try
        {
            if (File.Exists(_userFilePath))
                document = _serializer.Deserialize<UserDataDocument>(
                    File.ReadAllText(_userFilePath, Encoding.UTF8));
        }
        catch (Exception exception)
        {
            // Distinguished from "no save yet": a file that is there but unreadable is worth
            // telling the player about, an absent one is not.
            UserDataCorrupted = true;
            Debug.LogWarning($"[DataManager] Save file could not be read: {exception.Message}");
        }

        document ??= new UserDataDocument();
        document.Settings ??= new UserSettingsData();
        document.Progress ??= new UserProgressData();
        document.Settings.Clamp();
        return document;
    }

    /// <summary>Saving never throws, so a failed write cannot stop the current process.</summary>
    public async Task SaveUserAsync()
    {
        await InitializeAsync();
        var json = _serializer.Serialize(User);

        await _saveLock.WaitAsync();
        try
        {
            await Task.Run(() => WriteAtomic(json));
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke(exception);
            Debug.LogError($"[DataManager] Save failed: {exception.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    // Write to a temporary file first so an interrupted save leaves the previous file intact.
    void WriteAtomic(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_userFilePath));
        File.WriteAllText(_userTempPath, json, new UTF8Encoding(false));

        if (!File.Exists(_userFilePath))
        {
            File.Move(_userTempPath, _userFilePath);
            return;
        }

        try
        {
            File.Replace(_userTempPath, _userFilePath, null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Delete(_userFilePath);
            File.Move(_userTempPath, _userFilePath);
        }
    }

    static void ValidateManifest(DataManifest manifest)
    {
        if (manifest.SchemaVersion <= 0)
            throw new DataLoadException("Data manifest schemaVersion must be positive.");
        if (manifest.Files == null)
            throw new DataLoadException("Data manifest has no files collection.");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Key) || string.IsNullOrWhiteSpace(file.Address))
                throw new DataLoadException("Every manifest file needs a key and Addressables address.");
            if (!keys.Add(file.Key))
                throw new DataLoadException($"Duplicate static data key '{file.Key}'.");
        }
    }
}
