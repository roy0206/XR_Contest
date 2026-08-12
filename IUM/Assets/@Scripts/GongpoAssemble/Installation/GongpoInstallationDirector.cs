using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GongpoInstallationDirector : MonoBehaviour
{
    [Header("Asset Settings")]
    [Tooltip("설치될 공포 프리팹")]
    public GameObject gongpoPrefab;

    [Header("Placement Settings (Option B: Anchor List)")]
    [Tooltip("공포가 설치될 앵커 위치들")]
    public List<Transform> placementAnchors = new List<Transform>();

    [Header("Animation & Timing")]
    [Tooltip("각 공포가 설치되는 시간 간격")]
    public float delayBetweenInstalls = 0.1f;
    [Tooltip("공포 하나가 나타나는 데 걸리는 시간")]
    public float appearDuration = 0.5f;
    [Tooltip("나타날 때의 애니메이션 커브")]
    public AnimationCurve appearCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Effects")]
    [Tooltip("설치 시 발생할 파티클 이펙트")]
    public ParticleSystem installEffectPrefab;
    [Tooltip("설치 시 재생할 효과음")]
    public AudioClip installSound;

    [Header("Events")]
    public UnityEvent OnInstallationComplete;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    [ContextMenu("Test Installation")]
    public void StartInstallation()
    {
        if (gongpoPrefab == null)
        {
            Debug.LogError("GongpoPrefab이 할당되지 않았습니다!");
            return;
        }

        if (placementAnchors == null || placementAnchors.Count == 0)
        {
            Debug.LogError("Placement Anchors가 설정되지 않았습니다!");
            return;
        }

        StartCoroutine(InstallationRoutine());
    }

    private IEnumerator InstallationRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(delayBetweenInstalls);

        foreach (var anchor in placementAnchors)
        {
            if (anchor == null) continue;

            // 공포 인스턴스화
            GameObject newGongpo = Instantiate(gongpoPrefab, anchor.position, anchor.rotation, anchor);
            
            // 효과음 재생
            if (installSound != null)
            {
                audioSource.PlayOneShot(installSound);
            }

            // 이펙트 생성
            if (installEffectPrefab != null)
            {
                Instantiate(installEffectPrefab, anchor.position, Quaternion.identity);
            }

            // 애니메이션 실행 (스케일 업)
            StartCoroutine(AnimateGongpo(newGongpo.transform));

            yield return wait;
        }

        OnInstallationComplete?.Invoke();
    }

    private IEnumerator AnimateGongpo(Transform targetTransform)
    {
        float time = 0f;
        Vector3 targetScale = targetTransform.localScale;
        
        // 초기 스케일 0
        targetTransform.localScale = Vector3.zero;

        while (time < appearDuration)
        {
            time += Time.deltaTime;
            float normalizedTime = time / appearDuration;
            float curveValue = appearCurve.Evaluate(normalizedTime);
            
            targetTransform.localScale = targetScale * curveValue;
            yield return null;
        }

        targetTransform.localScale = targetScale; // 보정
    }
}
