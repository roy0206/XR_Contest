using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Audio
{
    /// <summary>
    /// Playground `Core.Audio`의 범용 오디오 매니저. 문자열 ID로 사전 로드된 클립을 재생하며
    /// 로딩 방식은 <see cref="IAudioClipProvider"/>가 담당한다.
    ///
    /// IUM 이식분은 둘이다.
    /// - <see cref="AudioBus.Dialogue"/> 버스 위임 프로퍼티
    /// - <c>spatialize</c> (Quest 스페이셜라이저). 구 IUM <c>AudioManager</c>에만 있던 것이다
    ///
    /// 기반 클래스를 완전 수식하는 이유는 <see cref="Core.Foundation.Singleton{T}"/> 주석에 있다.
    /// 무자격으로 쓰면 IUM 전역 <c>Singleton&lt;T&gt;</c>가 조용히 선택된다.
    /// </summary>
    public sealed class AudioManager : Core.Foundation.Singleton<AudioManager>
    {
        private struct ChannelState
        {
            public AudioSource Source;
            public Transform FollowTarget;
            public bool StopWithTarget;
            public bool Loop;
            public bool SceneBound;
            public int Remaining;
            public float BaseVolume;
            public long Sequence;
        }

        private struct BgmState
        {
            public AudioSource Source;
            public float BaseVolume;
        }

        private readonly Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal);
        private readonly Dictionary<int, ChannelState> activeChannels = new();
        private readonly Dictionary<string, BgmState> bgmLayers = new(StringComparer.Ordinal);
        private readonly Queue<AudioSource> availableChannels = new();
        private readonly List<int> removeBuffer = new();
        private readonly List<int> replayBuffer = new();
        private readonly HashSet<string> missingIdWarnings = new(StringComparer.Ordinal);

        private IAudioClipProvider provider;
        private Transform poolParent;
        private int channelCount;
        private int maxSfxChannels = 32;
        private int nextId = 1;
        private long nextSequence = 1;
        private float spatialBlend = 1f;
        private float minDistance = 1f;
        private float maxDistance = 15f;
        private bool enableSpatialization = true;
        private bool initializationWarningReported;
        private bool voiceLimitWarningReported;

        public bool IsInitializing { get; private set; }
        public bool IsInitialized { get; private set; }
        public AudioVolumeMixer Mixer { get; } = new();

        public float MasterVolume
        {
            get => Mixer.MasterVolume;
            set => Mixer.MasterVolume = value;
        }

        public float SfxVolume
        {
            get => Mixer.SfxVolume;
            set => Mixer.SfxVolume = value;
        }

        public float BgmVolume
        {
            get => Mixer.BgmVolume;
            set => Mixer.BgmVolume = value;
        }

        /// <summary>
        /// 대사 버스 볼륨. 이 매니저는 대사를 직접 재생하지 않는다. 자기 AudioSource를 들고
        /// <see cref="AudioVolumeMixer.Calculate"/>로 볼륨을 받아 가는 쪽이 소비한다.
        /// </summary>
        public float DialogueVolume
        {
            get => Mixer.DialogueVolume;
            set => Mixer.DialogueVolume = value;
        }

        /// <summary>
        /// 영상 버스 볼륨. 대사와 같은 구조로, 이 매니저는 영상을 재생하지 않는다.
        /// <c>CutsceneVideoSurface</c>가 자기 AudioSource를 들고 볼륨만 받아 간다.
        /// </summary>
        public float VideoVolume
        {
            get => Mixer.VideoVolume;
            set => Mixer.VideoVolume = value;
        }

        public bool MasterMuted
        {
            get => Mixer.MasterMuted;
            set => Mixer.MasterMuted = value;
        }

        public bool SfxMuted
        {
            get => Mixer.SfxMuted;
            set => Mixer.SfxMuted = value;
        }

        public bool BgmMuted
        {
            get => Mixer.BgmMuted;
            set => Mixer.BgmMuted = value;
        }

        public bool DialogueMuted
        {
            get => Mixer.DialogueMuted;
            set => Mixer.DialogueMuted = value;
        }

        protected override void OnRegistered()
        {
            poolParent = transform;
            Mixer.Changed += RefreshVolumes;
        }

        protected override void OnUnregistering()
        {
            Mixer.Changed -= RefreshVolumes;
            StopAllSfx();
            StopAllBgm();
            provider?.Release();
            provider = null;
            clips.Clear();
            IsInitialized = false;
            IsInitializing = false;
        }

        private void Update()
        {
            if (!IsInitialized || activeChannels.Count == 0)
                return;

            removeBuffer.Clear();
            replayBuffer.Clear();

            foreach (var pair in activeChannels)
            {
                var state = pair.Value;
                if (state.Source == null)
                {
                    removeBuffer.Add(pair.Key);
                    continue;
                }

                if (state.FollowTarget != null)
                    state.Source.transform.position = state.FollowTarget.position;
                else if (state.StopWithTarget)
                {
                    removeBuffer.Add(pair.Key);
                    continue;
                }

                if (state.Source.isPlaying)
                    continue;

                if (state.Remaining > 1)
                    replayBuffer.Add(pair.Key);
                else
                    removeBuffer.Add(pair.Key);
            }

            for (var i = 0; i < replayBuffer.Count; i++)
            {
                var id = replayBuffer[i];
                if (!activeChannels.TryGetValue(id, out var state) || state.Source == null)
                    continue;

                state.Remaining--;
                activeChannels[id] = state;
                state.Source.Play();
            }

            for (var i = 0; i < removeBuffer.Count; i++)
                ReturnChannel(removeBuffer[i]);
        }

        public async Awaitable<bool> InitializeAsync(
            IAudioClipProvider clipProvider,
            IReadOnlyList<AudioEntryData> entries,
            AudioRuntimeSettings settings = null)
        {
            if (IsInitialized)
                return true;

            if (IsInitializing)
            {
                Debug.LogError("[AudioManager] Initialization is already in progress.");
                return false;
            }

            if (clipProvider == null)
            {
                Debug.LogError("[AudioManager] Audio clip provider is null.");
                return false;
            }

            IsInitializing = true;
            provider = clipProvider;

            try
            {
                var loadedClips = await provider.LoadAsync(entries ?? Array.Empty<AudioEntryData>());
                if (loadedClips == null)
                    throw new InvalidOperationException("Audio clip provider returned null.");

                clips.Clear();
                foreach (var pair in loadedClips)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                        continue;

                    if (!clips.TryAdd(pair.Key, pair.Value))
                        throw new InvalidOperationException($"Duplicate audio ID: '{pair.Key}'.");
                }

                ApplySettings(settings ?? new AudioRuntimeSettings());
                IsInitialized = true;
                initializationWarningReported = false;
                missingIdWarnings.Clear();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                provider.Release();
                provider = null;
                clips.Clear();
                return false;
            }
            finally
            {
                IsInitializing = false;
            }
        }

        public AudioHandle PlayGlobal(
            string id,
            float volume = 1f,
            int repeatCount = 1,
            bool sceneBound = true)
        {
            return PlaySfx(
                id,
                Vector3.zero,
                null,
                false,
                0f,
                volume,
                repeatCount,
                false,
                sceneBound);
        }

        public AudioHandle PlayAt(
            string id,
            Vector3 position,
            float volume = 1f,
            int repeatCount = 1,
            bool sceneBound = true)
        {
            return PlaySfx(
                id,
                position,
                null,
                false,
                spatialBlend,
                volume,
                repeatCount,
                false,
                sceneBound);
        }

        public AudioHandle PlayAttached(
            string id,
            Transform target,
            float volume = 1f,
            int repeatCount = 1,
            bool sceneBound = true)
        {
            if (target == null)
            {
                Debug.LogWarning($"[AudioManager] Attached target is null for '{id}'.");
                return AudioHandle.Invalid;
            }

            return PlaySfx(
                id,
                target.position,
                target,
                true,
                spatialBlend,
                volume,
                repeatCount,
                false,
                sceneBound);
        }

        public AudioHandle PlayLoopAt(
            string id,
            Vector3 position,
            float volume = 1f,
            bool sceneBound = true)
        {
            return PlaySfx(
                id,
                position,
                null,
                false,
                spatialBlend,
                volume,
                1,
                true,
                sceneBound);
        }

        public AudioHandle PlayLoopGlobal(
            string id,
            float volume = 1f,
            bool sceneBound = true)
        {
            return PlaySfx(
                id,
                Vector3.zero,
                null,
                false,
                0f,
                volume,
                1,
                true,
                sceneBound);
        }

        public AudioHandle PlayLoopAttached(
            string id,
            Transform target,
            float volume = 1f,
            bool sceneBound = true)
        {
            if (target == null)
            {
                Debug.LogWarning($"[AudioManager] Attached target is null for '{id}'.");
                return AudioHandle.Invalid;
            }

            return PlaySfx(
                id,
                target.position,
                target,
                true,
                spatialBlend,
                volume,
                1,
                true,
                sceneBound);
        }

        public void Stop(AudioHandle handle)
        {
            if (handle.IsValid)
                ReturnChannel(handle.Id);
        }

        public bool IsPlaying(AudioHandle handle)
        {
            return handle.IsValid &&
                   activeChannels.TryGetValue(handle.Id, out var state) &&
                   state.Source != null &&
                   state.Source.isPlaying;
        }

        public void SetVolume(AudioHandle handle, float volume)
        {
            if (!handle.IsValid ||
                !activeChannels.TryGetValue(handle.Id, out var state) ||
                state.Source == null)
                return;

            state.BaseVolume = Mathf.Clamp01(volume);
            state.Source.volume = Mixer.Calculate(AudioBus.Sfx, state.BaseVolume);
            activeChannels[handle.Id] = state;
        }

        public void PlayBgm(string id, float volume = 1f, bool loop = true)
        {
            if (!TryGetClip(id, out var clip))
                return;

            if (!bgmLayers.TryGetValue(id, out var state) || state.Source == null)
            {
                state = new BgmState
                {
                    Source = CreateBgmSource(id),
                    BaseVolume = Mathf.Clamp01(volume)
                };
            }

            state.BaseVolume = Mathf.Clamp01(volume);
            state.Source.volume = Mixer.Calculate(AudioBus.Bgm, state.BaseVolume);
            state.Source.loop = loop;

            if (state.Source.clip != clip || !state.Source.isPlaying)
            {
                state.Source.clip = clip;
                state.Source.Play();
            }

            bgmLayers[id] = state;
        }

        public void StopBgm(string id)
        {
            if (bgmLayers.TryGetValue(id, out var state) && state.Source != null)
                state.Source.Stop();
        }

        public void StopAllBgm()
        {
            foreach (var state in bgmLayers.Values)
                if (state.Source != null)
                    state.Source.Stop();
        }

        public void StopSceneSounds()
        {
            removeBuffer.Clear();
            foreach (var pair in activeChannels)
                if (pair.Value.SceneBound)
                    removeBuffer.Add(pair.Key);

            for (var i = 0; i < removeBuffer.Count; i++)
                ReturnChannel(removeBuffer[i]);
        }

        public void StopAllSfx()
        {
            removeBuffer.Clear();
            foreach (var id in activeChannels.Keys)
                removeBuffer.Add(id);

            for (var i = 0; i < removeBuffer.Count; i++)
                ReturnChannel(removeBuffer[i]);
        }

        private AudioHandle PlaySfx(
            string id,
            Vector3 position,
            Transform followTarget,
            bool stopWithTarget,
            float sourceSpatialBlend,
            float volume,
            int repeatCount,
            bool loop,
            bool sceneBound)
        {
            if (!TryGetClip(id, out var clip))
                return AudioHandle.Invalid;

            var source = GetChannel();
            if (source == null)
                return AudioHandle.Invalid;

            source.transform.SetParent(poolParent, false);
            source.transform.position = position;
            source.clip = clip;
            source.loop = loop;
            source.spatialBlend = Mathf.Clamp01(sourceSpatialBlend);

            // 구 IUM AudioManager에서 이식. 2D 재생에 스페이셜라이저를 물리면 플러그인만 돌고
            // 결과가 없으므로 blend가 실제로 3D일 때만 켠다.
            source.spatialize = enableSpatialization && source.spatialBlend > 0.001f;

            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;

            var baseVolume = Mathf.Clamp01(volume);
            source.volume = Mixer.Calculate(AudioBus.Sfx, baseVolume);
            source.Play();

            var handle = new AudioHandle(NextId());
            activeChannels[handle.Id] = new ChannelState
            {
                Source = source,
                FollowTarget = followTarget,
                StopWithTarget = stopWithTarget,
                Loop = loop,
                SceneBound = sceneBound,
                Remaining = loop ? int.MaxValue : Mathf.Max(1, repeatCount),
                BaseVolume = baseVolume,
                Sequence = nextSequence++
            };
            return handle;
        }

        private bool TryGetClip(string id, out AudioClip clip)
        {
            if (!IsInitialized)
            {
                if (!initializationWarningReported)
                {
                    initializationWarningReported = true;
                    Debug.LogWarning("[AudioManager] AudioManager is not initialized.");
                }

                clip = null;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(id) && clips.TryGetValue(id, out clip))
                return true;

            if (missingIdWarnings.Add(id ?? string.Empty))
                Debug.LogWarning($"[AudioManager] Unknown audio ID: '{id}'.");

            clip = null;
            return false;
        }

        private void ApplySettings(AudioRuntimeSettings settings)
        {
            maxSfxChannels = Mathf.Max(1, settings.maxSfxChannels);
            var initialPoolSize = Mathf.Clamp(
                settings.initialPoolSize,
                1,
                maxSfxChannels);
            spatialBlend = Mathf.Clamp01(settings.spatialBlend);
            minDistance = Mathf.Max(0.01f, settings.minDistance);
            maxDistance = Mathf.Max(minDistance, settings.maxDistance);
            enableSpatialization = settings.enableSpatialization;
            Mixer.Apply(settings);

            while (channelCount < initialPoolSize)
                availableChannels.Enqueue(CreateChannel());
        }

        private AudioSource GetChannel()
        {
            while (availableChannels.Count > 0 && availableChannels.Peek() == null)
            {
                availableChannels.Dequeue();
                channelCount = Mathf.Max(0, channelCount - 1);
            }

            if (availableChannels.Count == 0 && channelCount < maxSfxChannels)
                availableChannels.Enqueue(CreateChannel());

            if (availableChannels.Count == 0)
            {
                var oldestId = 0;
                var oldestSequence = long.MaxValue;

                foreach (var pair in activeChannels)
                {
                    var state = pair.Value;
                    if (state.Loop || state.Source == null || state.Sequence >= oldestSequence)
                        continue;

                    oldestId = pair.Key;
                    oldestSequence = state.Sequence;
                }

                if (oldestId > 0)
                    ReturnChannel(oldestId);
            }

            if (availableChannels.Count == 0)
            {
                if (!voiceLimitWarningReported)
                {
                    voiceLimitWarningReported = true;
                    Debug.LogWarning("[AudioManager] SFX channel limit reached.");
                }

                return null;
            }

            var source = availableChannels.Dequeue();
            source.gameObject.SetActive(true);
            return source;
        }

        private AudioSource CreateChannel()
        {
            var channel = new GameObject($"SFXChannel_{channelCount:00}");
            channel.transform.SetParent(poolParent, false);
            var source = channel.AddComponent<AudioSource>();
            source.playOnAwake = false;
            channel.SetActive(false);
            channelCount++;
            return source;
        }

        private AudioSource CreateBgmSource(string id)
        {
            var layer = new GameObject($"BGM_{id}");
            layer.transform.SetParent(poolParent, false);
            var source = layer.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            return source;
        }

        private void ReturnChannel(int id)
        {
            if (!activeChannels.TryGetValue(id, out var state))
                return;

            activeChannels.Remove(id);

            var source = state.Source;
            if (source == null)
            {
                channelCount = Mathf.Max(0, channelCount - 1);
                return;
            }

            source.Stop();
            source.clip = null;
            source.loop = false;
            source.spatialBlend = 0f;
            source.spatialize = false;
            source.transform.SetParent(poolParent, false);
            source.gameObject.SetActive(false);
            availableChannels.Enqueue(source);
            voiceLimitWarningReported = false;
        }

        private int NextId()
        {
            if (nextId <= 0 || nextId == int.MaxValue)
                nextId = 1;

            while (activeChannels.ContainsKey(nextId))
            {
                nextId++;
                if (nextId == int.MaxValue)
                    nextId = 1;
            }

            return nextId++;
        }

        private void RefreshVolumes()
        {
            foreach (var pair in activeChannels)
            {
                var state = pair.Value;
                if (state.Source != null)
                    state.Source.volume = Mixer.Calculate(AudioBus.Sfx, state.BaseVolume);
            }

            foreach (var state in bgmLayers.Values)
                if (state.Source != null)
                    state.Source.volume = Mixer.Calculate(AudioBus.Bgm, state.BaseVolume);
        }
    }
}
