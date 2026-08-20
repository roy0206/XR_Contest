using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// StartScene에 직접 배치된 공용 Player와 FixedUI의 World Space 버튼을 연결한다.
/// 플레이어 생성과 이동은 공용 Player 프리팹이 담당하고 이 컴포넌트는 UI 상호작용만 중계한다.
/// </summary>
public sealed class StartScenePlayerInteraction : MonoBehaviour
{
    [SerializeField] Camera sourceCamera;

    readonly List<RaycastResult> _raycastResults = new();

    Player _player;
    Camera _playerCamera;

    public Player ActivePlayer => _player;
    public Camera ActiveCamera => _playerCamera;

    void Awake()
    {
        _player = FindFirstObjectByType<Player>();
        _playerCamera = _player != null ? _player.GetComponentInChildren<Camera>(true) : null;

        if (_player == null || _playerCamera == null)
        {
            Debug.LogError("[StartScene Player] 씬에 직접 배치된 Player 또는 카메라를 찾을 수 없습니다.", this);
            return;
        }

        // 원본 FixedUI 카메라의 렌더 설정만 이어받고, 위치와 회전은 씬의 Player가 소유한다.
        if (sourceCamera != null)
            _playerCamera.CopyFrom(sourceCamera);

        _playerCamera.tag = "MainCamera";
        _playerCamera.enabled = true;
    }

    void LateUpdate()
    {
        if (_player == null || _player.Input == null || _playerCamera == null)
            return;

        var commands = _player.Input.Commands;
        if (commands.InteractLeft || commands.InteractRight)
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
