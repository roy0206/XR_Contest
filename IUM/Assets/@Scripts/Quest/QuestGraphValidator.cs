using System;
using System.Collections.Generic;

public static class QuestGraphValidator
{
    public static IReadOnlyList<string> Validate(QuestTable table)
    {
        var errors = new List<string>();
        if (table == null)
        {
            errors.Add("퀘스트 테이블이 null입니다.");
            return errors;
        }

        if (table.SchemaVersion <= 0) errors.Add("schemaVersion은 1 이상이어야 합니다.");
        if (table.Quests == null || table.Quests.Count == 0)
        {
            errors.Add("퀘스트가 하나도 없습니다.");
            return errors;
        }

        var questIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processes = new HashSet<ProcessId>();
        foreach (var quest in table.Quests)
        {
            if (quest == null)
            {
                errors.Add("null 퀘스트 항목이 있습니다.");
                continue;
            }

            var label = string.IsNullOrWhiteSpace(quest.Id) ? "<이름 없음>" : quest.Id;
            if (string.IsNullOrWhiteSpace(quest.Id)) errors.Add("ID가 없는 퀘스트가 있습니다.");
            else if (!questIds.Add(quest.Id)) errors.Add($"퀘스트 ID '{quest.Id}'가 중복됩니다.");
            if (string.IsNullOrWhiteSpace(quest.Title))
                errors.Add($"퀘스트 '{label}'에 인게임 표시 제목이 없습니다.");

            if (!processes.Add(quest.Process))
                errors.Add($"퀘스트 '{label}'의 ProcessId '{quest.Process}'가 다른 퀘스트와 중복됩니다.");

            ValidateQuest(quest, label, errors);
        }

        return errors;
    }

    static void ValidateQuest(QuestDefinition quest, string label, List<string> errors)
    {
        if (quest.Nodes == null || quest.Nodes.Count == 0)
        {
            errors.Add($"퀘스트 '{label}'에 노드가 없습니다.");
            return;
        }

        quest.Edges ??= new List<QuestEdgeData>();
        var nodes = new Dictionary<string, QuestNodeData>(StringComparer.OrdinalIgnoreCase);
        var outgoing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var completeCount = 0;
        var entryCount = 0;
        var objectiveCount = 0;

        foreach (var node in quest.Nodes)
        {
            if (node == null)
            {
                errors.Add($"퀘스트 '{label}'에 null 노드가 있습니다.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add($"퀘스트 '{label}'에 ID가 없는 노드가 있습니다.");
                continue;
            }

            if (!nodes.TryAdd(node.Id, node))
                errors.Add($"퀘스트 '{label}'의 노드 ID '{node.Id}'가 중복됩니다.");

            if (node.Kind == QuestNodeKind.Entry) entryCount++;
            if (node.Kind == QuestNodeKind.Complete) completeCount++;
            if (node.Kind == QuestNodeKind.Objective) objectiveCount++;
            if (node.Kind == QuestNodeKind.Objective && node.Objective == null)
                errors.Add($"퀘스트 '{label}'의 목표 노드 '{node.Id}'에 objective가 없습니다.");
            else if (node.Kind == QuestNodeKind.Objective)
            {
                if (string.IsNullOrWhiteSpace(node.Objective.Goal))
                    errors.Add($"퀘스트 '{label}'의 목표 노드 '{node.Id}'에 인게임 목표 문구가 없습니다.");
                if (string.IsNullOrWhiteSpace(node.ControlHint))
                    errors.Add($"퀘스트 '{label}'의 목표 노드 '{node.Id}'에 조작 힌트가 없습니다.");
            }

            outgoing.TryAdd(node.Id, new List<string>());
            incoming.TryAdd(node.Id, 0);
        }

        if (entryCount != 1) errors.Add($"퀘스트 '{label}'에는 Entry 노드가 정확히 하나 있어야 합니다.");
        if (objectiveCount == 0) errors.Add($"퀘스트 '{label}'에 목표 노드가 없습니다.");
        if (completeCount == 0) errors.Add($"퀘스트 '{label}'에 Complete 노드가 없습니다.");
        if (string.IsNullOrWhiteSpace(quest.EntryNode) || !nodes.TryGetValue(quest.EntryNode, out var entry))
            errors.Add($"퀘스트 '{label}'의 entryNode가 유효한 노드를 가리키지 않습니다.");
        else if (entry.Kind != QuestNodeKind.Entry)
            errors.Add($"퀘스트 '{label}'의 entryNode '{quest.EntryNode}'는 Entry 노드가 아닙니다.");

        var edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in quest.Edges)
        {
            if (edge == null || string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
            {
                errors.Add($"퀘스트 '{label}'에 양 끝이 지정되지 않은 연결선이 있습니다.");
                continue;
            }

            if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To))
            {
                errors.Add($"퀘스트 '{label}'의 연결 '{edge.From} → {edge.To}'가 없는 노드를 참조합니다.");
                continue;
            }

            if (!edgeKeys.Add($"{edge.From}\n{edge.To}"))
                errors.Add($"퀘스트 '{label}'의 연결 '{edge.From} → {edge.To}'가 중복됩니다.");

            outgoing[edge.From].Add(edge.To);
            incoming[edge.To]++;
        }

        foreach (var pair in nodes)
        {
            var node = pair.Value;
            var count = outgoing[pair.Key].Count;
            if (node.Kind == QuestNodeKind.Complete && count != 0)
                errors.Add($"퀘스트 '{label}'의 Complete 노드 '{node.Id}'에는 출력 연결을 만들 수 없습니다.");
            else if (node.Kind != QuestNodeKind.Complete && count != 1)
                errors.Add($"퀘스트 '{label}'의 노드 '{node.Id}'에는 출력 연결이 정확히 하나 있어야 합니다.");

            if (node.Kind == QuestNodeKind.Entry && incoming[pair.Key] != 0)
                errors.Add($"퀘스트 '{label}'의 Entry 노드 '{node.Id}'에는 입력 연결을 만들 수 없습니다.");
        }

        if (!string.IsNullOrWhiteSpace(quest.EntryNode) && nodes.ContainsKey(quest.EntryNode))
            ValidateReachabilityAndCycles(quest, label, nodes, outgoing, errors);
    }

    static void ValidateReachabilityAndCycles(
        QuestDefinition quest,
        string label,
        IReadOnlyDictionary<string, QuestNodeData> nodes,
        IReadOnlyDictionary<string, List<string>> outgoing,
        List<string> errors)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycleReported = false;

        void Visit(string id)
        {
            if (visiting.Contains(id))
            {
                if (!cycleReported)
                {
                    errors.Add($"퀘스트 '{label}'의 진행 경로에 순환이 있습니다.");
                    cycleReported = true;
                }

                return;
            }

            if (!visited.Add(id)) return;
            visiting.Add(id);
            if (outgoing.TryGetValue(id, out var next))
                foreach (var target in next)
                    Visit(target);
            visiting.Remove(id);
        }

        Visit(quest.EntryNode);
        foreach (var node in nodes.Keys)
            if (!visited.Contains(node))
                errors.Add($"퀘스트 '{label}'의 노드 '{node}'는 Entry에서 도달할 수 없습니다.");
    }
}
