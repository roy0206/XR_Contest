using UnityEngine;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// <see cref="DialogueAudioModule"/>의 호스트.
///
/// 모듈을 <c>Core.Audio.AudioManager</c>에 직접 붙일 수 없어 별도 호스트를 둔다. 그쪽 매니저는
/// <c>Core.Foundation.Singleton&lt;AudioManager&gt;</c>를 상속하고 <see cref="MonoThing"/>도
/// <see cref="MonoBehaviour"/>라, C# 단일 상속에서는 둘을 겸할 수 없다. 대신 모듈이 매니저를
/// 생성자로 받는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DialogueAudioHost : MonoThing
{
    [SerializeField] CoreAudioManager audioManager;

    public DialogueAudioModule Voice { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        if (audioManager == null) audioManager = FindAnyObjectByType<CoreAudioManager>();
        if (audioManager == null)
            Debug.LogWarning(
                "[DialogueAudioHost] Core.Audio.AudioManager가 씬에 없습니다. " +
                "대사 볼륨이 버스를 거치지 않고 그대로 재생됩니다.", this);

        Voice = new DialogueAudioModule(this, audioManager);
        AddModule(Voice);

        // Module.TickUpdate는 IsInitialized일 때만 OnUpdate를 부른다. Init을 빠뜨리면
        // 재생 종료를 영영 감지하지 못한다.
        Voice.Init();
    }
}
