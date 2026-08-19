using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// mp4 컷씬의 재생 표면. <see cref="CutsceneDirector"/>가 필요할 때 한 번 만들어 계속 재사용한다.
///
/// 인게임 컷씬은 씬을 겹쳐 올려 <see cref="CutsceneStage"/>가 연출하지만, 영상 컷씬에는 연출할 씬
/// 오브젝트가 없다. 영상 한 편마다 빈 씬과 Build Settings 항목을 만드는 대신, cutscene.json의
/// video 한 줄로 끝나도록 재생 수단을 하나 더 연 것이 이 클래스다.
///
/// Screen Space Overlay를 쓰지 않는 이유는 <see cref="ScreenFader"/>와 같다 — XR 렌더 타깃으로
/// 신뢰할 수 없다. 정렬 순서는 페이더보다 낮게 두어, 진입·종료 암전과 건너뛰기 페이드가 영상을
/// 정상적으로 덮는다. UIToolkit으로 그리는 캡션과 건너뛰기 게이지는 두 캔버스보다 위에 남는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CutsceneVideoSurface : MonoBehaviour
{
    /// <summary>ScreenFader(5000)보다 아래. 암전이 영상을 덮어야 한다.</summary>
    const int SortingOrder = 4000;

    const float CameraPlaneOffset = 0.06f;

    Canvas _canvas;
    RawImage _image;
    AspectRatioFitter _fitter;
    VideoPlayer _player;
    AudioSource _audio;

    TaskCompletionSource<bool> _preparation;

    /// <summary>True from <see cref="Play"/> until <see cref="Stop"/>. 종료 판정 주체를 가른다.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// 재생할 영상이 열려 있는지. 표면은 컷씬 사이에 살아남으므로, 영상 없는 컷씬이 뒤따를 때
    /// 호출자가 이걸 보고 <see cref="Play"/>를 건너뛴다.
    /// </summary>
    public bool IsPrepared => _player != null && _player.isPrepared;

    /// <summary>
    /// 재생이 끝났는지. 오류로 끝난 경우에도 true다 — 재생하지 못한 영상 때문에 컷씬이 입력 잠금을
    /// 쥔 채 멈춰 있으면 게임을 진행할 수 없다.
    /// </summary>
    public bool IsFinished { get; private set; }

    void Awake()
    {
        BuildCanvas();
        BuildPlayer();
        SetVisible(false);
    }

    void OnEnable() => PauseService.Changed += OnPauseChanged;

    void OnDisable() => PauseService.Changed -= OnPauseChanged;

    /// <summary>
    /// 카메라 재바인딩. 컷씬이 카메라를 교체하거나 씬이 바뀌면 붙어 있던 카메라가 꺼지고, 그대로
    /// 두면 캔버스가 아무것도 그리지 않는다 (ISSUE-012와 같은 사유).
    /// </summary>
    void LateUpdate()
    {
        if (!IsActive) return;

        // APIOnly 모드의 출력 텍스처는 재생 중에 교체될 수 있다. 매 프레임 참조만 맞춰 둔다.
        if (_image != null && _player != null && _image.texture != _player.texture)
            _image.texture = _player.texture;

        // 변경된 소리를 즉시 미리 듣기 (F-002 2.3). 컷씬 중에도 일시정지와 옵션 진입은 허용되므로
        // 재생 시작 시점에 한 번 읽는 것으로는 재생 중의 슬라이더 조작이 반영되지 않는다. 볼륨이
        // 아직 버스를 타지 않아 통지받을 곳이 없으므로 여기서 다시 읽는다 (ISSUE-015).
        ApplyVolume();

        if (_canvas == null) return;
        if (_canvas.worldCamera != null && _canvas.worldCamera.isActiveAndEnabled) return;
        BindCamera();
    }

    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.sortingOrder = SortingOrder;

        // 영상 비율과 화면 비율이 다를 때 남는 여백. 없으면 그 자리에 게임 화면이 그대로 비친다.
        var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        background.transform.SetParent(transform, false);
        var backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = Color.black;
        backgroundImage.raycastTarget = false;
        Stretch(backgroundImage.rectTransform);

        var frame = new GameObject("Video", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        frame.transform.SetParent(transform, false);
        _image = frame.GetComponent<RawImage>();
        _image.raycastTarget = false;
        Stretch(_image.rectTransform);

        // 레터박스. 영상 해상도를 알게 되는 준비 완료 시점에 비율을 넣는다.
        _fitter = frame.AddComponent<AspectRatioFitter>();
        _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _fitter.aspectRatio = 16f / 9f;

        BindCamera();
    }

    void BuildPlayer()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;

        // ignoreListenerPause는 기본값 그대로 둔다. PauseService가 AudioListener.pause를 켜므로
        // 일시정지하면 소리도 함께 멎는다.
        _audio.spatialBlend = 0f;

        _player = gameObject.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.isLooping = false;
        _player.waitForFirstFrame = true;
        _player.source = VideoSource.Url;

        // RenderTexture를 직접 잡지 않는다. APIOnly는 VideoPlayer가 자기 출력 텍스처를 들고 있어
        // 해상도별 할당과 해제를 관리할 필요가 없다.
        _player.renderMode = VideoRenderMode.APIOnly;
        _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _player.skipOnDrop = true;

        _player.loopPointReached += OnLoopPointReached;
        _player.errorReceived += OnErrorReceived;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void BindCamera()
    {
        if (_canvas == null) return;
        var camera = ResolveCamera();
        if (camera == null) return;

        _canvas.worldCamera = camera;

        // 페이더보다 카메라에서 살짝 멀리. 같은 평면에 두면 어느 쪽이 앞인지 정렬 순서만으로
        // 결정되지 않는다.
        _canvas.planeDistance = Mathf.Max(camera.nearClipPlane + CameraPlaneOffset, 0.1f);
    }

    /// <summary>
    /// <see cref="ScreenFader.ResolveCamera"/>와 같은 규칙. Camera.main은 MainCamera 태그가 붙은
    /// 활성 카메라만 돌려주므로, 컷씬이 주 카메라를 꺼둔 동안에는 null이 된다.
    /// </summary>
    static Camera ResolveCamera()
    {
        var main = Camera.main;
        if (main != null) return main;

        var cameras = Camera.allCameras;
        Camera last = null;
        for (var i = 0; i < cameras.Length; i++)
            if (last == null || cameras[i].depth > last.depth)
                last = cameras[i];

        return last;
    }

    /// <summary>
    /// 영상을 열고 첫 프레임까지 준비한다. 준비에 실패해도 예외를 던지지 않고 false를 돌려준다 —
    /// 호출자가 컷씬을 접을지 씬 연출만으로 이어갈지 정한다.
    /// </summary>
    public Task<bool> PrepareAsync(string source)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(source))
        {
            Debug.LogWarning("[Cutscene] 영상 경로가 비어 있습니다.");
            return Task.FromResult(false);
        }

        IsFinished = false;
        _preparation = new TaskCompletionSource<bool>();

        _player.url = ResolveUrl(source);

        // 준비 전에 걸어 둔다. 트랙 활성화는 Prepare 이후에는 반영되지 않고, 트랙 수는 준비가
        // 끝나야 알 수 있다. 첫 트랙만 미리 잡고 나머지는 준비 완료 시점에 맞춘다.
        _player.controlledAudioTrackCount = 1;
        _player.EnableAudioTrack(0, true);
        _player.SetTargetAudioSource(0, _audio);

        _player.prepareCompleted += OnPrepareCompleted;
        _player.Prepare();

        return _preparation.Task;
    }

    /// <summary>
    /// StreamingAssets 기준 상대 경로를 절대 경로로 바꾼다. 이미 스킴이 붙어 있으면 그대로 쓴다.
    ///
    /// Path.Combine을 쓰지 않는 이유: Android에서 streamingAssetsPath는 APK 안을 가리키는
    /// `jar:file://...!/assets` 형태이고, Windows 에디터의 Path.Combine은 역슬래시를 넣는다. URL로
    /// 넘길 문자열이므로 구분자를 직접 맞춘다.
    /// </summary>
    static string ResolveUrl(string source)
    {
        if (source.Contains("://")) return source;

        var relative = source.Replace('\\', '/').TrimStart('/');
        return $"{Application.streamingAssetsPath}/{relative}";
    }

    void OnPrepareCompleted(VideoPlayer player)
    {
        _player.prepareCompleted -= OnPrepareCompleted;

        var width = (int)_player.width;
        var height = (int)_player.height;
        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[Cutscene] 영상 해상도를 읽지 못했습니다: {_player.url}");
            CompletePreparation(false);
            return;
        }

        // 화면 비율과의 차이는 AspectRatioFitter가 여백으로 처리한다.
        _image.texture = _player.texture;
        _fitter.aspectRatio = (float)width / height;

        for (ushort track = 0; track < _player.controlledAudioTrackCount; track++)
            _player.SetTargetAudioSource(track, _audio);

        CompletePreparation(true);
    }

    void OnErrorReceived(VideoPlayer player, string message)
    {
        Debug.LogError($"[Cutscene] 영상 재생 오류: {message} ({_player.url})");

        // 준비 중이었다면 실패로 닫고, 재생 중이었다면 끝난 것으로 처리해 컷씬이 빠져나가게 한다.
        if (!CompletePreparation(false)) IsFinished = true;
    }

    /// <summary>준비 대기를 닫는다. 대기 중이 아니었으면 false.</summary>
    bool CompletePreparation(bool result)
    {
        var pending = _preparation;
        if (pending == null) return false;

        _preparation = null;
        pending.TrySetResult(result);
        return true;
    }

    /// <summary>준비된 영상을 재생하고 화면에 띄운다.</summary>
    public void Play()
    {
        if (_player == null || !_player.isPrepared)
        {
            Debug.LogWarning("[Cutscene] 준비되지 않은 영상을 재생하려 했습니다.");
            IsFinished = true;
            return;
        }

        IsActive = true;
        IsFinished = false;

        ApplyVolume();
        BindCamera();
        SetVisible(true);

        _player.Play();

        // 일시정지 중에 컷씬이 시작되는 경로는 없어야 하지만, 있더라도 첫 프레임만 세워 둔다.
        if (PauseService.IsPaused) _player.Pause();
    }

    /// <summary>
    /// 재생을 멈추고 화면에서 내린다. 여러 번 불러도 안전하다 — 종료·건너뛰기·중단 경로가 모두
    /// 이걸 지난다.
    /// </summary>
    public void Stop()
    {
        CompletePreparation(false);

        if (_player != null)
        {
            _player.prepareCompleted -= OnPrepareCompleted;
            _player.Stop();
            _player.url = string.Empty;
        }

        if (_image != null) _image.texture = null;

        IsActive = false;
        IsFinished = false;
        SetVisible(false);
    }

    /// <summary>
    /// 영상 볼륨을 적용한다 (F-002 2.2). mp4는 배경음악·내레이션·효과음이 하나로 믹스되어 있어
    /// 나머지 세 분류로 나눌 수 없다. 쪼갤 수 없으므로 분류를 하나 늘렸다 (ISSUE-020).
    ///
    /// VIDEO 버스를 지난다 (ISSUE-015). 마스터 볼륨과 뮤트가 함께 걸린다.
    /// </summary>
    public void ApplyVolume()
    {
        if (_audio == null) return;
        _audio.volume = AudioBusVolume.Resolve(Core.Audio.AudioBus.Video);
    }

    void SetVisible(bool value)
    {
        if (_canvas != null) _canvas.enabled = value;
    }

    /// <summary>
    /// 일시정지 대응. AudioListener.pause가 소리는 멈추지만 영상 프레임은 계속 넘어가므로,
    /// 재생 자체를 세우지 않으면 메뉴를 닫았을 때 그림과 소리가 어긋난다.
    /// </summary>
    void OnPauseChanged(bool paused)
    {
        if (!IsActive || _player == null) return;

        if (paused) _player.Pause();
        else if (!IsFinished) _player.Play();
    }

    void OnLoopPointReached(VideoPlayer player) => IsFinished = true;

    void OnDestroy()
    {
        if (_player != null)
        {
            _player.prepareCompleted -= OnPrepareCompleted;
            _player.loopPointReached -= OnLoopPointReached;
            _player.errorReceived -= OnErrorReceived;
        }

        CompletePreparation(false);
    }
}
