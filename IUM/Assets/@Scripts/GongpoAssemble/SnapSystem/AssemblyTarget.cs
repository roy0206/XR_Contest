using UnityEngine;

/// <summary>
/// One seat of the 공포 stack. It accepts a part whose <see cref="MaleSnapPoint"/> ID matches
/// <see cref="AcceptedPartID"/>, and only while the part this seat sits on is itself assembled,
/// which is what keeps the stack strictly bottom-up.
/// Trigger messages stay on the component because they are Unity callbacks; the judgement lives in
/// <see cref="AssemblySnapModule"/>, the same split as GrabSocket/SocketPlacementModule.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AssemblyTarget : MonoThing
{
    [Header("받을 부품의 ID")]
    [Tooltip("MaleSnapPoint의 mySnapID와 스펠링이 일치해야 함")]
    [SerializeField] string acceptedPartID = "Untagged";

    [Header("자식에게 물려줄 자리 ID")]
    [Tooltip("이곳에 부품이 조립되면, 아래로 연결될 부품들에게 이 ID를 물려줌")]
    [SerializeField] string giveIDToChild = "";

    [Header("판정 오차 범위")]
    [SerializeField, Min(0f)] float positionTolerance = 1f;
    [SerializeField, Min(0f)] float rotationTolerance = 20f;

    [Header("상태별 머티리얼")]
    [SerializeField] Material correctMaterial;
    [SerializeField] Material wrongMaterial;

    [Header("햅틱")]
    [Tooltip("컨트롤러가 없는 데스크톱 조작에서는 그대로 무시된다.")]
    [SerializeField, Range(0f, 1f)] float hapticIntensity = 0.5f;
    [SerializeField, Min(0f)] float hapticDuration = 0.1f;

    public AssemblySnapModule Snap { get; private set; }

    /// <summary>The part this seat belongs to. Null on a seat placed directly in the scene root.</summary>
    public AssemblyPart OwnerPart { get; private set; }

    public string AcceptedPartID => acceptedPartID;
    public string GiveIDToChild => giveIDToChild;
    public float PositionTolerance => positionTolerance;
    public float RotationTolerance => rotationTolerance;
    public Material CorrectMaterial => correctMaterial;
    public Material WrongMaterial => wrongMaterial;
    public float HapticIntensity => hapticIntensity;
    public float HapticDuration => hapticDuration;

    /// <summary>
    /// Called by <see cref="AssemblyPart.OnAssembled"/> so a seat can take over the ID handed down
    /// by the part that was just seated above it.
    /// </summary>
    public void SetAcceptedPartID(string id)
    {
        if (!string.IsNullOrEmpty(id)) acceptedPartID = id;
    }

    protected override void Awake()
    {
        base.Awake();

        if (collider3D != null) collider3D.isTrigger = true;

        OwnerPart = GetComponentInParent<AssemblyPart>();
        if (OwnerPart == null)
            Debug.LogWarning($"{gameObject.name}의 부모 객체에 AssemblyPart 스크립트가 없습니다!", this);

        Snap = new AssemblySnapModule(this);
        AddModule(Snap);
        Snap.Init();
    }

    void OnTriggerEnter(Collider other) => Snap?.SetCandidate(other.GetComponent<MaleSnapPoint>());

    void OnTriggerExit(Collider other) => Snap?.ClearCandidate(other.GetComponent<MaleSnapPoint>());
}
