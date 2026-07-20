using System;
using System.Collections.Generic;

[Serializable]
public sealed class DataManifest
{
    public int SchemaVersion { get; set; } = 1;
    public List<StaticDataEntry> Files { get; set; } = new();
}

[Serializable]
public sealed class StaticDataEntry
{
    public string Key { get; set; }
    public string Address { get; set; }
    public bool Required { get; set; } = true;
}

public sealed class DataLoadException : Exception
{
    public DataLoadException(string message) : base(message) { }
    public DataLoadException(string message, Exception innerException) : base(message, innerException) { }
}
