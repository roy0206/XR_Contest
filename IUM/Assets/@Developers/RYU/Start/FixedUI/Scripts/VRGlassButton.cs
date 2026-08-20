using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Image))]
[RequireComponent(typeof(Button))]
public class VRGlassButton : MonoBehaviour,
	IPointerEnterHandler, IPointerExitHandler,
	IPointerDownHandler, IPointerUpHandler
{
	public enum State { Disabled, Normal, Hovered, Pressed }

	[Header("배경색")]
	[SerializeField] Color bgDisabled = new Color(1f, 1f, 1f, 0.24f);
	[SerializeField] Color bgNormal = new Color(1f, 1f, 1f, 0.90f);
	[SerializeField] Color bgHovered = new Color(1f, 1f, 1f, 1.00f);
	[SerializeField] Color bgPressed = new Color(0.49f, 0.78f, 1f, 1.00f);

	[Header("글자·아이콘 색")]
	[SerializeField] Color fgDisabled = new Color(0.10f, 0.10f, 0.10f, 0.28f);
	[SerializeField] Color fgNormal = new Color(0.10f, 0.10f, 0.10f, 0.86f);
	[SerializeField] Color fgActive = new Color(0.10f, 0.10f, 0.10f, 1.00f);

	[Header("강조")]
	[SerializeField] Image indicator;
	[SerializeField] Image icon;
	[SerializeField] Color accent = new Color(0.49f, 0.78f, 1f, 1f);

	[Header("모션")]
	[SerializeField] float speed = 14f;
	[SerializeField] float pressScale = 0.975f;
	[SerializeField] float hoverTextOffset = 6f;

	[Header("햅틱")]
	[SerializeField] float hoverAmplitude = 0.10f;
	[SerializeField] float clickAmplitude = 0.35f;

	Image bg;
	Button button;
	TMP_Text label;
	RectTransform rect;
	RectTransform labelRect;

	State state = State.Normal;
	Color targetBg, targetFg, targetInd;
	Vector3 targetScale = Vector3.one;
	Vector2 labelBasePos, labelTargetPos;

	void Awake()
	{
		bg = GetComponent<Image>();
		button = GetComponent<Button>();
		rect = GetComponent<RectTransform>();
		label = GetComponentInChildren<TMP_Text>();

		if (label != null)
		{
			labelRect = label.rectTransform;
			labelBasePos = labelRect.anchoredPosition;
			labelTargetPos = labelBasePos;
		}

		button.transition = Selectable.Transition.None;
		SetState(button.interactable ? State.Normal : State.Disabled, true);
	}

	void Update()
	{
		if (!button.interactable && state != State.Disabled)
			SetState(State.Disabled);
		else if (button.interactable && state == State.Disabled)
			SetState(State.Normal);

		float t = Time.unscaledDeltaTime * speed;

		bg.color = Color.Lerp(bg.color, targetBg, t);
		if (label != null) label.color = Color.Lerp(label.color, targetFg, t);
		if (icon != null) icon.color = Color.Lerp(icon.color, targetFg, t);
		if (indicator != null) indicator.color = Color.Lerp(indicator.color, targetInd, t);

		rect.localScale = Vector3.Lerp(rect.localScale, targetScale, t);

		if (labelRect != null)
			labelRect.anchoredPosition = Vector2.Lerp(labelRect.anchoredPosition, labelTargetPos, t);
	}

	void SetState(State s, bool instant = false)
	{
		state = s;
		Color indOff = accent; indOff.a = 0f;

		switch (s)
		{
			case State.Disabled:
				targetBg = bgDisabled; targetFg = fgDisabled; targetInd = indOff;
				targetScale = Vector3.one;
				labelTargetPos = labelBasePos;
				break;
			case State.Normal:
				targetBg = bgNormal; targetFg = fgNormal; targetInd = indOff;
				targetScale = Vector3.one;
				labelTargetPos = labelBasePos;
				break;
			case State.Hovered:
				targetBg = bgHovered; targetFg = fgActive; targetInd = accent;
				targetScale = Vector3.one;
				labelTargetPos = labelBasePos + new Vector2(hoverTextOffset, 0f);
				break;
			case State.Pressed:
				targetBg = bgPressed; targetFg = fgActive; targetInd = accent;
				targetScale = Vector3.one * pressScale;
				labelTargetPos = labelBasePos + new Vector2(hoverTextOffset, 0f);
				break;
		}

		if (instant)
		{
			bg.color = targetBg;
			if (label != null) label.color = targetFg;
			if (icon != null) icon.color = targetFg;
			if (indicator != null) indicator.color = targetInd;
			rect.localScale = targetScale;
			if (labelRect != null) labelRect.anchoredPosition = labelTargetPos;
		}
	}

	public void OnPointerEnter(PointerEventData e)
	{
		if (!button.interactable) return;
		SetState(State.Hovered);
		VRHaptics.Pulse(hoverAmplitude, 0.04f);
	}

	public void OnPointerExit(PointerEventData e)
	{
		if (!button.interactable) return;
		SetState(State.Normal);
	}

	public void OnPointerDown(PointerEventData e)
	{
		if (!button.interactable) return;
		SetState(State.Pressed);
		VRHaptics.Pulse(clickAmplitude, 0.08f);
	}

	public void OnPointerUp(PointerEventData e)
	{
		if (!button.interactable) return;
		SetState(State.Hovered);
	}
}