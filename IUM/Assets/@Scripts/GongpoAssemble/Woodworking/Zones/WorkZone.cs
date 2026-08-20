using UnityEngine;
using UnityEngine.Events;
using System;

[System.Serializable]
public struct WorkResult
{
    public float qualityScore; // 0.0 ~ 1.0
    public string toolName;
}

[RequireComponent(typeof(Collider))]
public abstract class WorkZone : MonoBehaviour
{
    [Header("허용되는 도구 타입")]
    public ToolType requiredToolType;

    [Header("현재 작업 진행도 (0.0 ~ 1.0)")]
    [Range(0f, 1f)]
    public float progress = 0f;

    [Header("작업 대상 목재의 시각 변형 컴포넌트")]
    public VisualWoodModifier woodModifier;

    [Header("작업 완료 이벤트")]
    public UnityEvent OnWorkCompleted;
    public event Action<WorkResult> OnWorkCompletedEvent;

    protected bool isCompleted = false;
    public bool IsCompleted => isCompleted;

    protected virtual void Start()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (woodModifier == null)
        {
            woodModifier = GetComponentInParent<VisualWoodModifier>();
        }
    }

    /// <summary>
    /// 도구가 작업을 진행시킬 때 호출하는 메서드
    /// </summary>
    public virtual void AddProgress(float amount)
    {
        if (isCompleted) return;

        progress += amount;
        progress = Mathf.Clamp01(progress);

        OnProgressUpdated(progress);

        if (progress >= 1f)
        {
            CompleteWork();
        }
    }

    protected virtual void OnProgressUpdated(float currentProgress)
    {
        // 자식 클래스에서 필요 시 오버라이드 (예: BlendShape 업데이트 호출)
    }

    protected virtual void CompleteWork()
    {
        CompleteWork(new WorkResult { qualityScore = 1.0f, toolName = requiredToolType.ToString() });
    }

    protected virtual void CompleteWork(WorkResult result)
    {
        if (isCompleted) return;
        
        isCompleted = true;
        OnWorkCompleted?.Invoke();
        OnWorkCompletedEvent?.Invoke(result);
    }
}
