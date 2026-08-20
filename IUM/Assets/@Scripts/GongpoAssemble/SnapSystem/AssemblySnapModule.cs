using System;
using UnityEngine;

/// <summary>
/// Decides when a held part counts as installed in an <see cref="AssemblyTarget"/> and seats it.
/// The judgement is deterministic and driven by <see cref="Grabbable"/> only, so the same rules run
/// for the desktop hands and for XR controllers or hand tracking (F-014: 평가는 AI와 분리된 결정론적 규칙).
/// </summary>
public sealed class AssemblySnapModule : Module
{
    readonly AssemblyTarget _target;

    MaleSnapPoint _candidate;
    Grabbable _part;
    Renderer _partVisual;
    Material _normalMaterial;
    Material _appliedMaterial;

    public AssemblySnapModule(AssemblyTarget target) : base(target) => _target = target;

    /// <summary>Raised once the part is seated. Process evaluation and 공포 퍼즐 progress subscribe here.</summary>
    public event Action<Grabbable> Assembled;

    public bool IsOccupied { get; private set; }
    public Grabbable Occupant { get; private set; }

    /// <summary>A seat only opens after the part carrying it is itself assembled.</summary>
    public bool IsOpen =>
        !IsOccupied && _target.OwnerPart != null && _target.OwnerPart.isAssembled;

    /// <summary>Holds a single candidate at a time, so the first matching part in the volume wins.</summary>
    public void SetCandidate(MaleSnapPoint male)
    {
        if (male == null || _candidate != null || !IsOpen) return;
        if (male.mySnapID != _target.AcceptedPartID) return;

        var part = male.GetComponentInParent<Grabbable>();
        if (part == null)
        {
            Debug.LogWarning(
                $"{male.name}의 부모에 Grabbable 컴포넌트가 없어 조립 판정을 할 수 없습니다.", male);
            return;
        }

        _candidate = male;
        _part = part;
        _partVisual = part.Renderer3D != null ? part.Renderer3D : part.GetComponentInChildren<Renderer>();
        _normalMaterial = _partVisual != null ? _partVisual.sharedMaterial : null;
        _appliedMaterial = _normalMaterial;

        _part.Released += OnPartReleased;
        SendHaptic();
    }

    public void ClearCandidate(MaleSnapPoint male)
    {
        if (male == null || male != _candidate) return;
        ReleaseCandidate();
    }

    public override void OnUpdate()
    {
        if (_candidate == null) return;

        if (_part == null)
        {
            ReleaseCandidate();
            return;
        }

        // Live feedback only makes sense while the part is actually in a hand; the seat may also
        // have been closed again in the meantime by a recovery step.
        if (!IsOpen || !_part.IsHeld)
        {
            ApplyMaterial(_normalMaterial);
            return;
        }

        ApplyMaterial(IsWithinTolerance() ? _target.CorrectMaterial : _target.WrongMaterial);
    }

    public override void OnRemoved() => ReleaseCandidate();

    bool IsWithinTolerance()
    {
        var positionError = Vector3.Distance(_candidate.transform.position, _target.transform.position);
        var rotationError = Quaternion.Angle(_candidate.transform.rotation, _target.transform.rotation);

        return positionError <= _target.PositionTolerance &&
               rotationError <= _target.RotationTolerance;
    }

    void OnPartReleased(Grabbable released, GrabHandModule hand)
    {
        if (_candidate == null || released != _part || !IsOpen) return;
        if (!IsWithinTolerance()) return;

        Install();
    }

    void Install()
    {
        var part = _part;
        var male = _candidate.transform;
        var seat = _target.transform;
        var partRoot = part.transform;

        // Line the male snap point up with the seat instead of the part's own origin, so a member is
        // seated by its joint. Rotation first: the offset is measured again afterwards because the
        // snap point moves with the part.
        var rotationOffset = Quaternion.Inverse(partRoot.rotation) * male.rotation;
        partRoot.rotation = seat.rotation * Quaternion.Inverse(rotationOffset);
        partRoot.position = seat.position - (male.position - partRoot.position);

        // Freeze() handles both the rigidbody and grab locking, so the seated part cannot be
        // picked up again. The recovery module has to go as well: an installed part sitting far from
        // its original home pose would otherwise be teleported back out of the stack.
        part.Freeze(true);
        part.RemoveModule<GrabReturnModule>();

        ApplyMaterial(_normalMaterial);
        partRoot.SetParent(seat.parent != null ? seat.parent : seat, true);

        IsOccupied = true;
        Occupant = part;
        if (Thing.Collider3D != null) Thing.Collider3D.enabled = false;

        DetachCandidate();

        // Hands the seat's inherited ID down so the next part up the stack becomes installable.
        part.GetComponent<AssemblyPart>()?.OnAssembled(_target.GiveIDToChild);
        Assembled?.Invoke(part);
    }

    void ReleaseCandidate()
    {
        ApplyMaterial(_normalMaterial);
        DetachCandidate();
    }

    void DetachCandidate()
    {
        if (_part != null) _part.Released -= OnPartReleased;

        _candidate = null;
        _part = null;
        _partVisual = null;
        _normalMaterial = null;
        _appliedMaterial = null;
    }

    void ApplyMaterial(Material material)
    {
        if (_partVisual == null || material == null || ReferenceEquals(_appliedMaterial, material)) return;

        _partVisual.material = material;
        _appliedMaterial = material;
    }

    // No-op on desktop: UserInput reports no valid controller, so nothing is sent.
    void SendHaptic()
    {
        if (_part == null || !_part.IsHeld) return;

        UserInput.Instance.SendHapticImpulse(
            _part.Holder.Hand, _target.HapticIntensity, _target.HapticDuration);
    }
}
