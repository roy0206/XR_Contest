using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class SequenceAssemblyTarget : MonoBehaviour
{
    [Header("받을 부품의 ID")]
    [Tooltip("MaleSnapPoint의 mySnapID와 스펠링이 일치해야 함")]
    public string acceptedPartID = "Untagged";

    [Header("자식에게 물려줄 자리 ID")]
    [Tooltip("이곳에 부품이 조립되면, 아래로 연결될 부품들에게 이 ID를 물려줌")]
    public string giveIDToChild = "";

    [Header("판정 오차 범위")]
    public float positionTolerance = 1f;
    public float rotationTolerance = 20f;

    [Header("상태별 머티리얼")]
    public Material correctMaterial;
    public Material wrongMaterial;

    [Range(0f, 1f)] public float hapticIntensity = 0.5f;
    public float hapticDuration = 0.1f;

    private Transform currentMaleSnapPoint;
    private XRGrabInteractable parentInteractable;
    private MeshRenderer parentVisual;
    private Material normalMaterial;

    private bool isHovering = false;
    private bool isOccupied = false;
    private Material currentAppliedMaterial;

    // 내가 속한 부모 부품 (기단부 또는 기둥 본체)
    private AssemblyPart ownerPart;

    private void Start()
    {
        ownerPart = GetComponentInParent<AssemblyPart>();

        if (ownerPart == null)
        {
            Debug.LogWarning($"{gameObject.name}의 부모 객체에 AssemblyPart 스크립트가 없습니다!");
        }
    }

    private void Update()
    {
        // 부모 부품이 아직 조립 안 됐으면 작동 안 함
        if (ownerPart == null || !ownerPart.isAssembled || isOccupied || !isHovering || currentMaleSnapPoint == null) return;

        if (parentInteractable != null && parentInteractable.isSelected)
        {
            float posError = Vector3.Distance(currentMaleSnapPoint.position, transform.position);
            float rotError = Quaternion.Angle(currentMaleSnapPoint.rotation, transform.rotation);

            if (posError <= positionTolerance && rotError <= rotationTolerance)
            {
                ChangeMaterial(correctMaterial);
            }
            else
            {
                ChangeMaterial(wrongMaterial);
            }
        }
        else
        {
            ChangeMaterial(normalMaterial);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 부모 부품이 조립 안 됐거나, 이미 자리가 찼으면 안 됨
        if (ownerPart == null || !ownerPart.isAssembled || isOccupied || isHovering) return;

        // 영역에 들어온 부품의 ID 확인
        MaleSnapPoint incomingMale = other.GetComponent<MaleSnapPoint>();

        if (incomingMale != null && incomingMale.mySnapID == acceptedPartID)
        {
            currentMaleSnapPoint = incomingMale.transform;
            parentInteractable = currentMaleSnapPoint.GetComponentInParent<XRGrabInteractable>();
            parentVisual = currentMaleSnapPoint.GetComponentInParent<MeshRenderer>();

            if (parentVisual != null)
            {
                normalMaterial = parentVisual.material;
            }

            if (parentInteractable != null)
            {
                parentInteractable.selectExited.AddListener(CheckInstallation);
            }

            isHovering = true;
            TriggerHapticFeedback();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isHovering && other.transform == currentMaleSnapPoint)
        {
            isHovering = false;
            ChangeMaterial(normalMaterial);

            if (parentInteractable != null)
            {
                parentInteractable.selectExited.RemoveListener(CheckInstallation);
            }

            currentMaleSnapPoint = null;
            parentInteractable = null;
            parentVisual = null;
        }
    }

    private void ChangeMaterial(Material newMat)
    {
        if (parentVisual != null && newMat != null && currentAppliedMaterial != newMat)
        {
            parentVisual.material = newMat;
            currentAppliedMaterial = newMat;
        }
    }

    private void TriggerHapticFeedback()
    {
        if (parentInteractable != null && parentInteractable.isSelected)
        {
            IXRSelectInteractor interactor = parentInteractable.firstInteractorSelecting;
            if (interactor is XRBaseInputInteractor controllerInteractor)
            {
                controllerInteractor.SendHapticImpulse(hapticIntensity, hapticDuration);
            }
        }
    }

    private void CheckInstallation(SelectExitEventArgs arg)
    {
        if (ownerPart == null || !ownerPart.isAssembled || isOccupied || !isHovering || currentMaleSnapPoint == null) return;

        float posError = Vector3.Distance(currentMaleSnapPoint.position, transform.position);
        float rotError = Quaternion.Angle(currentMaleSnapPoint.rotation, transform.rotation);

        if (posError <= positionTolerance && rotError <= rotationTolerance)
        {
            InstallSuccess();
        }
    }

    private void InstallSuccess()
    {
        isOccupied = true;
        ChangeMaterial(normalMaterial);

        Transform partRoot = parentInteractable.transform;

        Quaternion rotationOffset = Quaternion.Inverse(partRoot.rotation) * currentMaleSnapPoint.rotation;
        partRoot.rotation = transform.rotation * Quaternion.Inverse(rotationOffset);

        Vector3 positionOffset = currentMaleSnapPoint.position - partRoot.position;
        partRoot.position = transform.position - positionOffset;

        Rigidbody rb = partRoot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        parentInteractable.enabled = false;
        partRoot.SetParent(transform.parent != null ? transform.parent : transform);
        GetComponent<Collider>().enabled = false;

        // 조립 성공 알려주기
        AssemblyPart partInfo = partRoot.GetComponent<AssemblyPart>();
        if (partInfo != null)
        {
            partInfo.OnAssembled(giveIDToChild);
        }
    }
}