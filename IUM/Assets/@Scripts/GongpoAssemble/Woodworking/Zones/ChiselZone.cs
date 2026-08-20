using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class ChiselZone : WorkZone
{
    [Header("끌질 설정")]
    [Tooltip("목표 깊이 (이 수치만큼 누적되면 완료)")]
    public float targetDepth = 5.0f;
    
    [Tooltip("수정할 BlendShape 이름")]
    public string blendShapeName = "Chiseled";

    private float accumulatedDepth = 0f;
    private BoxCollider zoneCollider;

    protected override void Start()
    {
        base.Start();
        requiredToolType = ToolType.Chisel;
        zoneCollider = GetComponent<BoxCollider>();
    }

    public BoxCollider GetCollider()
    {
        return zoneCollider;
    }

    public void AddHitProgress(float depth)
    {
        accumulatedDepth += depth;
        float progressAmount = depth / targetDepth;
        AddProgress(progressAmount);
        
        Debug.Log($"[ChiselZone] 🔨 유효 타격! 누적 깊이: {accumulatedDepth:F2}/{targetDepth:F2}");
    }

    protected override void OnProgressUpdated(float currentProgress)
    {
        if (woodModifier != null && !string.IsNullOrEmpty(blendShapeName))
        {
            woodModifier.ApplyBlendShape(blendShapeName, currentProgress * 100f);
        }
    }

    protected override void CompleteWork()
    {
        // 채점 (끌질은 위치가 정확했으므로 1.0)
        base.CompleteWork(new WorkResult { qualityScore = 1.0f, toolName = requiredToolType.ToString() });
    }
}
