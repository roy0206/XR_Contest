using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CoreAudioManager = Core.Audio.AudioManager;

/// <summary>
/// <c>Core.Audio</c> 모듈 검증용 씬을 만든다. 공용 <c>DevSceneBuilder</c>를 건드리지 않으려고
/// 별도 파일로 두었다. 메뉴: IUM > Dev > Create Core Audio Test Scene.
/// </summary>
public static class CoreAudioTestSceneBuilder
{
    const string SceneDirectory = "Assets/@Developers/RYU/Scenes/Dev";
    const string ScenePath = SceneDirectory + "/CoreAudioTest.unity";

    [MenuItem("IUM/Dev/Create Core Audio Test Scene")]
    public static void CreateScene()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog(
                "Core Audio Test Scene",
                $"{ScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 기본 카메라를 남긴다. AudioListener가 거기 붙어 있고, 이 씬은 플레이어가 필요 없다.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var services = new GameObject("Core Services");
        services.AddComponent<SceneController>();
        services.AddComponent<DataManager>();

        // Core.Audio.AudioManager는 지연 생성을 하지 않으므로 반드시 배치해야 한다.
        var audio = new GameObject("Core Audio");
        var manager = audio.AddComponent<CoreAudioManager>();
        audio.AddComponent<CoreAudioBridge>();

        var dialogue = new GameObject("Dialogue Audio");
        dialogue.AddComponent<DialogueAudioHost>();
        dialogue.AddComponent<CoreAudioTestDriver>();

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[CoreAudioTestSceneBuilder] Created {ScenePath}. " +
                  "1 배치한 클립, 2 런타임 생성 클립(TTS 상당), 3 무음 폴백, S 정지, " +
                  "[ ] 대사 볼륨, M 대사 뮤트, , . 마스터 볼륨, N 마스터 뮤트, V 현재 값. " +
                  "2번을 재생한 채 [ ]로 대사 볼륨만 움직여 DIALOGUE 버스가 걸리는지 확인하십시오.",
            manager);
    }
}
