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
        // autoReleaseHandle: false. The parameterless overload reclaims the handle the moment the
        // operation completes, which lands before the await resumes through the synchronization
        // context — reading Status afterwards then throws on an already-released handle. Owning
        // the release here keeps the status check and the finally below consistent.
        var handle = Addressables.InitializeAsync(false);
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
