using UnityEngine;

#if UNITY_INPUT_SYSTEM || true // 패키지가 설치되어 있다면 컴파일되도록 강제
using UnityEngine.InputSystem;
#endif

namespace GazeSystem
{
    /// <summary>
    /// WASD 키를 이용한 이동과 마우스 조작을 통한 카메라 회전을 처리하는 플레이어 컨트롤러 컴포넌트입니다.
    /// 구형 Input Manager에서 예외가 발생하더라도 신형 Input System으로 런타임에 안전하게 폴백(Fallback)합니다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Gaze System/Player/Player Movement")]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField]
        [Tooltip("이동 속도입니다.")]
        private float moveSpeed = 5f;

        [SerializeField]
        [Tooltip("중력의 강도입니다.")]
        private float gravity = 9.81f;

        [Header("Camera Look Settings")]
        [SerializeField]
        [Tooltip("플레이어 카메라를 지정합니다. 미지정 시 메인 카메라를 찾아 사용합니다.")]
        private Camera playerCamera;

        [SerializeField]
        [Tooltip("플레이어의 눈높이(카메라 Y축 로컬 위치)입니다.")]
        private float eyeHeight = 1.65f;

        [SerializeField]
        [Tooltip("마우스 감도입니다.")]
        private float mouseSensitivity = 2f;

        [SerializeField]
        [Tooltip("카메라 수직 회전의 최소/최대 제한 각도입니다.")]
        private Vector2 lookLimits = new Vector2(-80f, 80f);

        private CharacterController characterController;
        private Vector3 moveDirection = Vector3.zero;
        private float rotationX = 0f;

        // 런타임에 구형 인풋 시스템 예외 발생 여부 감지 플래그
        private bool useNewInputSystemFallback = false;

        private void Start()
        {
            characterController = GetComponent<CharacterController>();

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
                if (playerCamera == null)
                {
                    Debug.LogWarning($"[{gameObject.name}] Main Camera를 찾을 수 없습니다. 씬에 카메라가 있는지 확인해 주세요.");
                }
            }

            if (playerCamera != null)
            {
                // 카메라 눈높이를 지정된 높이(기본 1.0f)로 고정
                playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);

                // 카메라가 플레이어 오브젝트의 자식으로 설정되어 있는지 확인
                if (playerCamera.transform.parent != transform)
                {
                    Debug.LogWarning($"[{gameObject.name}] 지정된 카메라가 플레이어 오브젝트의 자식이 아닙니다. 회전이 어색할 수 있습니다.");
                }
            }

            // 마우스 커서를 게임 창 내에 고정하고 숨김
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 시작할 때 구형 시스템 작동 여부 체크
            try
            {
                Input.GetAxis("Horizontal");
            }
            catch (System.InvalidOperationException)
            {
                useNewInputSystemFallback = true;
                Debug.Log($"[{gameObject.name}] 신형 Input System 활성화가 감지되어 신형 API로 이동 제어를 수행합니다.");
            }
        }

        private void Update()
        {
            HandleRotation();
            HandleMovement();
        }

        /// <summary>
        /// 마우스 입력에 기반하여 플레이어의 좌우 회전 및 카메라의 상하 회전을 처리합니다.
        /// </summary>
        private void HandleRotation()
        {
            if (playerCamera == null) return;

            float mouseX = 0f;
            float mouseY = 0f;

            if (useNewInputSystemFallback)
            {
                // 신형 Input System 런타임 폴백 작동
                if (Mouse.current != null)
                {
                    Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                    mouseX = mouseDelta.x * 0.05f * mouseSensitivity;
                    mouseY = mouseDelta.y * 0.05f * mouseSensitivity;
                }
            }
            else
            {
                try
                {
                    mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
                    mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
                }
                catch (System.InvalidOperationException)
                {
                    useNewInputSystemFallback = true;
                }
            }

            // 상하 회전 값 계산 및 제한(Clamping)
            rotationX -= mouseY;
            rotationX = Mathf.Clamp(rotationX, lookLimits.x, lookLimits.y);

            // 카메라 수직 회전 적용
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);

            // 캐릭터 수평 회전 적용 (몸체를 좌우로 회전)
            transform.Rotate(Vector3.up * mouseX);
        }

        /// <summary>
        /// WASD 입력을 받아 CharacterController를 통해 캐릭터를 이동시킵니다.
        /// </summary>
        private void HandleMovement()
        {
            if (characterController == null) return;

            float inputX = 0f;
            float inputZ = 0f;

            if (useNewInputSystemFallback)
            {
                // 신형 Input System 런타임 폴백 작동
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) inputZ += 1f;
                    if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) inputZ -= 1f;
                    if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) inputX -= 1f;
                    if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) inputX += 1f;
                }
            }
            else
            {
                try
                {
                    inputX = Input.GetAxis("Horizontal");
                    inputZ = Input.GetAxis("Vertical");
                }
                catch (System.InvalidOperationException)
                {
                    useNewInputSystemFallback = true;
                }
            }

            // 플레이어가 바라보는 방향 기준의 이동 방향 벡터 계산
            Vector3 forward = transform.TransformDirection(Vector3.forward);
            Vector3 right = transform.TransformDirection(Vector3.right);
            Vector3 horizontalMove = (forward * inputZ + right * inputX).normalized * moveSpeed;

            // 수평 방향 속도 대입
            moveDirection.x = horizontalMove.x;
            moveDirection.z = horizontalMove.z;

            // 중력 및 접지 처리
            if (characterController.isGrounded)
            {
                moveDirection.y = -0.5f;
            }
            else
            {
                moveDirection.y -= gravity * Time.deltaTime;
            }

            // 이동 실행
            characterController.Move(moveDirection * Time.deltaTime);
        }
    }
}
