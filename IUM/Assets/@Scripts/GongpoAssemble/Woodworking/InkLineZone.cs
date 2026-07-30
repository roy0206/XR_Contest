using UnityEngine;

public class InkLineZone : WorkZone
{
    protected override void Start()
    {
        base.Start();
        requiredToolType = ToolType.InkLine;
    }

    public void OnInkLineSnapped(Vector3 startPoint, Vector3 endPoint)
    {
        if (woodModifier != null)
        {
            woodModifier.CreateInkLine(startPoint, endPoint);
            CompleteWork();
        }
    }

    public Vector3 GetSurfacePoint(Vector3 toolPos)
    {
        if (woodModifier != null) 
        {
            return woodModifier.GetClosestSurfacePoint(toolPos);
        }
        return toolPos;
    }
}
