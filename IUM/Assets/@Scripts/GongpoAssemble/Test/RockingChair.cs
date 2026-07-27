using UnityEngine;
using UnityEngine.XR; // VR 컨트롤러 입력을 받기 위해 필수!

public class RockingChair : MonoBehaviour
{
    public float speed = 100f;       // 레버 젖힐 때 속도
    public float returnSpeed = 2f;   // 레버 놓았을 때 원위치 속도
    public float maxAngle = 15f;     // 최대 기울기 각도

    private float currentAngle = 0f;

    void Update()
    {
        float input = 0f;

        // 1. 오른쪽 컨트롤러(RightHand) 기기 정보 가져오기
        InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // 2. 컨트롤러의 썸스틱(primary2DAxis) 값을 읽어오기 시도
        if (rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 thumbstickValue))
        {
            // 썸스틱의 X값 (왼쪽으로 밀면 -1.0, 오른쪽으로 밀면 1.0)
            input = thumbstickValue.x;
        }

        // 이후 로직은 기존과 동일
        if (input != 0)
        {
            currentAngle += input * speed * Time.deltaTime;
        }
        else
        {
            // 레버를 놓으면 서서히 0도(정중앙)로 복귀
            currentAngle = Mathf.Lerp(currentAngle, 0, Time.deltaTime * returnSpeed);
        }

        // 각도가 maxAngle을 넘지 않도록 제한
        currentAngle = Mathf.Clamp(currentAngle, -maxAngle, maxAngle);

        // Z축을 기준으로 회전 적용 (방향이 반대면 speed나 input에 마이너스(-)를 붙여주세요)
        transform.localRotation = Quaternion.Euler(0, 0, currentAngle);
    }
}