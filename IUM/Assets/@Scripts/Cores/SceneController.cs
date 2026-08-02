using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    /// <summary>
    /// 주 씬을 유지한 채 씬을 겹쳐 올린다. 컷씬 오버레이가 쓰는 경로다.
    ///
    /// Raises no <see cref="ISceneEventListener"/> callback on purpose. Those mean "the current
    /// scene is going away" and listeners stop playback on them, so firing one here would cut the
    /// very cutscene being loaded. It also does not fade: when to black out belongs to the caller,
    /// and <see cref="SceneSO"/>'s transition settings describe a scene swap, not an overlay.
    /// <see cref="IsTransitioning"/> stays untouched for the same reason.
    /// </summary>
    /// <param name="makeActive">
    /// Makes the loaded scene active, which is only needed when its lighting settings should apply.
    /// </param>
    /// <returns>The loaded scene, or an invalid scene when the load could not start.</returns>
    public Task<Scene> LoadAdditiveAsync(string sceneName, bool makeActive = false)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneController] Additive scene is not available in Build Settings: '{sceneName}'.");
            return Task.FromResult(default(Scene));
        }

        var existing = SceneManager.GetSceneByName(sceneName);
        if (existing.isLoaded)
        {
            Debug.LogWarning($"[SceneController] '{sceneName}' is already loaded additively.");
            return Task.FromResult(existing);
        }

        var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (operation == null)
        {
            Debug.LogError($"[SceneController] Failed to start the additive load of '{sceneName}'.");
            return Task.FromResult(default(Scene));
        }

        var completion = new TaskCompletionSource<Scene>();
        operation.completed += _ =>
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (makeActive && scene.IsValid()) SceneManager.SetActiveScene(scene);
            completion.SetResult(scene);
        };

        return completion.Task;
    }

    /// <summary>Unloads a scene brought in by <see cref="LoadAdditiveAsync"/>. Safe to call twice.</summary>
    public Task UnloadAdditiveAsync(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return Task.CompletedTask;

        // Unity leaves no active scene if the active one is unloaded, and objects created after
        // that have nowhere to go, so hand the role back before it disappears.
        if (SceneManager.GetActiveScene() == scene) RestoreActiveScene(scene);

        var operation = SceneManager.UnloadSceneAsync(scene);
        if (operation == null)
        {
            Debug.LogWarning($"[SceneController] Failed to start the unload of '{scene.name}'.");
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>();
        operation.completed += _ => completion.SetResult(true);
        return completion.Task;
    }

    public bool IsAdditiveLoaded(string sceneName) =>
        !string.IsNullOrWhiteSpace(sceneName) && SceneManager.GetSceneByName(sceneName).isLoaded;

    static void RestoreActiveScene(Scene leaving)
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var candidate = SceneManager.GetSceneAt(i);
            if (candidate == leaving || !candidate.isLoaded) continue;
            SceneManager.SetActiveScene(candidate);
            return;
        }
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
