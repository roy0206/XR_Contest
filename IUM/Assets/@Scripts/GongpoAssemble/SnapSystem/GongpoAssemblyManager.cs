using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 씬 내의 모든 AssemblyTarget을 감시하여 전체 공포 조립이 완료되었는지 판별하는 매니저입니다.
/// 완료 시 OnMinigameComplete 이벤트를 발생시킵니다.
/// </summary>
public class GongpoAssemblyManager : MonoBehaviour
{
    [Header("Assembly Settings")]
    [Tooltip("체크하면 시작 시 씬에 있는 모든 AssemblyTarget을 찾아 총 부품 개수를 자동 설정합니다.")]
    public bool autoFindTargets = true;
    
    [Tooltip("자동 찾기를 사용하지 않을 경우, 직접 드래그 앤 드롭으로 채워넣을 타겟 리스트입니다.")]
    public List<AssemblyTarget> assemblyTargets = new List<AssemblyTarget>();

    [Header("Status (Read Only)")]
    [SerializeField, Tooltip("조립해야 할 총 부품 개수")]
    private int totalPartsNeeded;
    
    [SerializeField, Tooltip("현재 조립 완료된 부품 개수")]
    private int currentAssembledCount;

    [Header("Events")]
    [Tooltip("모든 부품 조립이 끝났을 때 발생합니다. (GongpoInstallationDirector.StartInstallation 등을 연결하세요)")]
    public UnityEvent OnMinigameComplete;

    private bool _isCompleted = false;

    private void Start()
    {
        if (autoFindTargets)
        {
            assemblyTargets.Clear();
            assemblyTargets.AddRange(FindObjectsByType<AssemblyTarget>(FindObjectsSortMode.None));
        }

        totalPartsNeeded = assemblyTargets.Count;
        currentAssembledCount = 0;

        foreach (var target in assemblyTargets)
        {
            if (target != null && target.Snap != null)
            {
                // 시작 시점에 이미 조립된(Occupied) 상태라면 카운트 즉시 증가
                if (target.Snap.IsOccupied)
                {
                    currentAssembledCount++;
                }
                else
                {
                    // 조립될 때마다 카운트가 오르도록 이벤트 구독
                    target.Snap.Assembled += HandlePartAssembled;
                }
            }
        }

        // 혹시 시작하자마자 이미 다 맞춰져 있는 경우를 대비해 체크
        CheckCompletion();
    }

    private void HandlePartAssembled(Grabbable part)
    {
        currentAssembledCount++;
        CheckCompletion();
    }

    private void CheckCompletion()
    {
        if (!_isCompleted && totalPartsNeeded > 0 && currentAssembledCount >= totalPartsNeeded)
        {
            _isCompleted = true;
            Debug.Log("🎉 공포 조립 미니게임 전체 완료!");
            OnMinigameComplete?.Invoke();
        }
    }
}
