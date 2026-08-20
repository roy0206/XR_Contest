using UnityEngine;

/// <summary>
/// WorkZone에서 발생하는 이벤트(먹줄, 대패질, 톱질 등)를 받아
/// 실제 Mesh 교체나 BlendShape, 데칼 등을 처리하는 클래스
/// </summary>
public class VisualWoodModifier : MonoBehaviour
{
    [Header("시각적 에셋 세팅")]
    [Tooltip("메쉬 렌더러가 포함된 원본 모델")]
    public SkinnedMeshRenderer targetSkinnedMesh;
    public MeshRenderer targetMeshRenderer;

    [Tooltip("톱질 완료 시 사라질 Waste 오브젝트")]
    public GameObject wasteObject;

    [Header("먹줄 데칼 설정")]
    public GameObject inkLineDecalPrefab;

    public void ApplyBlendShape(string blendShapeName, float weight)
    {
        if (targetSkinnedMesh != null)
        {
            int index = targetSkinnedMesh.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (index != -1)
            {
                targetSkinnedMesh.SetBlendShapeWeight(index, weight);
                // 중복 로그 방지를 위해 주석 처리 (PlaneZone에서 이미 스트로크 완료 로그를 띄움)
                // Debug.Log($"[VisualWoodModifier] BlendShape '{blendShapeName}' 적용됨. 수치: {weight:F1}");
            }
            else
            {
                Debug.LogError($"[VisualWoodModifier] 에러! '{blendShapeName}'라는 이름의 BlendShape를 SkinnedMesh에서 찾을 수 없습니다!");
            }
        }
        else
        {
            Debug.LogError("[VisualWoodModifier] 에러! Target Skinned Mesh가 할당되지 않았습니다!");
        }
    }

    /// <summary>
    /// 허공에 찍은 좌표를 실제 목재 콜라이더의 가장 가까운 표면(Surface)으로 보정하여 반환합니다.
    /// </summary>
    public Vector3 GetClosestSurfacePoint(Vector3 point)
    {
        Collider woodCollider = GetComponent<Collider>();
        if (woodCollider != null)
        {
            // 목재 본체에 붙어있는 콜라이더 표면의 가장 가까운 점을 찾습니다.
            return woodCollider.ClosestPoint(point);
        }
        
        // 콜라이더가 없다면 그냥 원본 좌표 반환
        return point;
    }

    public void SwapMesh(Mesh newMesh)
    {
        if (targetMeshRenderer != null && targetMeshRenderer.GetComponent<MeshFilter>() != null)
        {
            targetMeshRenderer.GetComponent<MeshFilter>().mesh = newMesh;
        }
        else if (targetSkinnedMesh != null)
        {
            targetSkinnedMesh.sharedMesh = newMesh;
        }
    }

    public void CreateInkLine(Vector3 startPoint, Vector3 endPoint)
    {
        if (inkLineDecalPrefab != null)
        {
            GameObject lineObj = Instantiate(inkLineDecalPrefab, transform);
            Debug.Log($"[VisualWoodModifier] 먹줄 프리팹 생성 완료! (부모: {transform.name})");

            LineRenderer lr = lineObj.GetComponent<LineRenderer>();
            
            if (lr != null)
            {
                // 핵심: LineRenderer가 부모(목재)의 위치값과 중복 합산되지 않도록 월드 스페이스 사용 강제
                lr.useWorldSpace = true;
                
                // 오브젝트의 실제 트랜스폼 위치는 원점으로 초기화 (이중 오프셋 방지)
                lineObj.transform.localPosition = Vector3.zero;
                lineObj.transform.localRotation = Quaternion.identity;

                lr.SetPosition(0, startPoint);
                lr.SetPosition(1, endPoint);
            }
            else
            {
                // 데칼 형태라면 회전 및 크기 조절
                // 스케일 중복 적용을 막기 위해 잠시 부모를 해제했다가 월드 스케일 세팅 후 다시 자식으로 편입
                lineObj.transform.SetParent(null);
                
                lineObj.transform.position = (startPoint + endPoint) / 2f;
                lineObj.transform.rotation = Quaternion.LookRotation(endPoint - startPoint, transform.up);
                float distance = Vector3.Distance(startPoint, endPoint);
                lineObj.transform.localScale = new Vector3(0.01f, 0.01f, distance); // Z축으로 길게 늘림
                
                lineObj.transform.SetParent(transform, true);
            }
        }
        else
        {
            Debug.LogError("[VisualWoodModifier] 먹줄을 그릴 데칼 프리팹(Ink Line Decal Prefab)이 할당되지 않았습니다! 인스펙터를 확인해주세요.");
        }
    }

    /// <summary>
    /// 톱질 완료 시 Waste(버려지는 부분)를 보이지 않게 처리
    /// </summary>
    public void HideWaste()
    {
        if (wasteObject != null)
        {
            wasteObject.SetActive(false);
            Debug.Log("[VisualWoodModifier] Waste 부분을 성공적으로 숨겼습니다.");
        }
    }
}
