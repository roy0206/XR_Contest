using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public sealed class StaticDataService
{
    readonly Dictionary<string, JToken> _tables = new(StringComparer.OrdinalIgnoreCase);
    readonly JsonDataSerializer _serializer;
    readonly IDataTextProvider _provider;

    public IEnumerable<string> Keys => _tables.Keys;

    public StaticDataService(JsonDataSerializer serializer, IDataTextProvider provider)
    {
        _serializer = serializer;
        _provider = provider;
    }

    public async Task LoadAsync(IEnumerable<StaticDataEntry> files)
    {
        _tables.Clear();
        foreach (var file in files)
        {
            ValidateEntry(file);

            try
            {
                var json = await _provider.ReadTextAsync(file.Address);
                if (!_tables.TryAdd(file.Key, _serializer.DeserializeToken(json)))
                    throw new DataLoadException($"Duplicate static table key '{file.Key}'.");
            }
            catch (Exception exception) when (!file.Required)
            {
                UnityEngine.Debug.LogWarning($"[StaticData] Optional table '{file.Key}' was skipped: {exception.Message}");
            }
        }
    }

    public bool Contains(string key) => !string.IsNullOrWhiteSpace(key) && _tables.ContainsKey(key);

    public JToken GetToken(string key)
    {
        if (!_tables.TryGetValue(key, out var token))
            throw new KeyNotFoundException($"Static data table '{key}' is not loaded.");
        return token;
    }

    public T Get<T>(string key) => _serializer.Convert<T>(GetToken(key));

    public bool TryGet<T>(string key, out T value)
    {
        if (_tables.TryGetValue(key, out var token))
        {
            value = _serializer.Convert<T>(token);
            return true;
        }

        value = default;
        return false;
    }

    static void ValidateEntry(StaticDataEntry file)
    {
        if (string.IsNullOrWhiteSpace(file.Key))
            throw new DataLoadException("A static data entry has no key.");
        if (string.IsNullOrWhiteSpace(file.Address))
            throw new DataLoadException($"Static data entry '{file.Key}' has no Addressables address.");
    }
}
