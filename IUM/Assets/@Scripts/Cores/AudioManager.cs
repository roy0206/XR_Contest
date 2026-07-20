using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Quest-friendly pooled audio service with bounded SFX voices and optional spatialization.
/// Existing PlaySound/PlayBGM API is preserved.
/// </summary>
public class AudioManager : Singleton<AudioManager>, ISceneEventListener
{
    const string MasterParam = "MASTER";
    const string SfxParam = "SFX";
    const string BgmParam = "BGM";

    [Header("Mixer")]
    [SerializeField] AudioMixer mixer;
    [SerializeField] AudioMixerGroup sfxGroup;
    [SerializeField] AudioMixerGroup bgmGroup;

    [Header("Audio Catalog")]
    [SerializeField] List<AudioClip> clips = new();
    [SerializeField] bool loadResourcesFallback = true;
    [SerializeField] string resourcesPath = "Audio";

    [Header("Quest SFX Pool")]
    [SerializeField, Min(1)] int initialPoolSize = 12;
    [SerializeField, Min(1)] int maxSfxChannels = 32;
    [SerializeField] bool enableSpatialization = true;
    [SerializeField, Range(0f, 1f)] float spatialBlend = 1f;
    [SerializeField, Min(0.01f)] float minDistance = 1f;
    [SerializeField, Min(0.01f)] float maxDistance = 15f;

    [Header("Volume (0..1)")]
    [SerializeField, Range(0f, 1f)] float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;
    [SerializeField, Range(0f, 1f)] float bgmVolume = 1f;

    struct ChannelState
    {
        public AudioSource source;
        public Transform followTarget;
        public bool stopWithTarget;
        public int remaining;
    }

    readonly Dictionary<string, AudioClip> _clips = new();
    readonly Dictionary<int, ChannelState> _active = new();
    readonly Dictionary<string, AudioSource> _bgmLayers = new();
    readonly Queue<AudioSource> _available = new();
    readonly List<int> _toRemove = new();
    readonly List<int> _toReplay = new();

    Transform _poolParent;
    int _channelCount;
    int _nextId = 1;

    public float MasterVolume
    {
        get => masterVolume;
        set
        {
            masterVolume = Mathf.Clamp01(value);
            ApplyVolume(MasterParam, masterVolume);
        }
    }

