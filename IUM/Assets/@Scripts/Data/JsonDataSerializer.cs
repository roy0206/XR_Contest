using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

public sealed class JsonDataSerializer
{
    readonly JsonSerializerSettings _settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
    };

    public string Serialize<T>(T value) => JsonConvert.SerializeObject(value, _settings);

    public T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DataLoadException($"Cannot deserialize empty JSON as {typeof(T).Name}.");

        try
        {
            var value = JsonConvert.DeserializeObject<T>(json, _settings);
            return value != null
                ? value
                : throw new DataLoadException($"JSON produced a null {typeof(T).Name}.");
        }
        catch (JsonException exception)
        {
            throw new DataLoadException($"Invalid JSON for {typeof(T).Name}: {exception.Message}", exception);
        }
    }

    public JToken DeserializeToken(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DataLoadException("Cannot deserialize empty JSON.");

        try
        {
            return JToken.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new DataLoadException($"Invalid JSON: {exception.Message}", exception);
        }
    }

    public T Convert<T>(JToken token) => token.ToObject<T>(JsonSerializer.Create(_settings));
}
