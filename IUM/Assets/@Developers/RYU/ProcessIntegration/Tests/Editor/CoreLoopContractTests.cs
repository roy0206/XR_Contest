using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IUM.CoreLoopVerification.Tests
{
    /// <summary>
    /// 물리 공정 구현을 대신 실행하지 않고, 전체 게임 루프를 잇는 계약을 한 번에 검증한다.
    /// 검증 대상은 흐름·공정·대사·컷씬 데이터, Build Settings, 메인 씬 배선, 진행 상태 전이,
    /// MainPlayProcessBridge의 공정별 신호/접근 정책이다.
    ///
    /// 예상 루프는 CoreLoopVerificationProfile.json에 있으므로 다른 루프나 공정 구성을 검증할
    /// 때 테스트 코드를 복사하지 않고 프로필만 교체할 수 있다.
    /// </summary>
    [Category("IUM.CoreLoop")]
    public sealed class CoreLoopContractTests
    {
        const string ProfilePath =
            "Assets/@Developers/RYU/ProcessIntegration/Tests/CoreLoopVerificationProfile.json";
        const string FlowPath = "Assets/@AddressableAssets/Data/Static/flow.json";
        const string ProcessPath = "Assets/@AddressableAssets/Data/Static/process.json";
        const string DialoguePath = "Assets/@AddressableAssets/Data/Static/dialogue.json";
        const string CutscenePath = "Assets/@AddressableAssets/Data/Static/cutscene.json";
        const string TutorialScenePath = "Assets/@Developers/RYU/Scenes/Dev/TutorialScene.unity";

        static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        [Test]
        public void FullRoute_HasEveryDestinationAndBuildScene()
        {
            var profile = ReadJson<VerificationProfile>(ProfilePath);
            var flow = ReadJson<FlowFile>(FlowPath);
            var cutscenes = ReadJson<CutsceneFile>(CutscenePath);

            Assert.That(profile.route, Is.Not.Null.And.Not.Empty, "검증 프로필에 루프가 없습니다.");
            Assert.That(flow.entries, Is.Not.Null.And.Not.Empty, "flow.json에 목적지가 없습니다.");

            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
                .ToHashSet(Names);
            var flowByProcess = UniqueBy(flow.entries, entry => entry.process, FlowPath);
            var cutsceneById = UniqueBy(cutscenes.cutscenes, cutscene => cutscene.id, CutscenePath);

            foreach (var stage in profile.route)
            {
                Assert.That(flowByProcess.TryGetValue(stage.process, out var destination), Is.True,
                    $"flow.json에 '{stage.process}' 목적지가 없습니다.");

                if (Names.Equals(stage.destinationKind, "scene"))
                {
                    Assert.That(destination.scene, Is.EqualTo(stage.destination).IgnoreCase,
                        $"'{stage.process}' 씬 목적지가 프로필과 다릅니다.");
                    Assert.That(enabledScenes, Does.Contain(stage.destination),
                        $"씬 '{stage.destination}'이 Build Settings에서 활성화되지 않았습니다.");
                    continue;
                }

                Assert.That(stage.destinationKind, Is.EqualTo("cutscene").IgnoreCase,
                    $"'{stage.process}'의 destinationKind가 scene/cutscene이 아닙니다.");
                Assert.That(destination.cutscene, Is.EqualTo(stage.destination).IgnoreCase,
                    $"'{stage.process}' 컷씬 목적지가 프로필과 다릅니다.");
                Assert.That(cutsceneById.TryGetValue(stage.destination, out var cutscene), Is.True,
                    $"cutscene.json에 '{stage.destination}'가 없습니다.");

                if (!string.IsNullOrWhiteSpace(cutscene.scene))
                    Assert.That(enabledScenes, Does.Contain(cutscene.scene),
                        $"컷씬 씬 '{cutscene.scene}'이 Build Settings에서 활성화되지 않았습니다.");

                if (!string.IsNullOrWhiteSpace(cutscene.video) &&
                    !Uri.TryCreate(cutscene.video, UriKind.Absolute, out _))
                {
                    var videoPath = Path.Combine(Application.streamingAssetsPath, cutscene.video);
                    Assert.That(File.Exists(videoPath), Is.True,
                        $"컷씬 영상 '{cutscene.video}'를 StreamingAssets에서 찾지 못했습니다.");
                }

                if (!string.IsNullOrWhiteSpace(cutscene.nextScene))
                    Assert.That(enabledScenes, Does.Contain(cutscene.nextScene),
                        $"컷씬 다음 씬 '{cutscene.nextScene}'이 Build Settings에서 활성화되지 않았습니다.");
            }
        }

        [Test]
        public void ProcessDefinitions_HaveSignalsAndResolvableDialogue()
        {
            var profile = ReadJson<VerificationProfile>(ProfilePath);
            var processFile = ReadJson<ProcessFile>(ProcessPath);
            var dialogueFile = ReadJson<DialogueFile>(DialoguePath);
            var definitions = UniqueBy(processFile.processes, process => process.process, ProcessPath);
            var dialogueIds = UniqueBy(dialogueFile.sequences, sequence => sequence.id, DialoguePath);

            foreach (var stage in profile.route.Where(stage => stage.requiresProcessDefinition))
            {
                Assert.That(definitions.TryGetValue(stage.process, out var definition), Is.True,
                    $"process.json에 '{stage.process}' 정의가 없습니다.");
                Assert.That(definition.steps, Is.Not.Null.And.Not.Empty,
                    $"'{stage.process}'에 실행할 단계가 없습니다.");

                AssertDialogueExists(definition.introDialogue, dialogueIds, stage.process, "introDialogue");
                AssertDialogueExists(definition.completeDialogue, dialogueIds, stage.process, "completeDialogue");

                foreach (var step in definition.steps)
                {
                    Assert.That(step.id, Is.Not.Null.And.Not.Empty,
                        $"'{stage.process}'에 ID가 없는 단계가 있습니다.");
                    AssertDialogueExists(step.introDialogue, dialogueIds, stage.process, $"{step.id}.introDialogue");
                    AssertDialogueExists(step.retryDialogue, dialogueIds, stage.process, $"{step.id}.retryDialogue");
                    AssertDialogueExists(step.successDialogue, dialogueIds, stage.process, $"{step.id}.successDialogue");
                }

                if (string.IsNullOrWhiteSpace(stage.signal)) continue;

                var signalSteps = definition.steps
                    .Where(step => Names.Equals(step.condition, "signal"))
                    .ToArray();
                Assert.That(signalSteps, Has.Length.GreaterThan(0),
                    $"'{stage.process}'에 Signal 단계가 없습니다.");
                Assert.That(signalSteps.Any(step => Names.Equals(step.target, stage.signal)), Is.True,
                    $"'{stage.process}'의 신호 '{stage.signal}'을 process.json에서 사용하지 않습니다.");
                Assert.That(signalSteps.All(step => step.amount > 0f), Is.True,
                    $"'{stage.process}' Signal 단계의 amount는 0보다 커야 합니다.");

                var bridgeSignal = InvokeBridgeSignal(stage.process);
                Assert.That(bridgeSignal, Is.EqualTo(stage.signal),
                    $"MainPlayProcessBridge와 process.json의 '{stage.process}' 신호가 다릅니다.");
            }
        }

        [Test]
        public void ProgressModel_CompletesWholeRouteAndEndingCanReset()
        {
            var profile = ReadJson<VerificationProfile>(ProfilePath);
            var processId = RuntimeType("ProcessId");
            var gradeType = RuntimeType("ProcessGrade");
            var progressType = RuntimeType("UserProgressData");
            var progress = Activator.CreateInstance(progressType);
            var nextProcess = progressType.GetProperty("NextProcess");
            var complete = progressType.GetMethod("Complete", new[] { processId, gradeType });
            var reset = progressType.GetMethod("Reset", Type.EmptyTypes);

            Assert.That(nextProcess, Is.Not.Null);
            Assert.That(complete, Is.Not.Null);
            Assert.That(reset, Is.Not.Null);

            var stagesToComplete = profile.route.Take(profile.route.Length - 1).ToArray();
            var none = Enum.Parse(gradeType, "None", true);
            for (var i = 0; i < stagesToComplete.Length; i++)
            {
                var stage = stagesToComplete[i];
                Assert.That(nextProcess.GetValue(progress).ToString(), Is.EqualTo(stage.process).IgnoreCase,
                    $"'{stage.process}' 시작 전 진행 상태가 끊겼습니다.");

                var current = Enum.Parse(processId, stage.process, true);
                complete.Invoke(progress, new[] { current, none });
            }

            var ending = profile.route[^1].process;
            Assert.That(nextProcess.GetValue(progress).ToString(), Is.EqualTo(ending).IgnoreCase,
                "마지막 제작 공정을 완료해도 엔딩 상태에 도달하지 못했습니다.");

            reset.Invoke(progress, null);
            Assert.That(nextProcess.GetValue(progress).ToString(),
                Is.EqualTo(profile.route[0].process).IgnoreCase,
                "엔딩의 진행 초기화 뒤 첫 공정으로 돌아가지 못했습니다.");
        }

        [Test]
        public void MainPlaySignals_CanSatisfyEveryConfiguredProductionStep()
        {
            var profile = ReadJson<VerificationProfile>(ProfilePath);
            var processFile = ReadJson<ProcessFile>(ProcessPath);
            var definitions = UniqueBy(processFile.processes, process => process.process, ProcessPath);
            var signalBus = RuntimeType("ProcessSignalBus");
            var reset = signalBus.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static);
            var add = signalBus.GetMethod("Add", BindingFlags.Public | BindingFlags.Static);
            var read = signalBus.GetMethod("Read", BindingFlags.Public | BindingFlags.Static);

            Assert.That(reset, Is.Not.Null);
            Assert.That(add, Is.Not.Null);
            Assert.That(read, Is.Not.Null);

            foreach (var stage in profile.route.Where(stage => !string.IsNullOrWhiteSpace(stage.signal)))
            {
                Assert.That(definitions.TryGetValue(stage.process, out var definition), Is.True);
                var signalSteps = definition.steps
                    .Where(step => Names.Equals(step.condition, "signal") &&
                                   Names.Equals(step.target, stage.signal))
                    .ToArray();
                Assert.That(signalSteps, Is.Not.Empty);

                reset.Invoke(null, new object[] { stage.signal });
                Assert.That((float)read.Invoke(null, new object[] { stage.signal }), Is.EqualTo(0f));

                foreach (var step in signalSteps)
                    add.Invoke(null, new object[] { stage.signal, step.amount });

                var required = signalSteps.Sum(step => step.amount);
                var actual = (float)read.Invoke(null, new object[] { stage.signal });
                Assert.That(actual, Is.GreaterThanOrEqualTo(required),
                    $"'{stage.process}' 신호 요구량을 ProcessSignalBus에서 충족하지 못했습니다.");

                reset.Invoke(null, new object[] { stage.signal });
            }
        }

        [Test]
        public void MainPlayPolicy_RejectsPrematureAssemblyAccess()
        {
            var profile = ReadJson<VerificationProfile>(ProfilePath);
            var bridge = RuntimeType("MainPlayProcessBridge");
            var processId = RuntimeType("ProcessId");
            var policy = bridge.GetMethod("IsAssemblyPartAvailable", BindingFlags.Public | BindingFlags.Static);
            var purlinId = (string)bridge.GetField("PurlinPartId", BindingFlags.Public | BindingFlags.Static)
                ?.GetRawConstantValue();

            Assert.That(policy, Is.Not.Null);
            Assert.That(purlinId, Is.Not.Null.And.Not.Empty);

            foreach (var stage in profile.route)
            {
                var process = Enum.Parse(processId, stage.process, true);
                var allowsPurlin = (bool)policy.Invoke(null, new[] { process, purlinId, false });
                var allowsGongpo = (bool)policy.Invoke(null, new object[] { process, "gongpo-test-part", false });

                Assert.That(allowsPurlin, Is.EqualTo(Names.Equals(stage.process, "purlinInstall")),
                    $"'{stage.process}'의 도리 접근 정책이 잘못됐습니다.");
                Assert.That(allowsGongpo, Is.EqualTo(Names.Equals(stage.process, "gongpoPuzzle")),
                    $"'{stage.process}'의 공포 부재 접근 정책이 잘못됐습니다.");
            }

            var purlinProcess = Enum.Parse(processId, "PurlinInstall", true);
            var assembledPart = (bool)policy.Invoke(null, new[] { purlinProcess, purlinId, (object)true });
            Assert.That(assembledPart, Is.False, "이미 조립된 부재를 다시 잡을 수 있게 열면 안 됩니다.");
        }

        [Test]
        public void MainPlayScene_ContainsIntegrationComponentsAndRestartTarget()
        {
            var profile = ReadJson<VerificationProfile>(ProfilePath);
            Assert.That(File.Exists(Absolute(profile.mainPlaySceneAsset)), Is.True,
                $"메인 플레이 씬 '{profile.mainPlaySceneAsset}'이 없습니다.");

            var sceneText = File.ReadAllText(Absolute(profile.mainPlaySceneAsset));
            AssertSceneContainsScript(sceneText, "Assets/@Scripts/Process/ProcessRunner.cs");
            AssertSceneContainsScript(sceneText,
                "Assets/@Developers/RYU/ProcessIntegration/MainPlayProcessBridge.cs");
            AssertSceneContainsScript(sceneText, "Assets/@Scripts/Dialogue/InGameDialogue.cs");
            AssertSceneContainsScript(sceneText, "Assets/@Scripts/UI/PauseController.cs");

            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
                .ToHashSet(Names);
            Assert.That(enabledScenes, Does.Contain(profile.mainPlayScene),
                "공정 재시작 대상인 메인 플레이 씬이 Build Settings에서 활성화되지 않았습니다.");

            var runner = RuntimeType("ProcessRunner");
            Assert.That(runner.GetEvent("ProcessChanged"), Is.Not.Null,
                "연결 계층이 공정 진입을 동기화할 ProcessChanged 이벤트가 없습니다.");
        }

        [Test]
        public void TutorialOutlineGuide_CoversEveryObjectInteractionStep()
        {
            var sceneText = File.ReadAllText(Absolute(TutorialScenePath));
            AssertSceneContainsScript(sceneText, "Assets/@Scripts/Process/TutorialOutlineGuide.cs");
            AssertSceneContainsScript(sceneText, "Assets/QuickOutline/Scripts/Outline.cs");

            Assert.That(File.Exists(Absolute("Assets/QuickOutline/Resources/Materials/OutlineMask.mat")), Is.True);
            Assert.That(File.Exists(Absolute("Assets/QuickOutline/Resources/Materials/OutlineFill.mat")), Is.True);

            var processFile = ReadJson<ProcessFile>(ProcessPath);
            var tutorial = processFile.processes.Single(process => Names.Equals(process.process, "tutorial"));
            var point = tutorial.steps.Single(step => Names.Equals(step.condition, "point"));
            var grab = tutorial.steps.Single(step => Names.Equals(step.condition, "grab"));
            var place = tutorial.steps.Single(step => Names.Equals(step.condition, "place"));

            Assert.That(point.target, Is.EqualTo("tool_saw"));
            Assert.That(grab.target, Is.EqualTo("tool_saw"));
            Assert.That(place.target, Is.EqualTo("socket_bench"));
            Assert.That(place.unlock, Does.Contain("tool_saw"));
        }

        static string InvokeBridgeSignal(string processName)
        {
            var bridge = RuntimeType("MainPlayProcessBridge");
            var processId = RuntimeType("ProcessId");
            var method = bridge.GetMethod("SignalForProcess", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new[] { Enum.Parse(processId, processName, true) });
        }

        static void AssertSceneContainsScript(string sceneText, string scriptPath)
        {
            var metaPath = Absolute(scriptPath + ".meta");
            Assert.That(File.Exists(metaPath), Is.True, $"'{scriptPath}.meta'가 없습니다.");

            var guidLine = File.ReadLines(metaPath)
                .FirstOrDefault(line => line.StartsWith("guid: ", StringComparison.Ordinal));
            Assert.That(guidLine, Is.Not.Null, $"'{scriptPath}.meta'에서 GUID를 찾지 못했습니다.");

            var guid = guidLine.Substring("guid: ".Length).Trim();
            Assert.That(sceneText, Does.Contain($"guid: {guid}"),
                $"메인 플레이 씬에 '{scriptPath}' 컴포넌트가 없습니다.");
        }

        static void AssertDialogueExists(
            string dialogueId,
            IReadOnlyDictionary<string, DialogueSequence> dialogueIds,
            string process,
            string field)
        {
            if (string.IsNullOrWhiteSpace(dialogueId)) return;
            Assert.That(dialogueIds.ContainsKey(dialogueId), Is.True,
                $"'{process}.{field}'가 존재하지 않는 대사 '{dialogueId}'를 참조합니다.");
        }

        static Dictionary<string, T> UniqueBy<T>(IEnumerable<T> items, Func<T, string> key, string source)
        {
            Assert.That(items, Is.Not.Null, $"'{source}'의 배열이 null입니다.");
            var result = new Dictionary<string, T>(Names);
            foreach (var item in items)
            {
                Assert.That(item, Is.Not.Null, $"'{source}' 배열에 null 항목이 있습니다.");
                var value = key(item);
                Assert.That(value, Is.Not.Null.And.Not.Empty, $"'{source}'에 키가 없는 항목이 있습니다.");
                Assert.That(result.TryAdd(value, item), Is.True,
                    $"'{source}'에 중복 키 '{value}'가 있습니다.");
            }

            return result;
        }

        static Type RuntimeType(string name)
        {
            var type = Type.GetType($"{name}, Assembly-CSharp");
            Assert.That(type, Is.Not.Null, $"런타임 타입 '{name}'을 찾지 못했습니다.");
            return type;
        }

        static T ReadJson<T>(string assetPath)
        {
            var path = Absolute(assetPath);
            Assert.That(File.Exists(path), Is.True, $"검증 입력 '{assetPath}'이 없습니다.");
            var result = JsonUtility.FromJson<T>(File.ReadAllText(path));
            Assert.That(result, Is.Not.Null, $"검증 입력 '{assetPath}'을 읽지 못했습니다.");
            return result;
        }

        static string Absolute(string assetPath) =>
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, assetPath);

        [Serializable]
        sealed class VerificationProfile
        {
            public string name;
            public string mainPlayScene;
            public string mainPlaySceneAsset;
            public VerificationStage[] route;
        }

        [Serializable]
        sealed class VerificationStage
        {
            public string process;
            public string destinationKind;
            public string destination;
            public bool requiresProcessDefinition;
            public string signal;
        }

        [Serializable]
        sealed class FlowFile { public FlowEntry[] entries; }

        [Serializable]
        sealed class FlowEntry
        {
            public string process;
            public string scene;
            public string cutscene;
        }

        [Serializable]
        sealed class ProcessFile { public ProcessDefinition[] processes; }

        [Serializable]
        sealed class ProcessDefinition
        {
            public string process;
            public string introDialogue;
            public string completeDialogue;
            public ProcessStep[] steps;
        }

        [Serializable]
        sealed class ProcessStep
        {
            public string id;
            public string condition;
            public string target;
            public string[] unlock;
            public float amount = 1f;
            public string introDialogue;
            public string retryDialogue;
            public string successDialogue;
        }

        [Serializable]
        sealed class DialogueFile { public DialogueSequence[] sequences; }

        [Serializable]
        sealed class DialogueSequence { public string id; }

        [Serializable]
        sealed class CutsceneFile { public CutsceneDefinition[] cutscenes; }

        [Serializable]
        sealed class CutsceneDefinition
        {
            public string id;
            public string scene;
            public string video;
            public string nextScene;
        }
    }
}
