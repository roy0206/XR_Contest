using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class InstallChecker : MonoBehaviour
{
    [Header("장착 설정")]
    public Transform targetTransform; // 목표로 하는 스냅 위치 트랜스폼
    public float positionTolerance = 1.0f; // 장착 위치 오차 허용 범위
    public float rotationTolerance = 30f; // 장착 회전 오차 허용 범위

    private XRGrabInteractable grabInteractable;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        // 사용자가 잡았던 물건을 놓을 때(Select Exited) 판정 함수 실행
        grabInteractable.selectExited.AddListener(CheckInstallation);
    }

    private void CheckInstallation(SelectExitEventArgs arg)
    {
        // 1. 오차 계산
        float posError = Vector3.Distance(transform.position, targetTransform.position);
        float rotError = Quaternion.Angle(transform.rotation, targetTransform.rotation);

        // 2. 판정
        if (posError <= positionTolerance && rotError <= rotationTolerance)
        {
            InstallSuccess();
        }
        else
        {
            InstallFailed(posError, rotError);
        }
    }

    private void InstallSuccess()
    {
        Debug.Log("장착 성공!");
        // (선택) 목표 위치에 정확히 맞춤(Snap) 시키기
        transform.position = targetTransform.position;
        transform.rotation = targetTransform.rotation;

        // (선택) 물리 연산 끄고 더 이상 못 잡게 하기
        GetComponent<Rigidbody>().isKinematic = true;
        grabInteractable.enabled = false;

        // 여기에 성공 UI 호출 등을 추가할 수 있습니다.
    }

    private void InstallFailed(float posError, float rotError)
    {
        Debug.Log($"실패: 위치 오차 {posError:F2}, 회전 오차 {rotError:F2}");
        // 여기에 실패 메시지 팝업 UI 호출 등을 추가할 수 있습니다.
    }
}