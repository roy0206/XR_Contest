using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

/// <summary>
/// Builds the desktop test scenes from code so no shared scene has to be edited by hand.
/// Menu: IUM > Dev > Create Interaction Test Scene, IUM > Dev > Create AI Voice Test Scene,
/// IUM > Dev > Create Gongpo Test Scene, IUM > Dev > Create Dialogue Test Scene,
/// IUM > Dev > Create Cutscene Test Scene, IUM > Dev > Create Flow Test Scene,
/// IUM > Dev > Create Free Play Test Scene, IUM > Dev > Create Prologue Cutscene Scene,
/// IUM > Dev > Create Ending Cutscene Scene.
/// </summary>
public static class DevSceneBuilder
{
    const string SceneDirectory = "Assets/@Developers/RYU/Scenes/Dev";
    const string ScenePath = SceneDirectory + "/InteractionTest.unity";
    const string VoiceScenePath = SceneDirectory + "/AiVoiceTest.unity";
    const string GongpoScenePath = SceneDirectory + "/GongpoTest.unity";
    const string DialogueScenePath = SceneDirectory + "/DialogueTest.unity";
    const string CutsceneScenePath = SceneDirectory + "/CutsceneTest.unity";
    const string FlowSceneName = "FlowTest";
    const string FlowScenePath = SceneDirectory + "/" + FlowSceneName + ".unity";
    const string FreePlaySceneName = "FreePlayTest";
    const string FreePlayScenePath = SceneDirectory + "/" + FreePlaySceneName + ".unity";
    const string CutsceneSceneDirectory = "Assets/@Developers/RYU/Scenes/Cutscene";
    const string PrologueScenePath = CutsceneSceneDirectory + "/Cutscene_Prologue.unity";
    const string EndingScenePath = CutsceneSceneDirectory + "/Cutscene_Ending.unity";
    const string HudUxmlPath = "Assets/@UI/Ai/IeumiHud.uxml";
    const string HudPanelPath = "Assets/@UI/Ai/IeumiHudPanelSettings.asset";
    const string SubtitleUxmlPath = "Assets/@UI/Dialogue/Subtitle.uxml";
    const string SubtitlePanelPath = "Assets/@UI/Dialogue/SubtitlePanelSettings.asset";
    const string CutsceneUxmlPath = "Assets/@UI/Cutscene/Cutscene.uxml";
    const string CutscenePanelPath = "Assets/@UI/Cutscene/CutscenePanelSettings.asset";
    const string PauseUxmlPath = "Assets/@UI/Pause/PauseMenu.uxml";
    const string PausePanelPath = "Assets/@UI/Pause/PauseMenuPanelSettings.asset";
    const string StartUxmlPath = "Assets/@UI/Start/StartMenu.uxml";
    const string StartPanelPath = "Assets/@UI/Start/StartMenuPanelSettings.asset";
    const string ThemePath = "Assets/@UI/Start/StartMenuTheme.tss";
    const string CorrectMaterialPath = "Assets/Textures/CorrectMat.mat";
    const string WrongMaterialPath = "Assets/Textures/WrongMat.mat";

