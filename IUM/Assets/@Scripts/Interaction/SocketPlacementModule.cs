using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides when a grabbable inside a socket counts as placed and snaps it to the attach point.
/// Tutorial steps and process checks subscribe to <see cref="Placed"/> instead of polling.
/// </summary>
public sealed class SocketPlacementModule : Module
{
    readonly GrabSocket _socket;
    readonly List<Grabbable> _candidates = new();

    public SocketPlacementModule(GrabSocket socket) : base(socket) => _socket = socket;

    public event Action<Grabbable> Placed;
    public event Action<Grabbable> Removed;

    public Grabbable Occupant { get; private set; }
    public bool IsOccupied => Occupant != null;

    public void AddCandidate(Grabbable candidate)
    {
        if (candidate == null || !Accepts(candidate) || _candidates.Contains(candidate)) return;
        _candidates.Add(candidate);
    }

    public void RemoveCandidate(Grabbable candidate)
    {
        if (candidate == null) return;
        _candidates.Remove(candidate);

        if (Occupant == candidate) ClearOccupant();
    }

    public override void OnUpdate()
    {
        if (IsOccupied)
        {
            // A player can take an unlocked object back out of the socket.
            if (Occupant.IsHeld) ClearOccupant();
            return;
        }

        for (var i = _candidates.Count - 1; i >= 0; i--)
        {
            var candidate = _candidates[i];
            if (candidate == null)
            {
                _candidates.RemoveAt(i);
                continue;
            }

            if (candidate.IsHeld) continue;

            Place(candidate);
            return;
        }
    }

    public override void OnRemoved()
    {
        _candidates.Clear();
        Occupant = null;
    }

    bool Accepts(Grabbable candidate) =>
        _socket.RequiredTarget == null || _socket.RequiredTarget == candidate;

    void Place(Grabbable candidate)
    {
        Occupant = candidate;

        var attach = _socket.AttachPoint;
        candidate.transform.SetPositionAndRotation(attach.position, attach.rotation);
        candidate.Freeze(true);

        if (!_socket.LockOnPlace) candidate.GrabEnabled = true;

        Placed?.Invoke(candidate);
    }

    void ClearOccupant()
    {
        var previous = Occupant;
        Occupant = null;
        if (previous == null) return;

        previous.Freeze(false);
        Removed?.Invoke(previous);
    }
}
