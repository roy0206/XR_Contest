using UnityEngine;

[CreateAssetMenu(fileName = "SceneSO", menuName = "IUM/Scene Configuration")]
public class SceneSO : ScriptableObject
{
    [Header("Scene")]
    [Tooltip("Target scene name as registered in Build Settings.")]
    public string targetSceneName;

    [Header("Loading Scene")]
    public bool useLoadingScene = true;
    public string loadingSceneName;
    [Min(0f)] public float leastHoldingDuration;

    [Header("Comfort Transition")]
    public TransitionEffect enterTransition = TransitionEffect.FadeIn;
    public TransitionEffect exitTransition = TransitionEffect.FadeOut;
    [Min(0f)] public float transitionDuration = 0.5f;

    public bool IsValid => !string.IsNullOrWhiteSpace(targetSceneName);

#if UNITY_EDITOR
    void OnValidate()
    {
        transitionDuration = Mathf.Max(0f, transitionDuration);
        leastHoldingDuration = Mathf.Max(0f, leastHoldingDuration);

        if (!IsValid)
            Debug.LogWarning($"[SceneSO] '{name}' has no target scene.", this);
        if (useLoadingScene && string.IsNullOrWhiteSpace(loadingSceneName))
            Debug.LogWarning($"[SceneSO] '{name}' enables a loading scene but has no loading scene name.", this);
    }
#endif
}

public enum TransitionEffect
{
    None = 0,
    FadeIn = 1,
    FadeOut = 2,
    SlideLeft = 3,
    SlideRight = 4,
    SlideUp = 5,
    SlideDown = 6,
    CrossFade = 7
}
