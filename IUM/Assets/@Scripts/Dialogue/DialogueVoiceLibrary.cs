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
    /// <summary>
    /// 합성 결과를 들고 있을 최대 개수. 넘으면 가장 오래된 것부터 버린다.
    ///
    /// 상한을 두는 이유는 기기 메모리를 아직 재보지 않았기 때문이다. 한 공정의 대사가 수십 줄
    /// 규모라 이 정도면 재방문 시 재합성을 거의 없앨 수 있다.
    /// </summary>
    const int MaxCachedClips = 64;

    readonly Dictionary<AudioClip, AsyncOperationHandle<AudioClip>> _loaded = new();
    readonly HashSet<AudioClip> _synthesized = new();

    // 캐시된 클립은 개별 Release로 파괴하지 않는다. 소유권이 캐시에 있고, 같은 대사가 다시
    // 나오면 그대로 재생해야 한다.
    readonly Dictionary<string, AudioClip> _cache = new();
    readonly Queue<string> _cacheOrder = new();

    readonly Func<DialogueSpeaker, IAiTextToSpeechService> _resolveTextToSpeech;

    /// <param name="resolveTextToSpeech">
    /// 화자별 TTS 공급자. Null이면 녹음 클립만 쓰며, 그것이 오프라인에서 안전한 구성이다.
    ///
    /// 서비스를 직접 받지 않고 함수로 받는다. 음색 설정은 파일에서 비동기로 읽으므로 이 객체가
    /// 만들어지는 시점에는 아직 준비되지 않을 수 있고, 매번 물어보면 늦게 준비돼도 그때부터
    /// 소리가 붙는다.
    /// </param>
    public DialogueVoiceLibrary(Func<DialogueSpeaker, IAiTextToSpeechService> resolveTextToSpeech = null) =>
        _resolveTextToSpeech = resolveTextToSpeech;

    public bool HasTextToSpeech => _resolveTextToSpeech != null;

    public async Task<AudioClip> LoadAsync(DialogueLine line, CancellationToken cancellationToken)
    {
        if (line == null) return null;

        if (!string.IsNullOrWhiteSpace(line.VoiceAddress))
        {
            var recorded = await LoadRecordedAsync(line.VoiceAddress, cancellationToken);
            if (recorded != null) return recorded;
        }

        // Prepare()가 줄마다 시퀀스 화자를 채워 두므로 여기서는 비어 있지 않다. 그래도 옛 데이터를
        // 대비해 기본값을 둔다.
        return await SynthesizeAsync(line.Text, line.Speaker ?? DialogueSpeaker.Nojang, cancellationToken);
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

    async Task<AudioClip> SynthesizeAsync(string text, DialogueSpeaker speaker, CancellationToken cancellationToken)
    {
        if (_resolveTextToSpeech == null || string.IsNullOrWhiteSpace(text)) return null;

        // 대사는 고정 스크립트라 같은 줄이 여러 번 나온다 — 재시도한 공정의 설명, 재안내 대사,
        // 이어하기로 되돌아온 구간이 그렇다. 캐시가 그만큼의 API 호출과 지연을 없앤다.
        var key = CacheKey(text, speaker);
        if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

        var service = _resolveTextToSpeech(speaker);
        if (service == null) return null;

        try
        {
            var clip = await service.SynthesizeAsync(text, cancellationToken);
            if (clip != null) Cache(key, clip);
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

    /// <summary>화자가 다르면 목소리가 다르므로 같은 문장도 다른 항목이다.</summary>
    static string CacheKey(string text, DialogueSpeaker speaker) => $"{(int)speaker}:{text}";

    void Cache(string key, AudioClip clip)
    {
        _synthesized.Add(clip);
        _cache[key] = clip;
        _cacheOrder.Enqueue(key);

        while (_cacheOrder.Count > MaxCachedClips)
        {
            var oldest = _cacheOrder.Dequeue();
            if (!_cache.TryGetValue(oldest, out var evicted)) continue;

            _cache.Remove(oldest);
            if (evicted == null) continue;

            _synthesized.Remove(evicted);
            UnityEngine.Object.Destroy(evicted);
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

        // 합성 클립은 여기서 파괴하지 않는다. 전부 캐시가 소유하고 있어서, 한 줄이 끝날 때마다
        // 버리면 캐시가 성립하지 않는다. 정리는 캐시에서 밀려날 때와 ReleaseAll에서 한다.
    }

    /// <summary>
    /// Releases everything still held. Called on scene unload.
    ///
    /// 캐시도 함께 비운다. 씬을 넘어 들고 있으면 재합성은 더 줄지만, 그만큼의 메모리를 기기에서
    /// 재보지 않았다. 같은 씬 안의 재시도와 재안내만으로도 캐시가 값을 한다.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var pair in _loaded)
            if (pair.Value.IsValid()) Addressables.Release(pair.Value);
        _loaded.Clear();

        foreach (var clip in _synthesized)
            if (clip != null) UnityEngine.Object.Destroy(clip);
        _synthesized.Clear();

        _cache.Clear();
        _cacheOrder.Clear();
    }
}
