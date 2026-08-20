using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FixedUI의 시작 화면 모델을 현재 메인 카메라 기준으로 배치한다.
/// StartMenuController와 게임 흐름에는 관여하지 않는 순수 프레젠테이션 어댑터다.
/// </summary>
public sealed class StartMenuBackdrop : MonoBehaviour
{
    [SerializeField] GameObject backdropPrefab;
    [SerializeField] Material[] authoredMaterials;
    [SerializeField, Min(0.1f)] float targetHeight = 3.4f;
    [SerializeField, Min(0.1f)] float distance = 4.5f;
    [SerializeField] float horizontalOffset = 1.35f;
    [SerializeField] float verticalOffset = -0.2f;
    [SerializeField] Vector3 rotation = new(0f, 180f, 0f);

    GameObject _instance;

    void OnEnable() => TryCreateBackdrop();

    void Update()
    {
        // XR Origin의 카메라는 프리팹 초기화 뒤 활성화될 수 있다.
        if (_instance == null)
            TryCreateBackdrop();
    }

    void OnDisable()
    {
        if (_instance != null)
            Destroy(_instance);

        _instance = null;
    }

    void TryCreateBackdrop()
    {
        if (_instance != null || backdropPrefab == null)
            return;

        var targetCamera = Camera.main;
        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            return;

        _instance = Instantiate(backdropPrefab);
        _instance.name = "FixedUI Start Backdrop";
        _instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(rotation));

        foreach (var collider in _instance.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        ApplyAuthoredMaterials();
        FrameFor(targetCamera);
    }

    void ApplyAuthoredMaterials()
    {
        var materialsByName = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in authoredMaterials)
        {
            if (material != null)
                materialsByName[material.name] = material;
        }

        foreach (var renderer in _instance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;

            for (var index = 0; index < materials.Length; index++)
            {
                var current = materials[index];
                if (current == null || !materialsByName.TryGetValue(current.name, out var authored))
                    continue;

                materials[index] = authored;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    void FrameFor(Camera targetCamera)
    {
        if (!TryGetBounds(_instance, out var bounds) || bounds.size.y <= Mathf.Epsilon)
            return;

        var uniformScale = targetHeight / bounds.size.y;
        _instance.transform.localScale = Vector3.one * uniformScale;

        if (!TryGetBounds(_instance, out bounds))
            return;

        var targetCenter = targetCamera.transform.position
                           + targetCamera.transform.forward * distance
                           + targetCamera.transform.right * horizontalOffset
                           + targetCamera.transform.up * verticalOffset;
        _instance.transform.position += targetCenter - bounds.center;
    }

    static bool TryGetBounds(GameObject target, out Bounds bounds)
    {
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);

        return true;
    }
}
