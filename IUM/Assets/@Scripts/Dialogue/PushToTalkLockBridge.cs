using UnityEngine;

/// <summary>
/// Mirrors the PTT input lock into <see cref="AiConversationManager"/>. Kept as a bridge instead
/// of having the dialogue and cutscene systems call SetInputLocked directly: both can hold PTT at
/// once, and two direct callers would let whichever finished first unlock 이음이 while the other
/// is still speaking. <see cref="InputLockService"/> already reference counts, so the conversation
/// layer only has to follow the combined result.
/// </summary>
public static class PushToTalkLockBridge
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        InputLockService.Changed -= OnLocksChanged;
        InputLockService.Changed += OnLocksChanged;
    }

    static void OnLocksChanged(InputLockFlags locks)
    {
        // HasInstance, not Instance: 이음이 is absent from scenes that only play 노장 대사, and
        // touching Instance would create the conversation manager there.
        if (!AiConversationManager.HasInstance) return;
        AiConversationManager.Instance.SetInputLocked((locks & InputLockFlags.PushToTalk) != 0);
    }
}
