using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Supplies the audio for a line. Kept behind an interface so the player never learns whether a
/// voice was recorded, synthesized or absent.
/// </summary>
public interface IDialogueVoiceSource
{
    /// <summary>Returns null when no audio could be produced. That is a normal result, not an error.</summary>
    Task<AudioClip> LoadAsync(DialogueLine line, CancellationToken cancellationToken);

    /// <summary>Releases a clip returned by <see cref="LoadAsync"/>. Safe to call with null.</summary>
    void Release(AudioClip clip);
}

/// <summary>
/// Pre-recorded clips first, TTS second, silence third — the order 노장 음성이 사전 녹음으로
/// 확정되든 TTS로 가든 같은 코드가 동작하도록 잡은 것 (F-011 1.3).
///
/// Ownership differs by origin: an Addressables clip must be released through its handle while a
/// synthesized clip is a runtime object that has to be destroyed, so both are tracked here rather
/// than leaving the caller to guess.
/// </summary>
public sealed class DialogueVoiceLibrary : IDialogueVoiceSource
{
    readonly Dictionary<AudioClip, AsyncOperationHandle<AudioClip>> _loaded = new();
    readonly HashSet<AudioClip> _synthesized = new();
    readonly IAiTextToSpeechService _textToSpeech;

    /// <param name="textToSpeech">
    /// Optional. Null means recorded clips only, which is the offline-safe configuration.
    /// </param>
    public DialogueVoiceLibrary(IAiTextToSpeechService textToSpeech = null) => _textToSpeech = textToSpeech;

    public bool HasTextToSpeech => _textToSpeech != null;

    public async Task<AudioClip> LoadAsync(DialogueLine line, CancellationToken cancellationToken)
    {
        if (line == null) return null;

        if (!string.IsNullOrWhiteSpace(line.VoiceAddress))
        {
            var recorded = await LoadRecordedAsync(line.VoiceAddress, cancellationToken);
            if (recorded != null) return recorded;
        }

        return await SynthesizeAsync(line.Text, cancellationToken);
    }

    async Task<AudioClip> LoadRecordedAsync(string address, CancellationToken cancellationToken)
    {
        var handle = Addressables.LoadAssetAsync<AudioClip>(address);
        try
        {
            var clip = await handle.Task;
            cancellationToken.ThrowIfCancellationRequested();

            if (handle.Status != AsyncOperationStatus.Succeeded || clip == null)
            {
                // A missing recording is expected while voice work is outstanding, so this
                // degrades to TTS or silence instead of failing the line.
                Debug.LogWarning($"[Dialogue] 음성 '{address}' 로드 실패. 자막으로 진행합니다.");
                if (handle.IsValid()) Addressables.Release(handle);
                return null;
            }

            _loaded[clip] = handle;
            return clip;
        }
        catch (OperationCanceledException)
        {
            if (handle.IsValid()) Addressables.Release(handle);
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Dialogue] 음성 '{address}' 로드 중 오류: {exception.Message}");
            if (handle.IsValid()) Addressables.Release(handle);
            return null;
        }
    }

    async Task<AudioClip> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        if (_textToSpeech == null || string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            // Not cached: 노장 대사 is fixed script, so caching would pay off, but a cache that
            // outlives a scene costs memory that has not been measured on device yet.
            var clip = await _textToSpeech.SynthesizeAsync(text, cancellationToken);
            if (clip != null) _synthesized.Add(clip);
            return clip;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Dialogue] TTS 합성 실패: {exception.Message}");
            return null;
        }
    }

    public void Release(AudioClip clip)
    {
        if (clip == null) return;

        if (_loaded.TryGetValue(clip, out var handle))
        {
            _loaded.Remove(clip);
            if (handle.IsValid()) Addressables.Release(handle);
            return;
        }

        if (_synthesized.Remove(clip))
            UnityEngine.Object.Destroy(clip);
    }

    /// <summary>Releases everything still held. Called on scene unload.</summary>
    public void ReleaseAll()
    {
        foreach (var pair in _loaded)
            if (pair.Value.IsValid()) Addressables.Release(pair.Value);
        _loaded.Clear();

        foreach (var clip in _synthesized)
            if (clip != null) UnityEngine.Object.Destroy(clip);
        _synthesized.Clear();
    }
}
