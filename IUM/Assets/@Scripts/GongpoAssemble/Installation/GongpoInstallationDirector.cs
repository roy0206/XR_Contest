using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GongpoInstallationDirector : MonoBehaviour
{
    [Header("Asset & Placement Settings")]
    [Tooltip("[방식 1] 이미 씬에 완벽히 배치된 공포들의 부모 객체 (추천!)")]
    public Transform gongpoGroupParent;

    [Space(10)]
    [Tooltip("[방식 2] 프리팹을 생성할 경우 사용 (방식 1 사용 시 비워두세요)")]
    public GameObject gongpoPrefab;
    [Tooltip("[방식 2] 공포가 생성될 앵커 위치들")]
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

    /// <summary>
    /// 연출이 모두 완료되었는지 외부 스크립트에서 체크할 수 있는 프로퍼티입니다.
    /// </summary>
    public bool IsInstallationComplete { get; private set; } = false;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    [ContextMenu("Test Installation")]
    public void StartInstallation()
    {
        Debug.Log("[GongpoInstallationDirector] StartInstallation() 연출 시작 호출됨!");

        if (gongpoGroupParent == null && (gongpoPrefab == null || placementAnchors == null || placementAnchors.Count == 0))
        {
            Debug.LogError("[GongpoInstallationDirector] 부모 객체(gongpoGroupParent)를 할당하거나, 프리팹+앵커를 할당해야 합니다!");
            return;
        }

        // 방식 1: 시작 전 모든 공포 크기를 0으로 숨김
        if (gongpoGroupParent != null)
        {
            foreach (Transform child in gongpoGroupParent)
            {
                child.localScale = Vector3.zero;
            }
        }

        StartCoroutine(InstallationRoutine());
    }

    private IEnumerator InstallationRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(delayBetweenInstalls);

        // 방식 1: 씬에 이미 배치된 자식들 활용
        if (gongpoGroupParent != null)
        {
            foreach (Transform child in gongpoGroupParent)
            {
                if (child == null) continue;
                PlayEffectsAndAnimate(child, child.position);
                yield return wait;
            }
        }
        // 방식 2: 앵커에 프리팹 생성
        else
        {
            foreach (var anchor in placementAnchors)
            {
                if (anchor == null) continue;

                GameObject newGongpo = Instantiate(gongpoPrefab, anchor.position, anchor.rotation, anchor);
                PlayEffectsAndAnimate(newGongpo.transform, anchor.position);
                
                yield return wait;
            }
        }

        IsInstallationComplete = true;
        OnInstallationComplete?.Invoke();
    }

    private void PlayEffectsAndAnimate(Transform targetTransform, Vector3 effectPosition)
    {
        if (installSound != null) audioSource.PlayOneShot(installSound);
        if (installEffectPrefab != null) Instantiate(installEffectPrefab, effectPosition, Quaternion.identity);
        
        StartCoroutine(AnimateGongpo(targetTransform));
    }

    private IEnumerator AnimateGongpo(Transform targetTransform)
    {
        float time = 0f;
        // 씬에 배치되어 있던 원래 크기를 목표 스케일로 잡음
        Vector3 targetScale = (gongpoGroupParent != null) ? Vector3.one : targetTransform.localScale;
        
        targetTransform.localScale = Vector3.zero;

        while (time < appearDuration)
        {
            time += Time.deltaTime;
            float normalizedTime = time / appearDuration;
            float curveValue = appearCurve.Evaluate(normalizedTime);
            
            targetTransform.localScale = targetScale * curveValue;
            yield return null;
        }

        targetTransform.localScale = targetScale;
    }
}
