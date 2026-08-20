#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>공용 Player 프리팹을 StartScene 루트에 직접 배치하는 에디터 통합 명령.</summary>
static class StartScenePlayerInstaller
{
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string StartScenePath = "Assets/@Scenes/StartScene.unity";
    const string InstallMarker = "Temp/FixedUIValidation/install-direct-player.marker";

    [InitializeOnLoadMethod]
    static void InstallWhenRequested()
    {
        if (!File.Exists(InstallMarker)) return;

        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(InstallMarker)) return;
            File.Delete(InstallMarker);
            PlacePlayer();
        };
    }

    [MenuItem("Tools/IUM/Start/Place Player Directly In StartScene %&#i")]
    public static void PlacePlayer()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[StartScene Player] Play Mode를 종료한 뒤 실행해야 합니다.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != StartScenePath)
            scene = EditorSceneManager.OpenScene(StartScenePath, OpenSceneMode.Single);

        var existing = Object.FindFirstObjectByType<Player>();
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[StartScene Player] 프리팹을 찾을 수 없습니다: {PlayerPrefabPath}");
            return;
        }

        var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        playerObject.name = "Start Scene Player";
        playerObject.transform.SetPositionAndRotation(
            new Vector3(7.6152124f, 0.3202884f, 3.3842149f),
            Quaternion.Euler(0f, 178.88274f, 0f));

        var player = playerObject.GetComponent<Player>();
        if (player != null)
            player.Head.localRotation = Quaternion.Euler(-1.547f, 0f, 0f);

        var sourceCameraObject = GameObject.Find("Main Camera");
        if (sourceCameraObject != null)
            sourceCameraObject.SetActive(false);

        // 사용자가 추가한 바닥의 윗면을 기존 카메라로부터 계산한 발 높이와 일치시킨다.
        var floor = GameObject.Find("Cube");
        if (floor != null && floor.TryGetComponent<BoxCollider>(out _))
        {
            var position = floor.transform.position;
            position.y = 0.3202884f - floor.transform.lossyScale.y * 0.5f;
            floor.transform.position = position;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = playerObject;
        Debug.Log("[StartScene Player] Player.prefab을 StartScene 루트에 직접 배치했습니다.", playerObject);
    }
}
#endif