    [MenuItem("IUM/Dev/Create Interaction Test Scene")]
    public static void CreateInteractionTestScene()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog(
                "Interaction Test Scene",
                $"{ScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        CreateGround();

        // Bench height and depth are chosen so a standing player's hand anchors actually reach
        // the top surface: the top sits at y = 1.0 and the front face at z = 0.75.
        var table = CreateBox("Table", new Vector3(0f, 0.5f, 1f), new Vector3(1.8f, 1f, 0.5f));
        CreatePlayer();

        CreateTool("Tool_Saw", new Vector3(-0.5f, 1.04f, 0.9f), new Vector3(0.5f, 0.05f, 0.12f));
        CreateTool("Tool_Chisel", new Vector3(0.15f, 1.04f, 0.9f), new Vector3(0.25f, 0.05f, 0.05f));
        CreateTool("Tool_Mallet", new Vector3(0.5f, 1.06f, 0.9f), new Vector3(0.2f, 0.08f, 0.08f));

        CreateSocket("Socket_ToolRest", new Vector3(0.8f, 1.12f, 0.9f), new Vector3(0.3f, 0.25f, 0.3f));

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[DevSceneBuilder] Created {ScenePath}. " +
                  "Desktop keys: WASD move, hold right mouse to look, Q/E snap turn, " +
                  "F left grab, left mouse right grab. " +
                  "Select the Player to see both grab radii in the scene view.", table);
    }

    /// <summary>
    /// Minimal scene for the 이음이 voice pipeline: a player that produces PTT commands, the HUD,
    /// and the debug switcher that stands in for the 3~6단계 process systems.
    /// </summary>
    [MenuItem("IUM/Dev/Create AI Voice Test Scene")]
    public static void CreateAiVoiceTestScene()
    {
        if (File.Exists(VoiceScenePath) &&
            !EditorUtility.DisplayDialog(
                "AI Voice Test Scene",
                $"{VoiceScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        CreateGround();
        CreateBox("Table", new Vector3(0f, 0.5f, 1f), new Vector3(1.8f, 1f, 0.5f));
        CreatePlayer();

        var conversation = new GameObject("AiConversationManager");
        conversation.AddComponent<AiConversationManager>();
        conversation.AddComponent<AiProcessDebugSwitcher>();

        CreateHud();

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, VoiceScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[DevSceneBuilder] Created {VoiceScenePath}. " +
                  "Hold T to ask 이음이, or type a question in the debug field. " +
                  "1~6 switch the process, 9 reports a failure, 0 clears it, L toggles the 노장 lock. " +
                  "Without Assets/StreamingAssets/ai_secrets.json every service runs in mock mode.",
            conversation);
    }

    /// <summary>
    /// Desktop bench for the 공포 assembly rules: a 기단부 that starts assembled plus the two parts
    /// that go on top of it. GongpoScene stays untouched; this scene exists so the ID gating,
    /// tolerance judgement and seat inheritance can be verified without a headset.
    /// </summary>
    [MenuItem("IUM/Dev/Create Gongpo Test Scene")]
    public static void CreateGongpoTestScene()
    {
        if (File.Exists(GongpoScenePath) &&
            !EditorUtility.DisplayDialog(
                "Gongpo Test Scene",
                $"{GongpoScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        CreateGround();
        CreateBox("Table", new Vector3(0f, 0.5f, 1f), new Vector3(1.8f, 1f, 0.5f));
        CreatePlayer();

        var correct = LoadMaterial(CorrectMaterialPath);
        var wrong = LoadMaterial(WrongMaterialPath);

        // 기단부. It is the only part that starts assembled, so its seat is the only one open.
        var basePart = CreatePart("Part_Base", new Vector3(0f, 1.06f, 1f), new Vector3(0.4f, 0.12f, 0.4f), true);
        CreateSeat(basePart.transform, "Seat_Column", new Vector3(0f, 0.06f, 0f),
            "Column", "Bo", correct, wrong);

        // 기둥. Carries the seat for the next part up, locked until the column itself is seated.
        var column = CreateGrabbablePart("Part_Column", new Vector3(-0.45f, 1.15f, 0.85f),
            new Vector3(0.12f, 0.3f, 0.12f), "Column", new Vector3(0f, -0.15f, 0f));
        CreateSeat(column.transform, "Seat_Bo", new Vector3(0f, 0.15f, 0f),
            "Locked", string.Empty, correct, wrong);

        // 공포 부재. Only becomes installable once the column hands the "Bo" ID down.
        CreateGrabbablePart("Part_Bo", new Vector3(0.45f, 1.09f, 0.85f),
            new Vector3(0.3f, 0.08f, 0.12f), "Bo", new Vector3(0f, -0.04f, 0f));

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, GongpoScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[DevSceneBuilder] Created {GongpoScenePath}. " +
                  "Desktop keys: WASD move, hold right mouse to look, Q/E snap turn, " +
                  "F left grab, left mouse right grab. " +
                  "Order is enforced: Part_Column seats on 기단부 first, and only then does " +
                  "Part_Bo become installable on the column. " +
                  "A held part turns green inside tolerance and red outside it.", basePart);
    }

    /// <summary>
    /// Desktop bench for 노장 대사: the player keeps full control while a line plays, so the point
    /// of the scene is watching 공정 판정 close and PTT lock without movement or grabbing being
    /// affected (F-011 1.4). The overlay makes both gates visible.
    /// </summary>
    [MenuItem("IUM/Dev/Create Dialogue Test Scene")]
    public static void CreateDialogueTestScene()
    {
        if (File.Exists(DialogueScenePath) &&
            !EditorUtility.DisplayDialog(
                "Dialogue Test Scene",
                $"{DialogueScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        CreateGround();
        CreateBox("Table", new Vector3(0f, 0.5f, 1f), new Vector3(1.8f, 1f, 0.5f));
        CreatePlayer();

        // A tool is present so it is obvious that grabbing stays available while 노장 speaks.
        CreateTool("Tool_Saw", new Vector3(-0.5f, 1.04f, 0.9f), new Vector3(0.5f, 0.05f, 0.12f));

        // No 노장 object: he is represented by screen-fixed UI, so the scene holds no speaker body.
        var dialogue = new GameObject("InGameDialogue");
        dialogue.AddComponent<InGameDialogue>();
        dialogue.AddComponent<DialogueDebugController>();

        CreateSubtitleView();
        CreatePauseMenu();

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, DialogueScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[DevSceneBuilder] Created {DialogueScenePath}. " +
                  "Z 챕터 도입 · X 먹매김 도입 · C 훈수 · V 평가 · B 이음이 · " +
                  "Backspace 중단 · Esc 일시정지. " +
                  "WASD와 잡기는 대사 중에도 그대로 동작하고, 공정 판정과 PTT만 잠깁니다. " +
                  "C(훈수)는 판정을 막지 않고, V(평가)는 우선순위가 높아 재생 중인 대사를 자릅니다. " +
                  "노장은 씬에 없고 화면 자막으로만 표현되며, 음성 파일이 없으므로 각 줄은 " +
                  "글자 수로 추정한 시간만큼 재생됩니다.",
            dialogue);
    }

    static void CreateSubtitleView()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SubtitleUxmlPath);
        if (uxml == null)
        {
            Debug.LogError($"[DevSceneBuilder] {SubtitleUxmlPath} is missing; the subtitle was skipped.");
            return;
        }

        var view = new GameObject("SubtitleView");
        var document = view.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = LoadOrCreatePanelSettings(SubtitlePanelPath);
        view.AddComponent<SubtitleView>();
    }

    /// <summary>
    /// 컷씬 테스트 씬. Verifies the opposite of the dialogue scene: here the player is locked out
    /// entirely (F-003 3.4) and only 건너뛰기 survives. The player rig is present precisely so that
    /// WASD going dead during a cutscene is observable.
    /// </summary>
    [MenuItem("IUM/Dev/Create Cutscene Test Scene")]
    public static void CreateCutsceneTestScene()
    {
        if (File.Exists(CutsceneScenePath) &&
            !EditorUtility.DisplayDialog(
                "Cutscene Test Scene",
                $"{CutsceneScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        CreateGround();
        CreateBox("Table", new Vector3(0f, 0.5f, 1f), new Vector3(1.8f, 1f, 0.5f));
        CreatePlayer();

        var director = new GameObject("CutsceneDirector");
        director.AddComponent<CutsceneDirector>();
        director.AddComponent<CutsceneDebugController>();

        CreateCutsceneView();
        CreatePauseMenu();

        // The cutscene's own dialogue player raises SubtitleChanged, so the subtitle view is needed
        // here too. No InGameDialogue in this scene: it would only add a second, unused source.
        CreateSubtitleView();

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, CutsceneScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[DevSceneBuilder] Created {CutsceneScenePath}. " +
                  "1 프롤로그 · Backspace 중단 · Space 2초 유지로 건너뛰기 · Esc 일시정지. " +
                  "컷씬은 이 씬을 유지한 채 Cutscene_Prologue를 겹쳐 올립니다. " +
                  "재생 중에는 이동·잡기·PTT가 모두 잠기고 공정 판정도 닫히며, " +
                  "끝나면 플레이어 위치와 잡고 있던 물건이 그대로 남아 있는지 확인할 수 있습니다. " +
                  "컷씬 도중 Esc로 일시정지하면 음성과 자막이 함께 멈추고, 해제하면 멈춘 지점부터 이어집니다. " +
                  "먼저 IUM > Dev > Create Prologue Cutscene Scene을 실행해 컷씬 씬을 만드십시오.",
            director);
    }

    /// <summary>
    /// 흐름 테스트 씬 (F-001 · F-003 · F-019). 메인 화면 → 새 게임 → 프롤로그 → 저장 → 엔딩 →
    /// 진행 삭제까지 한 씬에서 확인한다.
    ///
    /// StartScene을 쓰지 않고 새로 만드는 이유는 그 씬에 컷씬용 뷰가 하나도 없기 때문이다. 컷씬은
    /// 주 씬을 유지한 채 겹쳐 올라오므로, 캡션·건너뛰기 게이지·대사 자막·일시정지 메뉴는 전부 이
    /// 씬이 들고 있어야 한다. 정적 데이터는 그대로 쓰므로 flow.json과 cutscene.json이 가리키는
    /// 컷씬 씬을 먼저 만들어 두어야 한다.
    /// </summary>
    [MenuItem("IUM/Dev/Create Flow Test Scene")]
    public static void CreateFlowTestScene()
    {
        if (File.Exists(FlowScenePath) &&
            !EditorUtility.DisplayDialog(
                "Flow Test Scene",
                $"{FlowScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // The default camera stays, unlike the other dev scenes: the menu has no player rig, and
        // this camera carries both the AudioListener and the MainCamera tag that CutsceneDirector
        // suspends and hands the view back to.
        var camera = Camera.main;
        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
        }

        var services = new GameObject("Core Services");
        services.AddComponent<SceneController>();
        services.AddComponent<AudioManager>();
        services.AddComponent<DataManager>();

        // Both are lazily created on first access anyway; placing them here makes the scene
        // readable and lets their inspectors be reached while playing.
        var flow = new GameObject("GameFlow");
        flow.AddComponent<GameFlow>();

        var director = new GameObject("CutsceneDirector");
        director.AddComponent<CutsceneDirector>();

        CreateStartMenu();
        CreateEventSystem();

        // Everything a cutscene needs from its host scene. StartScene has none of these, which is
        // why the prologue plays blind there.
        CreateCutsceneView();
        CreateSubtitleView();
        CreatePauseMenu(FlowSceneName);

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, FlowScenePath);
        AssetDatabase.Refresh();

        RegisterInBuildSettings(FlowScenePath);

        Debug.Log($"[DevSceneBuilder] Created {FlowScenePath}. " +
                  "게임 시작 → 프롤로그 컷씬 → FreePlayTest → Enter → 엔딩 컷씬 → 진행 삭제 후 이 씬으로 복귀. " +
                  "컷씬 재생 중에는 메인 메뉴가 화면에서 빠지고 끝나면 돌아옵니다. " +
                  "Esc 일시정지 · 옵션은 메인 화면과 일시정지 양쪽에서 같은 세 슬라이더를 씁니다. " +
                  "먼저 Create Prologue Cutscene Scene · Create Ending Cutscene Scene · " +
                  "Create Free Play Test Scene을 실행해 두십시오.",
            flow);
    }

    /// <summary>
    /// 자유 플레이 테스트 씬. flow.json의 tutorial 항목이 가리키는 임시 구간으로, 메인 컨텐츠 여섯
    /// 공정이 들어올 자리를 대신한다.
    ///
    /// 여기가 흐름의 가운데다. 프롤로그가 끝나면 이 씬으로 들어오고, 돌아다니다 Enter를 누르면
    /// 엔딩으로 넘어간다. 그 사이 어느 시점에 나갔다 들어와도 같은 자리에서 이어지는지가 저장
    /// 기능의 실제 검증이다.
    ///
    /// 엔딩 컷씬은 이 씬을 유지한 채 겹쳐 올라오므로 컷씬용 뷰 일체를 여기서도 들고 있어야 한다.
    /// </summary>
    [MenuItem("IUM/Dev/Create Free Play Test Scene")]
    public static void CreateFreePlayTestScene()
    {
        if (File.Exists(FreePlayScenePath) &&
            !EditorUtility.DisplayDialog(
                "Free Play Test Scene",
                $"{FreePlayScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        CreateGround();
        CreateBox("Table", new Vector3(0f, 0.5f, 1f), new Vector3(1.8f, 1f, 0.5f));
        CreatePlayer();

        // Landmarks: the point of walking around is seeing that you came back to where you left,
        // and a bare plane gives the eye nothing to judge that by.
        CreateBox("Marker_North", new Vector3(0f, 1f, 8f), new Vector3(1f, 2f, 1f));
        CreateBox("Marker_East", new Vector3(8f, 1f, 0f), new Vector3(1f, 2f, 1f));
        CreateBox("Marker_West", new Vector3(-8f, 1f, 0f), new Vector3(1f, 2f, 1f));

        CreateTool("Tool_Saw", new Vector3(-0.5f, 1.04f, 0.9f), new Vector3(0.5f, 0.05f, 0.12f));

        var services = new GameObject("Core Services");
        services.AddComponent<SceneController>();
        services.AddComponent<AudioManager>();
        services.AddComponent<DataManager>();

        var flow = new GameObject("GameFlow");
        flow.AddComponent<GameFlow>();

        var director = new GameObject("CutsceneDirector");
        director.AddComponent<CutsceneDirector>();

        var process = new GameObject("PlaceholderProcess");
        process.AddComponent<PlaceholderProcess>();
        process.AddComponent<PlayerPoseTracker>();

        CreateCutsceneView();
        CreateSubtitleView();
        CreatePauseMenu(FlowSceneName);

        Undo.ClearAll();
        Directory.CreateDirectory(SceneDirectory);
        EditorSceneManager.SaveScene(scene, FreePlayScenePath);
        AssetDatabase.Refresh();

        RegisterInBuildSettings(FreePlayScenePath);

        Debug.Log($"[DevSceneBuilder] Created {FreePlayScenePath}. " +
                  "WASD로 돌아다니다 Enter를 누르면 tutorial~gongpoPuzzle이 완료 처리되고 엔딩으로 넘어갑니다. " +
                  "위치는 5초마다, 씬을 떠날 때, 애플리케이션이 내려갈 때 저장됩니다. " +
                  "Esc > 메인 화면으로 나간 뒤 이어하기를 누르면 서 있던 자리로 돌아와야 합니다. " +
                  "flow.json의 tutorial 항목이 이 씬을 가리킵니다.",
            process);
    }

    /// <summary>
    /// 메인 화면 UI (F-001). Reuses the authored StartMenu.uxml and its PanelSettings, so this scene
    /// and StartScene show the same menu.
    /// </summary>
    static void CreateStartMenu()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(StartUxmlPath);
        if (uxml == null)
        {
            Debug.LogError($"[DevSceneBuilder] {StartUxmlPath} is missing; the start menu was skipped.");
            return;
        }

        var view = new GameObject("Start UI");
        var document = view.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = LoadOrCreatePanelSettings(StartPanelPath);
        view.AddComponent<StartMenuController>();
    }

    /// <summary>
    /// 버튼이 있는 씬에만 필요하다. Created through Unity's own menu command so the input module is
    /// wired the way a hand-made scene's would be; the manual path is only a fallback, and relies on
    /// the module's built-in default actions.
    /// </summary>
    static void CreateEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        EditorApplication.ExecuteMenuItem("GameObject/UI/Event System");
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    /// <summary>
    /// 프롤로그 컷씬 씬 (F-003 3.3). Loaded additively over whatever is running, so it holds only
    /// the staging: a camera, the fire light, the parts that collapse, and the stage script.
    /// Everything the cutscene needs from outside — 대사, 자막, 암전, 건너뛰기 — comes through
    /// CutsceneContext, so this scene has no director, no player and no UI of its own.
    /// </summary>
    [MenuItem("IUM/Dev/Create Prologue Cutscene Scene")]
    public static void CreatePrologueCutsceneScene()
    {
        if (File.Exists(PrologueScenePath) &&
            !EditorUtility.DisplayDialog(
                "Prologue Cutscene Scene",
                $"{PrologueScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // The default camera carries the MainCamera tag, which would fight the host scene's camera
        // for Camera.main. The cutscene brings its own, untagged.
        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        var sun = Object.FindAnyObjectByType<Light>();
        if (sun != null)
        {
            sun.intensity = 0.15f;
            sun.color = new Color(0.4f, 0.45f, 0.6f);
        }

        var rig = new GameObject("CameraRig");
        rig.transform.position = new Vector3(0f, 2.2f, -9f);

        var cameraObject = new GameObject("CutsceneCamera", typeof(Camera));
        cameraObject.transform.SetParent(rig.transform, false);
        var stageCamera = cameraObject.GetComponent<Camera>();
        stageCamera.clearFlags = CameraClearFlags.SolidColor;
        stageCamera.backgroundColor = new Color(0.03f, 0.02f, 0.02f);
        stageCamera.depth = 10f;
        cameraObject.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

        CreateGround();

        // Placeholder 숭례문. Stand-ins for the real model: the point of this scene is that the
        // stage script drives scene objects directly, not that these boxes look like anything.
        var gate = new GameObject("Gate");
        CreateBox("Gate_Base", new Vector3(0f, 0.6f, 0f), new Vector3(7f, 1.2f, 2.5f))
            .transform.SetParent(gate.transform, true);

        var collapsing = new Transform[3];
        for (var i = 0; i < collapsing.Length; i++)
        {
            var pivot = new GameObject($"Collapse_{i}");
            pivot.transform.SetParent(gate.transform, false);
            pivot.transform.localPosition = new Vector3(-2.2f + i * 2.2f, 1.2f, 0f);

            var beam = CreateBox($"Beam_{i}", Vector3.zero, new Vector3(1.8f, 2.4f, 1.8f));
            beam.transform.SetParent(pivot.transform, false);
            beam.transform.localPosition = new Vector3(0f, 1.2f, 0f);

            collapsing[i] = pivot.transform;
        }

        var fireObject = new GameObject("FireLight");
        fireObject.transform.position = new Vector3(0f, 3.5f, 1.5f);
        var fireLight = fireObject.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.55f, 0.2f);
        fireLight.range = 24f;
        fireLight.intensity = 2f;
        fireLight.enabled = false;

        var stageObject = new GameObject("PrologueStage");
        var stage = stageObject.AddComponent<PrologueStage>();

        var serialized = new SerializedObject(stage);
        serialized.FindProperty("stageCamera").objectReferenceValue = stageCamera;
        serialized.FindProperty("cameraRig").objectReferenceValue = rig.transform;
        serialized.FindProperty("fireLight").objectReferenceValue = fireLight;

        var parts = serialized.FindProperty("collapsingParts");
        parts.arraySize = collapsing.Length;
        for (var i = 0; i < collapsing.Length; i++)
            parts.GetArrayElementAtIndex(i).objectReferenceValue = collapsing[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();

        Undo.ClearAll();
        Directory.CreateDirectory(CutsceneSceneDirectory);
        EditorSceneManager.SaveScene(scene, PrologueScenePath);
        AssetDatabase.Refresh();

        RegisterInBuildSettings(PrologueScenePath);

        Debug.Log($"[DevSceneBuilder] Created {PrologueScenePath}. " +
                  "PrologueStage의 카메라·화염 광원·붕괴 파트가 인스펙터로 연결되어 있습니다. " +
                  "연출을 바꾸려면 이 씬을 열어 오브젝트를 옮기거나 PrologueStage.cs를 고치면 되고, " +
                  "대사와 자막은 CutsceneContext를 통해 기존 대사 계층을 그대로 씁니다.",
            stage);
    }

    /// <summary>
    /// 엔딩 컷씬 씬 (F-021 6.3). 복원된 숭례문을 보여주고 마무리 대사를 지난 뒤 최종 문구를 남긴다.
    /// 프롤로그와 같은 구성이되 분위기가 반대라 조명과 카메라 방향만 다르다.
    /// </summary>
    [MenuItem("IUM/Dev/Create Ending Cutscene Scene")]
    public static void CreateEndingCutsceneScene()
    {
        if (File.Exists(EndingScenePath) &&
            !EditorUtility.DisplayDialog(
                "Ending Cutscene Scene",
                $"{EndingScenePath} already exists. Overwrite it?",
                "Overwrite", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var defaultCamera = Camera.main;
        if (defaultCamera != null) Object.DestroyImmediate(defaultCamera.gameObject);

        // Daylight, where the prologue was a night fire.
        var sun = Object.FindAnyObjectByType<Light>();
        if (sun != null)
        {
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
        }

        var rig = new GameObject("CameraRig");
        rig.transform.position = new Vector3(0f, 2f, -8f);

        var cameraObject = new GameObject("CutsceneCamera", typeof(Camera));
        cameraObject.transform.SetParent(rig.transform, false);
        var stageCamera = cameraObject.GetComponent<Camera>();
        stageCamera.clearFlags = CameraClearFlags.Skybox;
        stageCamera.depth = 10f;
        cameraObject.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);

        CreateGround();

        // Placeholder 복원된 숭례문: upright where the prologue's version collapsed.
        var gate = new GameObject("Gate_Restored");
        CreateBox("Gate_Base", new Vector3(0f, 0.6f, 0f), new Vector3(7f, 1.2f, 2.5f))
            .transform.SetParent(gate.transform, true);

        for (var i = 0; i < 3; i++)
            CreateBox($"Beam_{i}", new Vector3(-2.2f + i * 2.2f, 2.4f, 0f), new Vector3(1.8f, 2.4f, 1.8f))
                .transform.SetParent(gate.transform, true);

        CreateBox("Roof", new Vector3(0f, 4f, 0f), new Vector3(8.4f, 0.8f, 3.4f))
            .transform.SetParent(gate.transform, true);

        var stageObject = new GameObject("EndingStage");
        var stage = stageObject.AddComponent<EndingStage>();

        var serialized = new SerializedObject(stage);
        serialized.FindProperty("stageCamera").objectReferenceValue = stageCamera;
        serialized.FindProperty("cameraRig").objectReferenceValue = rig.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Undo.ClearAll();
        Directory.CreateDirectory(CutsceneSceneDirectory);
        EditorSceneManager.SaveScene(scene, EndingScenePath);
        AssetDatabase.Refresh();

        RegisterInBuildSettings(EndingScenePath);

        Debug.Log($"[DevSceneBuilder] Created {EndingScenePath}. " +
                  "카메라가 물러나며 복원된 모습을 보여준 뒤 노장·이음이 대사와 최종 문구가 이어지고, " +
                  "cutscene.json의 nextScene에 따라 메인 화면으로 돌아갑니다. " +
                  "문서 6.5의 결과 표시는 표시할 공정이 아직 없어 EndingStage.ShowResults 자리로 남겨 두었습니다.",
            stage);
    }

    /// <summary>
    /// Adds the scene to Build Settings, which additive loading requires. Asks first: Build
    /// Settings is shared project configuration, not this tool's to change unprompted.
    /// </summary>
    static void RegisterInBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes;
        for (var i = 0; i < scenes.Length; i++)
            if (scenes[i].path == scenePath)
                return;

        if (!EditorUtility.DisplayDialog(
                "Build Settings",
                $"'{scenePath}' is not in Build Settings.\n\n" +
                "Additive loading fails without it. Add it now?",
                "Add", "Skip"))
        {
            Debug.LogWarning(
                $"[DevSceneBuilder] {scenePath} is not registered in Build Settings; " +
                "the cutscene will fail to load until it is added.");
            return;
        }

        var updated = new EditorBuildSettingsScene[scenes.Length + 1];
        scenes.CopyTo(updated, 0);
        updated[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
        EditorBuildSettings.scenes = updated;
    }

    static void CreateCutsceneView()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CutsceneUxmlPath);
        if (uxml == null)
        {
            Debug.LogError($"[DevSceneBuilder] {CutsceneUxmlPath} is missing; the cutscene view was skipped.");
            return;
        }

        var view = new GameObject("CutsceneView");
        var document = view.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;

        // Above ScreenFader's canvas (sortingOrder 5000) because a caption has to be readable while
        // the screen is blacked out — 프롤로그는 암전 상태에서 자막부터 띄운다 (F-003 3.3).
        document.panelSettings = LoadOrCreatePanelSettings(CutscenePanelPath, 6000);
        view.AddComponent<CutsceneView>();
    }

    /// <summary>
    /// 일시정지 메뉴 (F-019). Added to every development scene: pausing has to work during a
    /// cutscene as much as during ordinary play, and its panel therefore sits above the cutscene's.
    /// </summary>
    /// <param name="mainSceneName">
    /// 메인 화면으로 나가기가 향할 씬. Null keeps the component's own default, which is what the
    /// scenes that are not part of the flow test want.
    /// </param>
    static void CreatePauseMenu(string mainSceneName = null)
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PauseUxmlPath);
        if (uxml == null)
        {
            Debug.LogError($"[DevSceneBuilder] {PauseUxmlPath} is missing; the pause menu was skipped.");
            return;
        }

        var controller = new GameObject("PauseController");
        var pause = controller.AddComponent<PauseController>();

        if (!string.IsNullOrWhiteSpace(mainSceneName))
        {
            var serialized = new SerializedObject(pause);
            serialized.FindProperty("mainSceneName").stringValue = mainSceneName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var view = new GameObject("PauseMenuView");
        var document = view.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = LoadOrCreatePanelSettings(PausePanelPath, 7000);
        view.AddComponent<PauseMenuView>();
    }

    static Material LoadMaterial(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
            Debug.LogWarning($"[DevSceneBuilder] {path} is missing; that state material was left empty.");
        return material;
    }

    // Root stays unscaled so seats and snap points can use plain local offsets; the box is a child.
    static GameObject CreatePart(string name, Vector3 position, Vector3 visualSize, bool assembled)
    {
        var root = new GameObject(name);
        root.transform.position = position;

        var visual = CreateBox("Visual", position, visualSize);
        visual.transform.SetParent(root.transform, true);

        root.AddComponent<AssemblyPart>().isAssembled = assembled;
        return root;
    }

    static GameObject CreateGrabbablePart(
        string name, Vector3 position, Vector3 visualSize, string snapID, Vector3 snapLocalPosition)
    {
        var root = CreatePart(name, position, visualSize, false);

        var body = root.AddComponent<Rigidbody>();
        body.mass = 3f;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        root.AddComponent<Grabbable>();

        // The trigger has to sit on the same object as MaleSnapPoint: AssemblyTarget reads the
        // component off the collider it was hit by.
        var male = new GameObject("MaleSnap");
        male.transform.SetParent(root.transform, false);
        male.transform.localPosition = snapLocalPosition;

        var trigger = male.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.05f;

        male.AddComponent<MaleSnapPoint>().mySnapID = snapID;
        return root;
    }

    static void CreateSeat(
        Transform parent, string name, Vector3 localPosition,
        string acceptedPartID, string giveIDToChild, Material correct, Material wrong)
    {
        var seat = new GameObject(name);
        seat.transform.SetParent(parent, false);
        seat.transform.localPosition = localPosition;

        var trigger = seat.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(0.25f, 0.25f, 0.25f);

        // Tighter than the component defaults on purpose: the point of this scene is to see the
        // judgement pass and fail, not to have every release count as a hit.
        var component = seat.AddComponent<AssemblyTarget>();
        var serialized = new SerializedObject(component);
        serialized.FindProperty("acceptedPartID").stringValue = acceptedPartID;
        serialized.FindProperty("giveIDToChild").stringValue = giveIDToChild;
        serialized.FindProperty("positionTolerance").floatValue = 0.08f;
        serialized.FindProperty("rotationTolerance").floatValue = 15f;
        serialized.FindProperty("correctMaterial").objectReferenceValue = correct;
        serialized.FindProperty("wrongMaterial").objectReferenceValue = wrong;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateHud()
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath);
        if (uxml == null)
        {
            Debug.LogError($"[DevSceneBuilder] {HudUxmlPath} is missing; the HUD was skipped.");
            return;
        }

        var hud = new GameObject("IeumiHud");
        var document = hud.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = LoadOrCreatePanelSettings(HudPanelPath);
        hud.AddComponent<AiConversationHud>();
    }

    // A dedicated PanelSettings keeps each overlay's scaling independent from the start menu.
    // An existing asset is returned untouched: sortingOrder may have been tuned by hand since.
    static PanelSettings LoadOrCreatePanelSettings(string path, int sortingOrder = 0)
    {
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (settings != null) return settings;

        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        settings.sortingOrder = sortingOrder;

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        AssetDatabase.CreateAsset(settings, path);
        AssetDatabase.SaveAssets();
        return settings;
    }

    static void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
    }

    static GameObject CreateBox(string name, Vector3 position, Vector3 size)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        return box;
    }

    static void CreatePlayer()
    {
        var player = new GameObject("Player");
        player.transform.position = Vector3.zero;

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.7f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.85f, 0f);

        var head = new GameObject("Head");
        head.transform.SetParent(player.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var camera = head.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.nearClipPlane = 0.05f;
        head.AddComponent<AudioListener>();

        // Hand anchors are children of the root, not the head: XR device poses arrive in
        // tracking space, which is the root's local space.
        var leftHand = new GameObject("LeftHand");
        leftHand.transform.SetParent(player.transform, false);
        var rightHand = new GameObject("RightHand");
        rightHand.transform.SetParent(player.transform, false);

        var component = player.AddComponent<Player>();
        var serialized = new SerializedObject(component);
        serialized.FindProperty("head").objectReferenceValue = head.transform;
        serialized.FindProperty("leftHandAnchor").objectReferenceValue = leftHand.transform;
        serialized.FindProperty("rightHandAnchor").objectReferenceValue = rightHand.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateTool(string name, Vector3 position, Vector3 size)
    {
        var tool = CreateBox(name, position, size);

        var body = tool.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        tool.AddComponent<Grabbable>();
    }

    static void CreateSocket(string name, Vector3 position, Vector3 size)
    {
        var socket = new GameObject(name);
        socket.transform.position = position;

        var trigger = socket.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = size;

        var attachPoint = new GameObject("AttachPoint");
        attachPoint.transform.SetParent(socket.transform, false);

        var component = socket.AddComponent<GrabSocket>();
        var serialized = new SerializedObject(component);
        serialized.FindProperty("attachPoint").objectReferenceValue = attachPoint.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
