using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인게임 대사. Plays 노장 lines while the player keeps control of movement and hands — only PTT
/// and, for instructional sequences, 공정 판정 are held (F-011 1.4).
///
/// Voices play 2D: 노장 has no model in the scene and is represented by screen-fixed UI, so no
/// speaker owns a world position.
///
/// Cutscene dialogue is deliberately not routed through here. A cutscene owns the whole frame and
/// its lines are linear, whereas these arrive unpredictably from process events and need a queue.
/// Both share <see cref="DialoguePlayer"/> underneath.
/// </summary>
public sealed class InGameDialogue : Singleton<InGameDialogue>, ISceneEventListener
{
    /// <summary>Static data key registered in manifest.json.</summary>
    public const string DataKey = "dialogue";

    /// <summary>
    /// Supplies the TTS service used when a line has no recorded clip. Assign before the first
    /// access; null keeps dialogue on recorded audio only, which is the offline-safe default
    /// and the correct behaviour if 사전 녹음 is chosen (07 문서 미정 16번).
    /// </summary>
    public static Func<IAiTextToSpeechService> TextToSpeechResolver { get; set; }

    readonly List<DialogueSequence> _queue = new();

    DialogueTable _table;
    DialoguePlayer _player;
    DialogueVoiceLibrary _voices;
    Task _initialization;
    IDisposable _processHold;
    IDisposable _pushToTalkLock;

    /// <summary>Suppresses queue handling while a sequence is being swapped out for another.</summary>
    bool _swapping;

    public bool IsReady { get; private set; }
    public bool IsSpeaking => _player != null && _player.IsPlaying;
    public DialogueSequence Current => _player?.CurrentSequence;
    public int QueuedCount => _queue.Count;

    /// <summary>Hook for lipsync and speaking animation.</summary>
    public event Action<DialogueLine> LineStarted;

    public event Action<DialogueLine> LineFinished;

    /// <summary>Empty text means "hide the subtitle". 대사 볼륨이 0이어도 자막은 표시한다 (F-018 1.4).</summary>
    public event Action<DialogueSpeaker, string> SubtitleChanged;

    /// <summary>Second argument is false when the sequence was cut.</summary>
    public event Action<DialogueSequence, bool> SequenceFinished;

    public event Action Ready;

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;

