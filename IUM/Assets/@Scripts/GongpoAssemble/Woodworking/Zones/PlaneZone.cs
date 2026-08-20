using UnityEngine;

public class PlaneZone : WorkZone
{
    [Header("대패질 설정")]
    [Tooltip("대패질 1회(스트로크)로 인정되기 위한 최소 이동 거리 (미터)")]
    public float minStrokeDistance = 0.3f;
    
    [Tooltip("작업을 100% 완료하기 위해 필요한 총 대패질 횟수")]
    public int requiredStrokes = 3;
    
    [Tooltip("대패질이 허용되는 정방향 (로컬 축 기준)")]
    public Vector3 planeDirection = Vector3.forward;
    
    [Tooltip("대패질 시 조절할 BlendShape 이름 (선택 사항)")]
    public string blendShapeName = "Planed";

    protected override void Start()
    {
        base.Start();
        requiredToolType = ToolType.HandPlane;
    }

    public bool IsStrokeSuccessful(float distance)
    {
        return distance >= minStrokeDistance;
    }

    public void AddStrokeProgress()
    {
        float progressAmount = 1.0f / requiredStrokes;
        AddProgress(progressAmount);
        Debug.Log($"[PlaneZone] 🪵 스트로크 성공! 현재 진행도: {progress * 100f:F1}%");
    }

    protected override void OnProgressUpdated(float currentProgress)
    {
        if (woodModifier != null && !string.IsNullOrEmpty(blendShapeName))
        {
            woodModifier.ApplyBlendShape(blendShapeName, currentProgress * 100f);
        }
        else if (woodModifier == null)
        {
            Debug.LogWarning($"[PlaneZone] 경고! woodModifier가 연결되지 않아 BlendShape를 변경할 수 없습니다! (진행도: {currentProgress * 100f:F1}%)");
        }
    }
}
