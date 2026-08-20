using UnityEngine;

public enum ToolType
{
    InkLine,
    HandPlane,
    Saw,
    Chisel
}

public interface IWoodTool
{
    ToolType GetToolType();
    bool IsActive();
}
