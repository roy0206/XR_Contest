using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>
/// One-click setup for on-device speech recognition.
///
/// Installing touches two shared assets — Packages/manifest.json and the scripting define
/// symbols in ProjectSettings — so it is deliberately a manual menu action instead of anything
/// automatic (Unity_커밋 규칙). Until it is run, the project builds without the package and
/// <see cref="LocalSpeechToTextService"/> compiles to a stub.
/// </summary>
public static class AiLocalSttSetup
{
    const string PackageUrl = "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm";
    const string PackageName = "com.eitan.sherpa-onnx-unity";
    const string Define = "IUM_SHERPA_ONNX";
    const string ModelId = "sherpa-onnx-zipformer-korean-2024-06-24";

    static readonly NamedBuildTarget[] Targets =
    {
        NamedBuildTarget.Standalone,
        NamedBuildTarget.Android
    };

    static AddRequest _addRequest;
    static RemoveRequest _removeRequest;

    [MenuItem("IUM/AI/Install Local STT (sherpa-onnx)")]
    public static void Install()
    {
        if (_addRequest != null || _removeRequest != null)
        {
            EditorUtility.DisplayDialog("로컬 STT", "이미 패키지 작업이 진행 중입니다.", "확인");
            return;
        }

        var proceed = EditorUtility.DisplayDialog(
            "로컬 STT 설치",
            "다음 두 가지 공용 설정을 변경합니다.\n\n" +
            $"1. Packages/manifest.json 에 {PackageName} 추가\n" +
            $"2. Standalone·Android 스크립팅 심볼에 {Define} 추가\n\n" +
            "설치 후 패키지의 Model Manager 에서 한국어 모델을 내려받아야 합니다.",
            "설치", "취소");

        if (!proceed) return;

        _addRequest = Client.Add(PackageUrl);
        EditorApplication.update += PollAdd;
        Debug.Log($"[AI] Installing {PackageName}…");
    }

    [MenuItem("IUM/AI/Remove Local STT")]
    public static void Uninstall()
    {
        if (_addRequest != null || _removeRequest != null) return;

        if (!EditorUtility.DisplayDialog(
                "로컬 STT 제거",
                $"{PackageName} 패키지와 {Define} 심볼을 제거합니다.",
                "제거", "취소"))
            return;

        // The define goes first so scripts stop referencing the package before it disappears.
        SetDefine(false);
        _removeRequest = Client.Remove(PackageName);
        EditorApplication.update += PollRemove;
    }

    [MenuItem("IUM/AI/Remove Local STT", true)]
    static bool ValidateUninstall() => HasDefine();

    static void PollAdd()
    {
        if (_addRequest == null || !_addRequest.IsCompleted) return;

        EditorApplication.update -= PollAdd;
        var request = _addRequest;
        _addRequest = null;

        if (request.Status != StatusCode.Success)
        {
            Debug.LogError($"[AI] Package install failed: {request.Error?.message}");
            EditorUtility.DisplayDialog("로컬 STT 설치 실패", request.Error?.message ?? "알 수 없는 오류", "확인");
            return;
        }

        SetDefine(true);
        Debug.Log($"[AI] {request.Result.packageId} installed and {Define} defined.");

        EditorUtility.DisplayDialog(
            "로컬 STT 설치 완료",
            "다음 순서로 마무리하십시오.\n\n" +
            $"1. 패키지의 Model Manager 에서 '{ModelId}' 다운로드\n" +
            "2. Android 빌드는 arm64-v8a 로 설정\n" +
            "3. ai_config.json 의 stt.provider 를 local 또는 auto 로 유지\n\n" +
            "모델 파일은 커밋하지 않습니다. 팀원은 각자 1회 내려받으면 됩니다.",
            "확인");
    }

    static void PollRemove()
    {
        if (_removeRequest == null || !_removeRequest.IsCompleted) return;

        EditorApplication.update -= PollRemove;
        var request = _removeRequest;
        _removeRequest = null;

        if (request.Status != StatusCode.Success)
            Debug.LogError($"[AI] Package removal failed: {request.Error?.message}");
        else
            Debug.Log($"[AI] {PackageName} removed.");
    }

    static bool HasDefine()
    {
        var symbols = PlayerSettings.GetScriptingDefineSymbols(Targets[0]);
        return Array.IndexOf(symbols.Split(';'), Define) >= 0;
    }

    static void SetDefine(bool enabled)
    {
        foreach (var target in Targets)
        {
            var symbols = new List<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(';'));
            symbols.RemoveAll(string.IsNullOrWhiteSpace);

            var contains = symbols.Contains(Define);
            if (enabled == contains) continue;

            if (enabled) symbols.Add(Define);
            else symbols.Remove(Define);

            PlayerSettings.SetScriptingDefineSymbols(target, symbols.ToArray());
        }
    }
}
