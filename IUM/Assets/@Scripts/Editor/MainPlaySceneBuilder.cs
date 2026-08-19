using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 우리 소유 메인 플레이 씬을 만든다. 팀원 소유 GongpoScene은 임시 복사본에서 루트 오브젝트를
/// 옮겨 오므로 원본 씬과 원본 목공 스크립트에는 어떤 변경도 기록하지 않는다.
/// </summary>
[InitializeOnLoad]
public static class MainPlaySceneBuilder
{
    public const string ScenePath = "Assets/@Developers/RYU/Scenes/Main/MainPlayScene.unity";

    const string BaseScenePath = "Assets/@Developers/RYU/Scenes/Dev/FreePlayTest.unity";
    const string WoodworkingScenePath = "Assets/@Scenes/GongpoScene.unity";
    const string TempScenePath = "Assets/@Developers/RYU/Scenes/Main/__GongpoImportTemp.unity";

    static readonly HashSet<string> BaseContentRoots = new()
    {
        "Directional Light",
        "PlaceholderProcess",
        "Tool_Saw",
        "Marker_West",
        "Marker_North",
        "Marker_East",
        "Ground",
        "Table"
    };

    static MainPlaySceneBuilder()
    {
        // 이번 통합에서 실제 씬 자산까지 남기기 위한 1회 실행 경로. 생성된 뒤에는 즉시 빠진다.
        EditorApplication.delayCall += BuildIfMissing;
    }

    [MenuItem("IUM/Dev/Create Main Play Scene")]
    public static void CreateMainPlayScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Main Play Scene", "기존 MainPlayScene을 다시 만들까요?", "다시 만들기", "취소"))
            return;

