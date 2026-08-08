using UnityEngine;

/// <summary>
/// Keeps F-005 1.8 true: a dropped or thrown object must never end up somewhere the player
/// cannot reach. Checked once a second, which is frequent enough for a fallen tool.
/// </summary>
public sealed class GrabReturnModule : Module
{
    readonly Grabbable _grabbable;

    public GrabReturnModule(Grabbable grabbable) : base(grabbable) => _grabbable = grabbable;

    /// <summary>Drop below this height, relative to the home pose, and the object is recovered.</summary>
    public float FallDepth { get; set; } = 3f;

    public float MaxDistance { get; set; } = 25f;

    public override void OnSecond()
    {
        if (_grabbable == null || _grabbable.IsHeld) return;

        var position = _grabbable.transform.position;
        var home = _grabbable.HomePose.position;

        if (position.y < home.y - FallDepth || Vector3.Distance(position, home) > MaxDistance)
            _grabbable.ReturnHome();
    }
}
