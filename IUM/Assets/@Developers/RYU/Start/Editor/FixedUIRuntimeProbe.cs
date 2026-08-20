#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UguiButton = UnityEngine.UI.Button;

/// <summary>FixedUI 시작 화면을 기기 없이 반복 검증하는 에디터 프로브.</summary>
[InitializeOnLoad]
static class FixedUIRuntimeProbe
{
    const string RunMarker = "Temp/FixedUIValidation/run.marker";
    const string ScreenshotPath = "Temp/FixedUIValidation/RuntimeGame.png";
    const string ReportPath = "Temp/FixedUIValidation/RuntimeReport.txt";

    static double _playStartedAt;
    static int _phase;
    static readonly StringBuilder Report = new();

    static FixedUIRuntimeProbe()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/IUM/Validation/Run FixedUI Start Screen Probe")]
    static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RunMarker));
        File.WriteAllText(RunMarker, "run", Encoding.UTF8);
        _playStartedAt = 0d;
        _phase = 0;
        Report.Clear();
    }

    static void Tick()
    {
        if (!File.Exists(RunMarker)) return;

        if (!EditorApplication.isPlaying)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.EnterPlaymode();
            return;
        }

        if (_playStartedAt <= 0d) _playStartedAt = EditorApplication.timeSinceStartup;
        var elapsed = EditorApplication.timeSinceStartup - _playStartedAt;

        if (_phase == 0 && elapsed >= 2d)
        {
            _phase = 1;
            ValidateBaseScreen();
            CaptureCamera(ScreenshotPath);
            InvokeOptionsThroughPlayer();
            return;
        }

        if (_phase == 1 && elapsed >= 4d)
        {
            _phase = 2;
            ValidateOptions();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, Report.ToString(), Encoding.UTF8);
            return;
        }

        if (_phase == 2 && elapsed >= 6d)
        {
            _phase = 3;
            File.Delete(RunMarker);
            EditorApplication.ExitPlaymode();
        }
    }

    static void ValidateBaseScreen()
    {
        var adapter = Object.FindFirstObjectByType<FixedUIStartMenuAdapter>();
        var startMenu = Object.FindFirstObjectByType<StartMenuController>();
        var playerBootstrap = Object.FindFirstObjectByType<StartScenePlayerBootstrap>();
        var player = Object.FindFirstObjectByType<Player>();
        var buttons = Object.FindObjectsByType<UguiButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        var missingScripts = roots.Sum(CountMissingScripts);

        Add("Adapter", adapter != null);
        Add("StartMenuController", startMenu != null);
        Add("PlayerBootstrap", playerBootstrap != null);
        Add("Player", player != null);
        Add("PlayerCharacterController", player != null && player.GetComponent<CharacterController>() != null);
        Add("DesktopInputFallback", new DesktopInputSource().IsAvailable);
        Add("MainCamera", Camera.main != null);
        Add("PlayerMainCamera", playerBootstrap != null &&
            playerBootstrap.ActiveCamera != null && playerBootstrap.ActiveCamera == Camera.main);
        Add("MissingScripts", missingScripts == 0, missingScripts.ToString());
        Add("Button_Continue", buttons.Any(button => button.name == "Button_Continue"));
        Add("Button_Options", buttons.Any(button => button.name == "Button_Options"));
        Add("Button_Restart", buttons.Any(button => button.name == "Button_Restart"));
        Add("Button_Main", buttons.Any(button => button.name == "Button_Main"));

        var canvas = adapter != null ? adapter.GetComponent<Canvas>() : null;
        Add("WorldSpaceEventCamera", canvas != null && canvas.worldCamera == Camera.main);
        Add("FixedUIPipeline", QualitySettings.renderPipeline != null &&
            QualitySettings.renderPipeline.name == "PC_RPAsset",
            QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : "null");

        var lightmaps = LightmapSettings.lightmaps;
        var validLightmaps = lightmaps != null && lightmaps.Length > 0 &&
            lightmaps.All(lightmap => lightmap.lightmapColor != null);
        Add("BakedLightmaps", validLightmaps,
            lightmaps != null ? lightmaps.Length.ToString() : "null");
    }

    static void CaptureCamera(string path)
    {
        var camera = Camera.main;
        if (camera == null) return;

        const int width = 1920;
        const int height = 1080;
        var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;

        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;

        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, texture.EncodeToPNG());

        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        Object.DestroyImmediate(texture);
        renderTexture.Release();
        Object.DestroyImmediate(renderTexture);
    }

    static int CountMissingScripts(GameObject gameObject)
    {
        var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        foreach (Transform child in gameObject.transform)
            count += CountMissingScripts(child.gameObject);
        return count;
    }

    static void InvokeOptionsThroughPlayer()
    {
        var options = Object.FindObjectsByType<UguiButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(button => button.name == "Button_Options");
        Add("OptionsPersistentListener", options != null && options.onClick.GetPersistentEventCount() == 1,
            options != null ? options.onClick.GetPersistentEventCount().ToString() : "button missing");

        var bootstrap = Object.FindFirstObjectByType<StartScenePlayerBootstrap>();
        var camera = bootstrap != null ? bootstrap.ActiveCamera : null;
        if (options == null || bootstrap == null || camera == null)
        {
            Add("PlayerButtonInteraction", false, "required object missing");
            return;
        }

        camera.transform.LookAt(options.transform.position);
        Add("PlayerButtonInteraction", bootstrap.TryActivateFocusedButton());
    }

    static void ValidateOptions()
    {
        var document = Object.FindFirstObjectByType<UIDocument>();
        var panel = document?.rootVisualElement.Q<VisualElement>("options-panel");
        Add("OptionsPanelOpened", panel != null && panel.resolvedStyle.display == DisplayStyle.Flex);
    }

    static void Add(string name, bool passed, string detail = null) =>
        Report.AppendLine($"{(passed ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? string.Empty : $" ({detail})")}");
}
#endif