        Build(true);
    }

    static void BuildIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
        Build(false);
    }

    static void Build(bool overwrite)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BaseScenePath) == null ||
            AssetDatabase.LoadAssetAtPath<SceneAsset>(WoodworkingScenePath) == null)
        {
            Debug.LogError("[MainPlaySceneBuilder] 베이스 씬 또는 GongpoScene을 찾지 못했습니다.");
            return;
        }

        EnsureFolder("Assets/@Developers/RYU/Scenes", "Main");

        var targetExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
        if (targetExists && !overwrite) return;

        AssetDatabase.DeleteAsset(TempScenePath);
        var baseReady = targetExists
            ? ReplaceSceneContents(BaseScenePath, ScenePath)
            : AssetDatabase.CopyAsset(BaseScenePath, ScenePath);

        if (!baseReady || !AssetDatabase.CopyAsset(WoodworkingScenePath, TempScenePath))
        {
            Debug.LogError("[MainPlaySceneBuilder] 작업용 씬 복사에 실패했습니다.");
            AssetDatabase.DeleteAsset(TempScenePath);
            return;
        }

        Scene target = default;
        Scene source = default;
        try
        {
            target = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            RemoveBasePrototypeContent(target);

            source = EditorSceneManager.OpenScene(TempScenePath, OpenSceneMode.Additive);
            var woodworkingRoots = source.GetRootGameObjects();
            for (var i = 0; i < woodworkingRoots.Length; i++)
                SceneManager.MoveGameObjectToScene(woodworkingRoots[i], target);

            ConfigureMainScene(target);
            EditorSceneManager.MarkSceneDirty(target);
            EditorSceneManager.SaveScene(target, ScenePath);
        }
        finally
        {
            if (source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
            if (target.IsValid() && target.isLoaded) EditorSceneManager.CloseScene(target, true);
            AssetDatabase.DeleteAsset(TempScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        RegisterInBuildSettings(ScenePath);
        Debug.Log($"[MainPlaySceneBuilder] 생성 완료: {ScenePath}");
    }

    static void RemoveBasePrototypeContent(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        for (var i = 0; i < roots.Length; i++)
            if (BaseContentRoots.Contains(roots[i].name))
                Object.DestroyImmediate(roots[i]);
    }

    static void ConfigureMainScene(Scene scene)
    {
        var player = FindInScene<Player>(scene);
        if (player != null)
        {
            player.transform.SetPositionAndRotation(
                new Vector3(-1.6f, 0f, -4.4f),
                Quaternion.Euler(0f, 180f, 0f));
        }

        if (FindInScene<InGameDialogue>(scene) == null)
            CreateInScene<InGameDialogue>(scene, "InGameDialogue");

        var runner = CreateInScene<ProcessRunner>(scene, "ProcessRunner");
        runner.gameObject.AddComponent<PlayerPoseTracker>();

        var runnerData = new SerializedObject(runner);
        runnerData.FindProperty("player").objectReferenceValue = player;
        runnerData.FindProperty("sceneProcess").enumValueIndex = (int)ProcessId.Makmeok;
        runnerData.FindProperty("useSavedProgress").boolValue = true;
        runnerData.FindProperty("continueInSameScene").boolValue = true;
        runnerData.FindProperty("allowForceStep").boolValue = false;
        runnerData.ApplyModifiedPropertiesWithoutUndo();

        var bridge = CreateInScene<MainPlayProcessBridge>(scene, "MainPlayProcessBridge");
        var zones = FindAllInScene<InkLineZone>(scene);
        var bridgeData = new SerializedObject(bridge);
        bridgeData.FindProperty("runner").objectReferenceValue = runner;
        bridgeData.FindProperty("sawingZone").objectReferenceValue = FindInScene<PlaneZone>(scene);
        var zoneProperty = bridgeData.FindProperty("inkLineZones");
        zoneProperty.arraySize = zones.Count;
        for (var i = 0; i < zones.Count; i++)
            zoneProperty.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];

        var assemblyTargets = FindAllInScene<AssemblyTarget>(scene);
        var assemblyProperty = bridgeData.FindProperty("assemblyTargets");
        assemblyProperty.arraySize = assemblyTargets.Count;
        for (var i = 0; i < assemblyTargets.Count; i++)
            assemblyProperty.GetArrayElementAtIndex(i).objectReferenceValue = assemblyTargets[i];
        bridgeData.ApplyModifiedPropertiesWithoutUndo();

        var pause = FindInScene<PauseController>(scene);
        if (pause != null)
        {
            var pauseData = new SerializedObject(pause);
            pauseData.FindProperty("mainSceneName").stringValue = "StartScene";
            pauseData.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log($"[MainPlaySceneBuilder] 먹매김 구역 {zones.Count}개를 공정 신호에 연결했습니다.", bridge);
    }

    static T CreateInScene<T>(Scene scene, string name) where T : Component
    {
        var host = new GameObject(name);
        SceneManager.MoveGameObjectToScene(host, scene);
        return host.AddComponent<T>();
    }

    static T FindInScene<T>(Scene scene) where T : Component
    {
        var roots = scene.GetRootGameObjects();
        for (var i = 0; i < roots.Length; i++)
        {
            var found = roots[i].GetComponentInChildren<T>(true);
            if (found != null) return found;
        }

        return null;
    }

    static List<T> FindAllInScene<T>(Scene scene) where T : Component
    {
        var result = new List<T>();
        var roots = scene.GetRootGameObjects();
        for (var i = 0; i < roots.Length; i++)
            result.AddRange(roots[i].GetComponentsInChildren<T>(true));
        return result;
    }

    static void EnsureFolder(string parent, string name)
    {
        var path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }

    static bool ReplaceSceneContents(string sourcePath, string destinationPath)
    {
        // 파일 내용만 교체해 destination의 meta/GUID를 보존한다. 이 씬을 다른 자산이 참조하기
        // 시작한 뒤 다시 생성해도 참조와 Build Settings 항목이 끊기지 않아야 한다.
        FileUtil.ReplaceFile(sourcePath, destinationPath);
        AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(destinationPath) != null;
    }

    static void RegisterInBuildSettings(string scenePath)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (var i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path != scenePath) continue;
            // 씬을 다시 만들면 meta GUID도 새로 생긴다. enabled만 유지하면 Build Settings가
            // 삭제된 이전 GUID를 계속 들 수 있으므로 항목 자체를 항상 다시 만든다.
            scenes[i] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = scenes.ToArray();
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