    public float SFXVolume
    {
        get => sfxVolume;
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            ApplyVolume(SfxParam, sfxVolume);
        }
    }

    public float BGMVolume
    {
        get => bgmVolume;
        set
        {
            bgmVolume = Mathf.Clamp01(value);
            ApplyVolume(BgmParam, bgmVolume);
        }
    }

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;

        _poolParent = transform;
        maxSfxChannels = Mathf.Max(1, maxSfxChannels);
        initialPoolSize = Mathf.Clamp(initialPoolSize, 1, maxSfxChannels);

        LoadAllSounds();
        for (var i = 0; i < initialPoolSize; i++)
            _available.Enqueue(CreateChannel());

        ApplyVolume(MasterParam, masterVolume);
        ApplyVolume(SfxParam, sfxVolume);
        ApplyVolume(BgmParam, bgmVolume);

        SceneController.Instance?.RegisterListener(this);
    }

    protected override void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);
        base.OnDestroy();
    }

    void Update()
    {
        if (_active.Count == 0) return;
        _toRemove.Clear();
        _toReplay.Clear();

        foreach (var pair in _active)
        {
            var state = pair.Value;
            if (state.source == null)
            {
                _toRemove.Add(pair.Key);
                continue;
            }

            if (state.followTarget != null)
                state.source.transform.position = state.followTarget.position;
            else if (state.stopWithTarget)
            {
                _toRemove.Add(pair.Key);
                continue;
            }

            if (state.source.isPlaying) continue;
            if (state.remaining > 1) _toReplay.Add(pair.Key);
            else _toRemove.Add(pair.Key);
        }

        for (var i = 0; i < _toReplay.Count; i++)
        {
            var id = _toReplay[i];
            if (!_active.TryGetValue(id, out var state) || state.source == null) continue;
            state.remaining--;
            _active[id] = state;
            state.source.Play();
        }

        for (var i = 0; i < _toRemove.Count; i++)
            ReturnChannel(_toRemove[i]);
    }

    public int PlaySound(string soundName, Transform sourceParent, float volume = 1f, int repeatTime = 1) =>
        PlaySoundInternal(soundName, sourceParent, volume, repeatTime, false, spatialBlend);

    public int PlayLoopingSound(string soundName, Transform sourceParent, float volume = 1f) =>
        PlaySoundInternal(soundName, sourceParent, volume, 1, true, spatialBlend);

    public int PlayGlobalSound(string soundName, float volume = 1f, int repeatTime = 1) =>
        PlaySoundInternal(soundName, null, volume, repeatTime, false, 0f);

    public void SetSoundVolume(int id, float volume)
    {
        if (_active.TryGetValue(id, out var state) && state.source != null)
            state.source.volume = Mathf.Clamp01(volume);
    }

    public bool IsSoundPlaying(int id) =>
        _active.TryGetValue(id, out var state) && state.source != null && state.source.isPlaying;

    public void StopSound(int id) => ReturnChannel(id);

    int PlaySoundInternal(
        string soundName,
        Transform sourceParent,
        float volume,
        int repeatTime,
        bool loop,
        float sourceSpatialBlend)
    {
        if (string.IsNullOrWhiteSpace(soundName) || !_clips.TryGetValue(soundName, out var clip))
        {
            Debug.LogWarning($"[AudioManager] Unknown audio clip: '{soundName}'.");
            return -1;
        }

        var source = GetChannel();
        if (source == null) return -1;

        // Pooled channels must remain owned by the manager. Parenting a channel to
        // a transient emitter destroys the pooled AudioSource with that emitter.
        source.transform.SetParent(_poolParent, false);
        source.transform.position = sourceParent != null ? sourceParent.position : Vector3.zero;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.loop = loop;
        source.spatialBlend = Mathf.Clamp01(sourceSpatialBlend);
        source.spatialize = enableSpatialization && source.spatialBlend > 0.001f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance, maxDistance);
        source.Play();

        var id = NextId();
        _active[id] = new ChannelState
        {
            source = source,
            followTarget = sourceParent,
            stopWithTarget = sourceParent != null,
            remaining = loop ? int.MaxValue : Mathf.Max(1, repeatTime)
        };
        return id;
    }

    AudioSource GetChannel()
    {
        while (_available.Count > 0 && _available.Peek() == null)
        {
            _available.Dequeue();
            _channelCount = Mathf.Max(0, _channelCount - 1);
        }

        if (_available.Count == 0 && _channelCount < maxSfxChannels)
            _available.Enqueue(CreateChannel());

        if (_available.Count == 0)
        {
            var oldestOneShot = int.MaxValue;
            foreach (var pair in _active)
                if (pair.Key < oldestOneShot && pair.Value.source != null && !pair.Value.source.loop)
                    oldestOneShot = pair.Key;

            if (oldestOneShot != int.MaxValue)
                ReturnChannel(oldestOneShot);
        }

        if (_available.Count == 0)
        {
            Debug.LogWarning("[AudioManager] SFX voice limit reached; sound was skipped.");
            return null;
        }

        var source = _available.Dequeue();
        source.gameObject.SetActive(true);
        return source;
    }

    AudioSource CreateChannel()
    {
        var channel = new GameObject($"SFXChannel_{_channelCount:00}");
        channel.transform.SetParent(_poolParent, false);
        var source = channel.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = sfxGroup;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance, maxDistance);
        channel.SetActive(false);
        _channelCount++;
        return source;
    }

    void ReturnChannel(int id)
    {
        if (!_active.TryGetValue(id, out var state)) return;
        _active.Remove(id);

        var source = state.source;
        if (source == null)
        {
            _channelCount = Mathf.Max(0, _channelCount - 1);
            return;
        }
        source.Stop();
        source.clip = null;
        source.loop = false;
        source.spatialize = false;
        source.transform.SetParent(_poolParent, false);
        source.gameObject.SetActive(false);
        _available.Enqueue(source);
    }

    int NextId()
    {
        if (_nextId == int.MaxValue) _nextId = 1;
        while (_active.ContainsKey(_nextId)) _nextId++;
        return _nextId++;
    }

    public void PlayBGM(string soundName, float volume = 1f, bool loop = true)
    {
        if (!_clips.TryGetValue(soundName, out var clip))
        {
            Debug.LogWarning($"[AudioManager] Unknown BGM clip: '{soundName}'.");
            return;
        }

        var source = GetOrCreateBgmLayer(soundName);
        source.loop = loop;
        source.volume = Mathf.Clamp01(volume);
        if (source.clip == clip && source.isPlaying) return;
        source.clip = clip;
        source.Play();
    }

    public void StopBGM(string soundName)
    {
        if (_bgmLayers.TryGetValue(soundName, out var source) && source != null) source.Stop();
    }

    // Keep the original typo for source compatibility.
    public void StopALlBGM() => StopAllBGM();

    public void StopAllBGM()
    {
        foreach (var source in _bgmLayers.Values)
            if (source != null) source.Stop();
    }

    AudioSource GetOrCreateBgmLayer(string soundName)
    {
        if (_bgmLayers.TryGetValue(soundName, out var source) && source != null) return source;

        var layer = new GameObject($"BGM_{soundName}");
        layer.transform.SetParent(_poolParent, false);
        source = layer.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = bgmGroup;
        _bgmLayers[soundName] = source;
        return source;
    }

    void ApplyVolume(string parameter, float linearValue)
    {
        if (mixer == null)
        {
            if (parameter == MasterParam)
                AudioListener.volume = linearValue;
            return;
        }

        var decibels = linearValue <= 0.0001f ? -80f : Mathf.Log10(linearValue) * 20f;
        mixer.SetFloat(parameter, decibels);
    }

    void LoadAllSounds()
    {
        _clips.Clear();
        for (var i = 0; i < clips.Count; i++) RegisterClip(clips[i]);

        if (loadResourcesFallback)
        {
            var resourceClips = Resources.LoadAll<AudioClip>(resourcesPath ?? string.Empty);
            for (var i = 0; i < resourceClips.Length; i++) RegisterClip(resourceClips[i]);
        }

        if (_clips.Count == 0)
            Debug.LogWarning("[AudioManager] No clips are configured. Assign the clip catalog or Resources/Audio assets.");
    }

    void RegisterClip(AudioClip clip)
    {
        if (clip == null) return;
        if (_clips.ContainsKey(clip.name))
            Debug.LogWarning($"[AudioManager] Duplicate clip name '{clip.name}' was ignored.", clip);
        else
            _clips.Add(clip.name, clip);
    }

    public void OnSceneLoadStart(string sceneName) => DisableAllChannels();
    public void OnSceneLoadComplete(string sceneName) { }

    void DisableAllChannels()
    {
        _toRemove.Clear();
        foreach (var id in _active.Keys) _toRemove.Add(id);
        for (var i = 0; i < _toRemove.Count; i++) ReturnChannel(_toRemove[i]);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        maxSfxChannels = Mathf.Max(1, maxSfxChannels);
        initialPoolSize = Mathf.Clamp(initialPoolSize, 1, maxSfxChannels);
        maxDistance = Mathf.Max(minDistance, maxDistance);
        if (!Application.isPlaying) return;
        ApplyVolume(MasterParam, masterVolume);
        ApplyVolume(SfxParam, sfxVolume);
        ApplyVolume(BgmParam, bgmVolume);
    }
#endif
}
