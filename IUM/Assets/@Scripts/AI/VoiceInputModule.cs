/// <summary>
/// Turns the device-independent PTT command into 이음이 listening. Desktop `T` and the XR left
/// primary button both arrive as <see cref="PlayerCommands.PushToTalk"/>, so the conversation
/// layer never sees which device was used (개발환경_입력 규칙).
/// </summary>
public sealed class VoiceInputModule : Module
{
    readonly Player _player;

    AiConversationManager _conversation;
    bool _wasPressed;

    public VoiceInputModule(Player player) : base(player) => _player = player;

    public override void OnAdded()
    {
        // Touching the singleton here starts config loading before the first question.
        _conversation = AiConversationManager.Instance;
    }

    public override void OnUpdate()
    {
        if (_conversation == null || _player.Input == null) return;

        var pressed = _player.Input.Commands.PushToTalk;
        if (pressed == _wasPressed) return;

        _wasPressed = pressed;
        if (pressed) _conversation.BeginListening();
        else _conversation.EndListening();
    }

    public override void OnRemoved()
    {
        if (!_wasPressed || _conversation == null) return;
        _wasPressed = false;
        _conversation.CancelListening();
    }
}