        SceneController.Instance?.RegisterListener(this);
        _ = InitializeAsync();
    }

    public Task InitializeAsync() => _initialization ??= InitializeInternalAsync();

    async Task InitializeInternalAsync()
    {
        _table = await LoadTableAsync();
        _table.Prepare();

        _voices = new DialogueVoiceLibrary(TextToSpeechResolver?.Invoke());
        _player = new DialoguePlayer(transform, _table.Settings, _voices);
        _player.LineStarted += OnLineStarted;
        _player.LineFinished += OnLineFinished;
        _player.SubtitleChanged += OnSubtitleChanged;
        _player.SequenceFinished += OnSequenceFinished;

        IsReady = true;
        Ready?.Invoke();
    }

    /// <summary>
    /// A missing table is not fatal. The entry is optional in the manifest so the system runs
    /// before the Addressables asset exists, and a broken file degrades the same way.
    /// </summary>
    async Task<DialogueTable> LoadTableAsync()
    {
        try
        {
            await DataManager.Instance.InitializeAsync();
            if (DataManager.Instance.Static != null &&
                DataManager.Instance.Static.TryGet<DialogueTable>(DataKey, out var table) &&
                table != null)
                return table;

            Debug.LogWarning($"[Dialogue] 정적 데이터 '{DataKey}'가 없어 빈 대사표로 시작합니다.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Dialogue] 대사 데이터를 불러오지 못했습니다: {exception.Message}");
        }

        return DialogueTable.CreateEmpty();
    }

    // The player already runs on PauseService.Now, so a paused game would not advance anyway; this
    // makes the intent explicit and skips the work.
    void Update()
    {
        if (PauseService.IsPaused) return;
        _player?.Tick();
    }

    /// <summary>Plays a sequence by id. Returns false when the id is unknown or data is not ready.</summary>
    public bool Play(string sequenceId)
    {
        if (!IsReady)
        {
            Debug.LogWarning($"[Dialogue] 초기화 전에 '{sequenceId}' 재생을 요청했습니다.");
            return false;
        }

        if (!_table.TryGet(sequenceId, out var sequence))
        {
            Debug.LogWarning($"[Dialogue] 대사 '{sequenceId}'를 찾을 수 없습니다.");
            return false;
        }

        PlaySequence(sequence);
        return true;
    }

    /// <summary>
    /// Plays or queues a sequence. A higher priority cuts an interruptible sequence; anything
    /// else waits in priority order behind what is already queued.
    /// </summary>
    public void PlaySequence(DialogueSequence sequence)
    {
        if (!IsReady || sequence == null || !sequence.HasLines) return;

        if (!IsSpeaking)
        {
            StartSequence(sequence);
            return;
        }

        var current = Current;
        if (sequence.Priority > current.Priority && current.Interruptible)
        {
            // The cut sequence is dropped rather than requeued: replaying an instruction the
            // player has already moved past reads as a bug, not as recovery.
            StartSequence(sequence);
            return;
        }

        Enqueue(sequence);
    }

    /// <summary>Stops the current sequence. Used by 공정 다시 시작 and scene teardown.</summary>
    public void Stop(bool clearQueue = true)
    {
        if (clearQueue) _queue.Clear();
        _player?.Stop();
    }

    void Enqueue(DialogueSequence sequence)
    {
        // Higher priority first, FIFO within the same priority.
        var index = _queue.Count;
        for (var i = 0; i < _queue.Count; i++)
        {
            if (_queue[i].Priority >= sequence.Priority) continue;
            index = i;
            break;
        }

        _queue.Insert(index, sequence);
    }

    void StartSequence(DialogueSequence sequence)
    {
        _swapping = true;
        _player.Stop();
        _swapping = false;

        ReleaseHolds();
        AcquireHolds(sequence);

        _swapping = true;
        _player.Play(sequence);
        _swapping = false;
    }

    void AcquireHolds(DialogueSequence sequence)
    {
        // 노장이 말하는 동안 이음이에게 질문할 수 없다 (F-011 1.4). Held for every sequence.
        _pushToTalkLock = InputLockService.Acquire(InputLockFlags.PushToTalk, $"대사:{sequence.Id}");

        if (sequence.BlocksProcess)
            _processHold = ProcessGate.Hold($"대사:{sequence.Id}");
    }

    void ReleaseHolds()
    {
        _pushToTalkLock?.Dispose();
        _pushToTalkLock = null;
        _processHold?.Dispose();
        _processHold = null;
    }

    void OnLineStarted(DialogueLine line) => LineStarted?.Invoke(line);
    void OnLineFinished(DialogueLine line) => LineFinished?.Invoke(line);
    void OnSubtitleChanged(DialogueSpeaker speaker, string text) => SubtitleChanged?.Invoke(speaker, text);

    void OnSequenceFinished(DialogueSequence sequence, bool completed)
    {
        // A swap already released and re-acquired the holds; running the queue here would
        // start a second sequence on top of the one being installed.
        if (_swapping) return;

        ReleaseHolds();
        SequenceFinished?.Invoke(sequence, completed);

        if (_queue.Count == 0) return;
        var next = _queue[0];
        _queue.RemoveAt(0);
        StartSequence(next);
    }

    public void OnSceneLoadStart(string sceneName)
    {
        Stop();
        ReleaseHolds();
        _voices?.ReleaseAll();
    }

    public void OnSceneLoadComplete(string sceneName) { }

    protected override void OnDestroy()
    {
        if (SceneController.TryGetInstance(out var controller))
            controller.UnregisterListener(this);

        ReleaseHolds();
        _player?.Dispose();
        _voices?.ReleaseAll();
        base.OnDestroy();
    }
}
