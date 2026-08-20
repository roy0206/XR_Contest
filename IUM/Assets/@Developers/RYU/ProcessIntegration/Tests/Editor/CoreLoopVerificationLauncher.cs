using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace IUM.CoreLoopVerification.Tests
{
    /// <summary>
    /// Test Runner 창, 자동화 요청 파일, Unity 표준 -runTests 세 경로에서 같은 테스트 묶음을
    /// 실행하기 위한 얇은 실행기. 요청 파일은 이미 열린 Editor에서 무인 검증을 시작할 때 쓴다.
    /// </summary>
    [InitializeOnLoad]
    public static class CoreLoopVerificationLauncher
    {
        const string Category = "IUM.CoreLoop";
        const string RequestFileName = "IUMCoreLoopVerification.request";
        const string ResultFileName = "CoreLoopVerification.latest.log";

        static TestRunnerApi _runner;
        static VerificationCallbacks _callbacks;

        static CoreLoopVerificationLauncher() => EditorApplication.delayCall += RunIfRequested;

        [MenuItem("Tools/IUM/Verify Full Core Loop")]
        public static void RunFromMenu() => Run();

        static void RunIfRequested()
        {
            var requestPath = Path.Combine(ProjectRoot, "Temp", RequestFileName);
            if (!File.Exists(requestPath)) return;

            File.Delete(requestPath);
            Run();
        }

        static void Run()
        {
            if (_runner != null)
            {
                Debug.LogWarning("[CoreLoopVerification] 검증이 이미 실행 중입니다.");
                return;
            }

            _runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            _callbacks = new VerificationCallbacks(Finish);
            _runner.RegisterCallbacks(_callbacks);
            _runner.Execute(new ExecutionSettings(new Filter
            {
                categoryNames = new[] { Category },
                testMode = TestMode.EditMode
            }));
        }

        static void Finish(ITestResultAdaptor result)
        {
            var resultPath = Path.Combine(ProjectRoot, "Logs", ResultFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));

            var report = new StringBuilder();
            report.AppendLine($"finishedUtc: {DateTime.UtcNow:O}");
            report.AppendLine($"status: {result.TestStatus}");
            report.AppendLine($"passed: {result.PassCount}");
            report.AppendLine($"failed: {result.FailCount}");
            report.AppendLine($"skipped: {result.SkipCount}");
            report.AppendLine($"inconclusive: {result.InconclusiveCount}");
            report.AppendLine($"durationSeconds: {result.Duration:F3}");
            AppendFailures(result, report);
            File.WriteAllText(resultPath, report.ToString());

            Debug.Log($"[CoreLoopVerification] {result.TestStatus} " +
                      $"(pass {result.PassCount}, fail {result.FailCount})\n{resultPath}");

            _runner.UnregisterCallbacks(_callbacks);
            UnityEngine.Object.DestroyImmediate(_runner);
            _callbacks = null;
            _runner = null;
        }

        static void AppendFailures(ITestResultAdaptor result, StringBuilder report)
        {
            if (result.TestStatus == TestStatus.Failed && !result.HasChildren)
            {
                report.AppendLine();
                report.AppendLine($"FAILED: {result.FullName}");
                report.AppendLine(result.Message);
                if (!string.IsNullOrWhiteSpace(result.StackTrace)) report.AppendLine(result.StackTrace);
            }

            if (!result.HasChildren) return;
            foreach (var child in result.Children) AppendFailures(child, report);
        }

        static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();

        sealed class VerificationCallbacks : ICallbacks
        {
            readonly Action<ITestResultAdaptor> _finished;

            public VerificationCallbacks(Action<ITestResultAdaptor> finished) => _finished = finished;

            public void RunStarted(ITestAdaptor testsToRun) =>
                Debug.Log("[CoreLoopVerification] 전체 코어 루프 계약 검증을 시작합니다.");

            public void RunFinished(ITestResultAdaptor result) => _finished(result);
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed)
                    Debug.LogError($"[CoreLoopVerification] {result.FullName}: {result.Message}");
            }
        }
    }
}
