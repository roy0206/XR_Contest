using UnityEngine;

public class AssemblyPart : MonoBehaviour
{
    [Header("현재 부품 조립 상태")]
    [Tooltip("기단부처럼 처음부터 조립된 부품은 체크해두고, 나머지는 비워둡니다.")]
    public bool isAssembled = false;

    // 조립 성공 시, 부모(결합 타겟)로부터 "새로운 ID"를 넘겨받습니다!
    public void OnAssembled(string inheritedID)
    {
        isAssembled = true;

        // 만약 부모 타겟이 새로운 역할을 부여했다면?
        if (!string.IsNullOrEmpty(inheritedID))
        {
            // 내 아래 달려 있는 자식 결합 타겟들을 모두 찾아서
            SequenceAssemblyTarget[] myTargets = GetComponentsInChildren<SequenceAssemblyTarget>();

            foreach (var target in myTargets)
            {
                // 실시간으로 자식들이 받을 새로운 결합 ID로 바꿔줍니다.
                target.acceptedPartID = inheritedID;
            }
        }
    }
}