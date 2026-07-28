using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Builds the desktop test scenes from code so no shared scene has to be edited by hand.
/// Menu: IUM > Dev > Create Interaction Test Scene, IUM > Dev > Create AI Voice Test Scene,
/// IUM > Dev > Create Gongpo Test Scene.
/// </summary>
public static class DevSceneBuilder
{
    const string SceneDirectory = "Assets/@Scenes/Dev";
    const string ScenePath = SceneDirectory + "/InteractionTest.unity";
    const string VoiceScenePath = SceneDirectory + "/AiVoiceTest.unity";
    const string GongpoScenePath = SceneDirectory + "/GongpoTest.unity";
    const string HudUxmlPath = "Assets/@UI/Ai/IeumiHud.uxml";
    const string HudPanelPath = "Assets/@UI/Ai/IeumiHudPanelSettings.asset";
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
        document.panelSettings = LoadOrCreatePanelSettings();
        hud.AddComponent<AiConversationHud>();
    }

    // A dedicated PanelSettings keeps HUD scaling independent from the start menu.
    static PanelSettings LoadOrCreatePanelSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(HudPanelPath);
        if (settings != null) return settings;

        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

        Directory.CreateDirectory(Path.GetDirectoryName(HudPanelPath));
        AssetDatabase.CreateAsset(settings, HudPanelPath);
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
