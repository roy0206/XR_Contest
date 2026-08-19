using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공정 단계가 가리킬 수 있도록 씬 오브젝트에 이름표를 붙인다. process.json은 씬 오브젝트를
/// 참조할 수 없으므로, 데이터는 문자열 키만 적고 실제 대상은 여기서 찾는다.
///
/// 컷씬이 연출을 씬에 둔 것과 같은 판단이다 (<see cref="CutsceneDefinition"/> 주석 참고). 다만
/// 공정은 단계의 순서와 수치가 반복적이라 그쪽은 데이터로 두고, 오브젝트 참조만 씬에 남겼다.
///
/// 등록은 <see cref="OnEnable"/>에서 한다. 씬이 내려가면 자동으로 빠지므로 러너가 죽은 참조를
/// 들고 있을 일이 없다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProcessTarget : MonoBehaviour
{
    static readonly Dictionary<string, ProcessTarget> Registry = new();

    [Tooltip("process.json의 target·unlock이 가리키는 이름. 씬 안에서 겹치지 않아야 한다.")]
    [SerializeField] string key;

    public string Key => key;

    /// <summary>같은 오브젝트의 잡을 수 있는 부분. 없으면 null이고 Grab·Point 조건에 쓸 수 없다.</summary>
    public Grabbable TargetGrabbable { get; private set; }

    /// <summary>같은 오브젝트의 소켓. 없으면 null이고 Place 조건에 쓸 수 없다.</summary>
    public GrabSocket TargetSocket { get; private set; }

    public static IReadOnlyCollection<ProcessTarget> All => Registry.Values;

    void Awake()
    {
        TargetGrabbable = GetComponent<Grabbable>();
        TargetSocket = GetComponent<GrabSocket>();
    }

    void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogWarning($"[Process] '{name}'의 ProcessTarget에 키가 없어 등록하지 않습니다.", this);
            return;
        }

        if (Registry.TryGetValue(key, out var existing) && existing != this && existing != null)
        {
            Debug.LogError(
                $"[Process] 키 '{key}'가 '{existing.name}'과 '{name}'에 중복됩니다. 뒤엣것은 등록하지 않습니다.",
                this);
            return;
        }

        Registry[key] = this;
    }

    void OnDisable()
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (Registry.TryGetValue(key, out var registered) && registered == this) Registry.Remove(key);
    }

    public static bool TryGet(string targetKey, out ProcessTarget target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(targetKey)) return false;
        return Registry.TryGetValue(targetKey, out target) && target != null;
    }

    /// <summary>
    /// 공정 잠금 (F-014 5.3). 잠근 대상은 잡을 수 없고, <see cref="Grabbable.GrabEnabled"/>가
    /// 쥔 상태에서 꺼지면 즉시 손에서 떨어진다.
    ///
    /// 강조는 건드리지 않는다. <see cref="GrabHandModule"/>이 hover에 따라 직접 켜고 끄므로
    /// 여기서 억지로 켜면 서로 덮어쓴다. 잡을 수 없는 도구는 hover 후보에서 빠지는 것으로
    /// "현재 도구만 강조"가 사실상 성립한다.
    /// </summary>
    public void SetAvailable(bool value)
    {
        if (TargetGrabbable == null) return;

        // 같은 값이면 건드리지 않는다. 끄는 쪽은 쥔 손을 놓게 하므로, 이미 꺼져 있는데 또 끄는
        // 호출이 섞이면 의도치 않게 물건이 떨어질 여지가 생긴다.
        if (TargetGrabbable.GrabEnabled == value) return;

        TargetGrabbable.GrabEnabled = value;
    }

    public static void SetAllAvailable(bool value)
    {
        foreach (var target in Registry.Values)
        {
            if (target == null) continue;
            target.SetAvailable(value);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        // 도메인 리로드를 끈 채 플레이에 들어가면 이전 세션의 등록이 그대로 남는다.
        Registry.Clear();
    }
}
