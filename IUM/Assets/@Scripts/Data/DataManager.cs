using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed class DataManager : Singleton<DataManager>
{
    public const string ManifestAddress = "ium/data/manifest";

    Task _initializationTask;

    public bool IsReady { get; private set; }
    public Exception InitializationError { get; private set; }
    public DataManifest Manifest { get; private set; }
    public StaticDataService Static { get; private set; }
    public UserDataService User { get; private set; }

    public event Action Ready;
    public event Action<Exception> InitializationFailed;

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
            var serializer = new JsonDataSerializer();
            IDataTextProvider provider = new AddressableDataProvider();
            await provider.InitializeAsync();
            var manifestJson = await provider.ReadTextAsync(ManifestAddress);
            Manifest = serializer.Deserialize<DataManifest>(manifestJson);
            ValidateManifest(Manifest);

            User = new UserDataService(serializer, new UserDataMigrator());
            Static = new StaticDataService(serializer, provider);

            await User.InitializeAsync();
            await Static.LoadAsync(Manifest.Files);
            AudioManager.Instance.MasterVolume = User.Current.Settings.MasterVolume;

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

    public async Task SaveUserAsync()
    {
        await InitializeAsync();
        await User.SaveAsync();
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
