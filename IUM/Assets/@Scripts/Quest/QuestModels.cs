using System;
using System.Collections.Generic;

public enum QuestNodeKind
{
    Entry,
    Objective,
    Complete
}

[Serializable]
public sealed class QuestNodePosition
{
    public float X { get; set; }
    public float Y { get; set; }
}

[Serializable]
public sealed class QuestNodeData
{
    public string Id { get; set; }
    public QuestNodeKind Kind { get; set; }
    public QuestNodePosition Position { get; set; } = new();
    public ProcessStepData Objective { get; set; }
}

[Serializable]
public sealed class QuestEdgeData
{
    public string From { get; set; }
    public string To { get; set; }
}

[Serializable]
public sealed class QuestDefinition
{
    public string Id { get; set; }
    public ProcessId Process { get; set; }
    public string EntryNode { get; set; }
    public string IntroDialogue { get; set; }
    public string CompleteDialogue { get; set; }
    public ProcessGrade Grade { get; set; } = ProcessGrade.None;
    public List<QuestNodeData> Nodes { get; set; } = new();
    public List<QuestEdgeData> Edges { get; set; } = new();

    public bool HasGraph => Nodes != null && Nodes.Count > 0 && !string.IsNullOrWhiteSpace(EntryNode);

    public bool TryGetNode(string nodeId, out QuestNodeData node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(nodeId) || Nodes == null) return false;

        for (var i = 0; i < Nodes.Count; i++)
        {
            var candidate = Nodes[i];
            if (candidate == null || !string.Equals(candidate.Id, nodeId, StringComparison.OrdinalIgnoreCase))
                continue;

            node = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetNext(string nodeId, out QuestNodeData node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(nodeId) || Edges == null) return false;

        for (var i = 0; i < Edges.Count; i++)
        {
            var edge = Edges[i];
            if (edge == null || !string.Equals(edge.From, nodeId, StringComparison.OrdinalIgnoreCase))
                continue;

            return TryGetNode(edge.To, out node);
        }

        return false;
    }
}

[Serializable]
public sealed class QuestTable
{
    public int SchemaVersion { get; set; } = 1;
    public ProcessSettings Settings { get; set; } = new();
    public List<QuestDefinition> Quests { get; set; } = new();

    public static QuestTable CreateEmpty() => new();

    public bool TryGet(string questId, out QuestDefinition quest)
    {
        quest = null;
        if (string.IsNullOrWhiteSpace(questId) || Quests == null) return false;

        for (var i = 0; i < Quests.Count; i++)
        {
            var candidate = Quests[i];
            if (candidate == null || !string.Equals(candidate.Id, questId, StringComparison.OrdinalIgnoreCase))
                continue;

            quest = candidate;
            return quest.HasGraph;
        }

        return false;
    }

    public bool TryGet(ProcessId process, out QuestDefinition quest)
    {
        quest = null;
        if (Quests == null) return false;

        for (var i = 0; i < Quests.Count; i++)
        {
            var candidate = Quests[i];
            if (candidate == null || candidate.Process != process) continue;

            quest = candidate;
            return quest.HasGraph;
        }

        return false;
    }
}
