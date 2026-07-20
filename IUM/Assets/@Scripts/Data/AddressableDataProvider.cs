using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public interface IDataTextProvider
{
    Task InitializeAsync();
    Task<string> ReadTextAsync(string address);
}

/// <summary>Loads static JSON TextAssets by Addressables address.</summary>
public sealed class AddressableDataProvider : IDataTextProvider
{
    Task _initializationTask;

    public Task InitializeAsync() => _initializationTask ??= InitializeInternalAsync();

    async Task InitializeInternalAsync()
    {
        var handle = Addressables.InitializeAsync();
        try
        {
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new DataLoadException("Addressables initialization failed.", handle.OperationException);
        }
        finally
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
    }

    public async Task<string> ReadTextAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("An Addressables address is required.", nameof(address));

        await InitializeAsync();
        var handle = Addressables.LoadAssetAsync<TextAsset>(address);
        try
        {
            var asset = await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
                throw new DataLoadException($"Failed to load Addressable text asset '{address}'.", handle.OperationException);
            return asset.text;
        }
        finally
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
    }
}
