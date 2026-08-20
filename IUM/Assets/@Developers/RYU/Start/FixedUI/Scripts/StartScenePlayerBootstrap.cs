using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// StartScene에 프로젝트 공용 플레이어를 배치하고 FixedUI의 World Space 버튼을
/// 플레이어 상호작용 명령으로 실행할 수 있게 연결한다.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class StartScenePlayerBootstrap : MonoBehaviour
{
    [Header("플레이어 배치")]
    [SerializeField] GameObject playerPrefab;
    [SerializeField] Camera sourceCamera;
    [SerializeField, Min(0f)] float eyeHeight = 1.6f;

    readonly List<RaycastResult> _raycastResults = new();

    Player _player;
    Camera _playerCamera;

    public Player ActivePlayer => _player;
    public Camera ActiveCamera => _playerCamera;

    void Awake()
    {
        _player = FindFirstObjectByType<Player>();
        if (_player == null)
            CreatePlayer();
        else
            _playerCamera = _player.GetComponentInChildren<Camera>(true);
    }

    void CreatePlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[StartScene Player] Player 프리팹이 연결되지 않았습니다.", this);
            return;
        }

        var cameraTransform = sourceCamera != null ? sourceCamera.transform : transform;
        var cameraRotation = cameraTransform.rotation;
        var rootRotation = Quaternion.Euler(0f, cameraRotation.eulerAngles.y, 0f);
        var rootPosition = cameraTransform.position - Vector3.up * eyeHeight;

        // 프리팹 카메라가 활성화될 때 AudioListener와 MainCamera가 중복되지 않게 한다.
        if (sourceCamera != null)
            sourceCamera.gameObject.SetActive(false);

        var instance = Instantiate(playerPrefab, rootPosition, rootRotation);
        instance.name = "Start Scene Player";

        _player = instance.GetComponent<Player>();
        _playerCamera = instance.GetComponentInChildren<Camera>(true);
        if (_player == null || _playerCamera == null)
        {
            Debug.LogError("[StartScene Player] Player 또는 카메라 구성을 찾을 수 없습니다.", instance);
            return;
        }

        // 원본 FixedUI 씬의 시야 설정은 유지하고, 위치와 회전만 플레이어가 소유한다.
        if (sourceCamera != null)
            _playerCamera.CopyFrom(sourceCamera);

        _playerCamera.transform.localRotation = Quaternion.Inverse(rootRotation) * cameraRotation;
        _playerCamera.tag = "MainCamera";
        _playerCamera.enabled = true;
    }

    void LateUpdate()
    {
        if (_player == null || _player.Input == null || _playerCamera == null)
            return;

        var commands = _player.Input.Commands;
        if (!commands.InteractLeft && !commands.InteractRight)
            return;

        TryActivateFocusedButton();
    }

    /// <summary>현재 플레이어 시야 중앙에 있는 uGUI 버튼을 한 번 실행한다.</summary>
    public bool TryActivateFocusedButton()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        var pointer = new PointerEventData(eventSystem)
        {
            position = new Vector2(_playerCamera.pixelWidth * 0.5f, _playerCamera.pixelHeight * 0.5f),
            button = PointerEventData.InputButton.Left
        };

        _raycastResults.Clear();
        eventSystem.RaycastAll(pointer, _raycastResults);

        for (var i = 0; i < _raycastResults.Count; i++)
        {
            var button = _raycastResults[i].gameObject.GetComponentInParent<Button>();
            if (button == null || !button.IsActive() || !button.IsInteractable())
                continue;

            button.Select();
            button.onClick.Invoke();
            return true;
        }

        return false;
    }
}
