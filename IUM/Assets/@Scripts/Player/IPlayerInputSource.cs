/// <summary>
/// Converts one input device family into <see cref="PlayerCommands"/>.
/// Gameplay never talks to a source directly, so swapping devices cannot change game rules.
/// </summary>
public interface IPlayerInputSource
{
    /// <summary>False when the backing devices are missing, which hands control to the next source.</summary>
    bool IsAvailable { get; }

    void Read(ref PlayerCommands commands);
}
