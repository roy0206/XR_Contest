using UnityEngine;

public class RockingHorse : MonoBehaviour
{
	[Header("흔들림 설정")]
	[SerializeField] private float maxAngle = 25f;          // 최대 기울기 각도
	[SerializeField] private float rotationSpeed = 90f;     // 입력에 따른 회전 속도 (도/초)
	[SerializeField] private float returnSpeed = 3f;        // 입력 없을 때 복귀(흔들림) 속도
	[SerializeField] private float damping = 1.5f;          // 감쇠 (값이 클수록 빨리 멈춤)

	[Header("회전 축")]
	[SerializeField] private Vector3 rotationAxis = Vector3.forward; // 보통 Z축(2D) 또는 X축(3D 앞뒤)

	private float currentAngle = 0f;
	private float angularVelocity = 0f;

	void Update()
	{
		float input = Input.GetAxisRaw("Horizontal"); // 좌(-1) / 우(+1)

		if (Mathf.Abs(input) > 0.01f)
		{
			// 사용자가 방향키를 누르는 동안 직접 각도 변경
			currentAngle += input * rotationSpeed * Time.deltaTime;
			currentAngle = Mathf.Clamp(currentAngle, -maxAngle, maxAngle);
			angularVelocity = input * rotationSpeed; // 떼는 순간 관성 유지
		}
		else
		{
			// 스프링처럼 0도로 복귀 + 감쇠 진동
			float springForce = -currentAngle * returnSpeed * returnSpeed;
			float dampingForce = -angularVelocity * damping;
			angularVelocity += (springForce + dampingForce) * Time.deltaTime;
			currentAngle += angularVelocity * Time.deltaTime;
		}

		// 회전 적용 (로컬 회전 기준)
		transform.localRotation = Quaternion.AngleAxis(currentAngle, rotationAxis.normalized);
	}
}