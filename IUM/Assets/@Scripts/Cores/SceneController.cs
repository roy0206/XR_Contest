using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : Singleton<SceneController>
{
    [Header("Scene Configurations")]
    [SerializeField] List<SceneSO> sceneConfigs = new();

    public static float LoadingProgress { get; private set; }
    public static bool IsTransitioning { get; private set; }

    readonly List<ISceneEventListener> _listeners = new();
    readonly List<ISceneEventListener> _listenerSnapshot = new();
    Dictionary<string, SceneSO> _configMap = new(StringComparer.Ordinal);

    protected override void Awake()
    {
        base.Awake();
        if (!ReferenceEquals(Instance, this)) return;
        BuildConfigMap();
        ScreenFader.Instance?.SetInstant(0f);
    }

    void BuildConfigMap()
    {
        _configMap.Clear();
        if (sceneConfigs == null) return;

        for (var i = 0; i < sceneConfigs.Count; i++)
        {
            var config = sceneConfigs[i];
            if (config == null || !config.IsValid) continue;
            if (!_configMap.TryAdd(config.targetSceneName, config))
                Debug.LogWarning($"[SceneController] Duplicate scene configuration: {config.targetSceneName}", config);
        }
    }

    public void LoadScene(string sceneName)
    {
        if (IsTransitioning)
        {
            Debug.LogWarning($"[SceneController] Ignored '{sceneName}' because another transition is active.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneController] Scene is not available in Build Settings: '{sceneName}'.");
            return;
        }

        if (!_configMap.TryGetValue(sceneName, out var config))
        {
            config = ScriptableObject.CreateInstance<SceneSO>();
            config.targetSceneName = sceneName;
            config.useLoadingScene = false;
            config.hideFlags = HideFlags.HideAndDontSave;
        }

        StartCoroutine(LoadSceneRoutine(config));
    }

    public void RegisterListener(ISceneEventListener listener)
    {
        if (listener != null && !_listeners.Contains(listener)) _listeners.Add(listener);
    }

    public void UnregisterListener(ISceneEventListener listener) => _listeners.Remove(listener);

    IEnumerator LoadSceneRoutine(SceneSO config)
    {
        IsTransitioning = true;
        LoadingProgress = 0f;
        var completed = false;
        var targetSceneName = config.targetSceneName;
        var runtimeConfig = (config.hideFlags & HideFlags.DontSave) != 0;

        try
        {
            NotifyLoadStart(targetSceneName);
            yield return PlayTransition(config.exitTransition, config.transitionDuration);

            var useLoadingScene = config.useLoadingScene &&
                                  !string.IsNullOrWhiteSpace(config.loadingSceneName) &&
                                  config.loadingSceneName != config.targetSceneName &&
                                  Application.CanStreamedLevelBeLoaded(config.loadingSceneName);

            if (useLoadingScene)
            {
                yield return SceneManager.LoadSceneAsync(config.loadingSceneName, LoadSceneMode.Single);
                yield return PlayTransition(config.enterTransition, config.transitionDuration);
            }

            var operation = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError($"[SceneController] Failed to start loading '{targetSceneName}'.");
                yield break;
            }

            operation.allowSceneActivation = false;
            var elapsed = 0f;
            while (operation.progress < 0.9f || elapsed < config.leastHoldingDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                LoadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
                yield return null;
            }

            LoadingProgress = 1f;
            if (useLoadingScene)
                yield return PlayTransition(config.exitTransition, config.transitionDuration);

            operation.allowSceneActivation = true;
            yield return operation;
            yield return null; // Allow the new XR camera to become active before binding the fader.
            yield return PlayTransition(config.enterTransition, config.transitionDuration);
            completed = true;
        }
        finally
        {
            IsTransitioning = false;
            if (runtimeConfig && config != null) Destroy(config);
        }

        if (completed) NotifyLoadComplete(targetSceneName);
    }

    IEnumerator PlayTransition(TransitionEffect effect, float duration)
    {
        if (effect == TransitionEffect.None || duration <= 0f) yield break;
        var fader = ScreenFader.Instance;
        if (fader == null) yield break;

        Tween tween;
        switch (effect)
        {
            case TransitionEffect.FadeIn:
                tween = fader.FadeIn(duration);
                break;
            case TransitionEffect.CrossFade:
                yield return fader.FadeOut(duration * 0.5f).WaitForCompletion();
                tween = fader.FadeIn(duration * 0.5f);
                break;
            default:
                if (effect is TransitionEffect.SlideLeft or TransitionEffect.SlideRight or
                    TransitionEffect.SlideUp or TransitionEffect.SlideDown)
                    Debug.LogWarning($"[SceneController] {effect} is not implemented; using comfort fade.");
                tween = fader.FadeOut(duration);
                break;
        }

        yield return tween.WaitForCompletion();
    }

    void NotifyLoadStart(string sceneName) => NotifyListeners(sceneName, true);
    void NotifyLoadComplete(string sceneName) => NotifyListeners(sceneName, false);

    void NotifyListeners(string sceneName, bool isStart)
    {
        _listenerSnapshot.Clear();
        _listenerSnapshot.AddRange(_listeners);
        for (var i = 0; i < _listenerSnapshot.Count; i++)
        {
            var listener = _listenerSnapshot[i];
            if (listener == null) continue;
            try
            {
                if (isStart) listener.OnSceneLoadStart(sceneName);
                else listener.OnSceneLoadComplete(sceneName);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}

public interface ISceneEventListener
{
    void OnSceneLoadStart(string sceneName);
    void OnSceneLoadComplete(string sceneName);
}
