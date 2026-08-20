using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class GongpoInstallationDirector : MonoBehaviour
{
    [Header("Asset & Placement Settings")]
    [Tooltip("이미 씬에 완벽히 배치된 공포들의 부모 객체들. 지정한 순서대로 자식들의 스케일이 커지며 나타납니다.")]
    public List<Transform> gongpoGroupParents = new List<Transform>();

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

        if (gongpoGroupParents == null || gongpoGroupParents.Count == 0)
        {
            Debug.LogError("[GongpoInstallationDirector] 부모 객체(gongpoGroupParents)를 하나 이상 할당해야 합니다!");
            return;
        }

        // 시작 전 모든 공포 크기를 0으로 숨김
        foreach (var parent in gongpoGroupParents)
        {
            if (parent == null) continue;
            foreach (Transform child in parent)
            {
                child.localScale = Vector3.zero;
            }
        }

        StartCoroutine(InstallationRoutine());
    }

    private IEnumerator InstallationRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(delayBetweenInstalls);

        foreach (var parent in gongpoGroupParents)
        {
            if (parent == null) continue;
            foreach (Transform child in parent)
            {
                if (child == null) continue;
                PlayEffectsAndAnimate(child, child.position);
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
        Vector3 targetScale = Vector3.one;
        
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
