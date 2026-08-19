namespace Core.Audio
{
    /// <summary>
    /// 출력 버스. <see cref="Dialogue"/>는 IUM에서 추가했고 (ISSUE-002),
    /// <see cref="Video"/>도 마찬가지다 (ISSUE-020).
    ///
    /// 둘 다 전용 로직이 아니라 버스가 늘어난 것뿐이라 범용 모듈에 두었다. 재생 자체는 이
    /// 어셈블리 밖에서 한다 — 대사는 <c>DialogueAudioModule</c>, 영상은 <c>CutsceneVideoSurface</c>가
    /// 각자 자기 소스를 들고 볼륨만 받아 간다.
    /// </summary>
    public enum AudioBus
    {
        Sfx = 0,
        Bgm = 1,
        Dialogue = 2,

        /// <summary>
        /// 영상 컷씬. mp4는 배경음악·내레이션·효과음이 이미 하나로 믹스되어 있어 다른 버스로
        /// 나눌 수 없다. 쪼갤 수 없으므로 버스를 하나 늘렸다.
        /// </summary>
        Video = 3
    }
}
