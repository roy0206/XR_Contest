using UnityEngine;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// <c>Core.Audio.AudioManager</c>와 <see cref="CoreAudioBridge"/>를 한 번 만들어 게임 내내 유지한다.
///
/// 이것이 없으면 볼륨 버스가 <c>CoreAudioTest</c> 씬에만 존재한다. 대사·영상 음량이 버스를
/// 지나게 된 뒤로는(ISSUE-015) 버스가 없는 씬에서 볼륨 설정이 통째로 무시되므로, 모든 씬에
/// 있어야 한다.
///
/// <c>Core.Foundation.Singleton</c>은 지연 생성을 하지 않는다 — 없으면 에러를 내고 null을 준다.
/// 그것은 그 모듈의 계약이고, 만들어 주는 책임은 통합 계층에 있다. 모듈 자체는 손대지 않는다.
///
/// 씬마다 배치하지 않고 진입점에서 거는 이유는 대상 씬이 너무 많기 때문이다. 메인 화면, 흐름,
/// 공정, 컷씬 두 편, 개발 씬 전부가 대사나 영상을 재생할 수 있다.
/// </summary>
public static class CoreAudioBootstrap
{
    /// <summary>씬에 이미 배치된 것이 있으면 그쪽을 쓴다. <c>CoreAudioTest</c>가 그런 경우다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (CoreAudioManager.HasInstance) return;

        var host = new GameObject(nameof(CoreAudioBootstrap));
        Object.DontDestroyOnLoad(host);

        // 매니저를 먼저 붙인다. 브릿지가 Awake에서 이걸 찾는다.
        host.AddComponent<CoreAudioManager>();
        host.AddComponent<CoreAudioBridge>();
    }
}
