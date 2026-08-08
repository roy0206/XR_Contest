namespace Core.Audio
{
    /// <summary>
    /// 출력 버스. <see cref="Dialogue"/>는 IUM에서 추가했다 (ISSUE-002).
    ///
    /// 대사 전용 로직이 아니라 버스 하나가 늘어난 것뿐이라 범용 모듈에 두었다. 대사 재생 자체는
    /// 이 어셈블리 밖의 <c>DialogueAudioModule</c>이 담당한다.
    /// </summary>
    public enum AudioBus
    {
        Sfx = 0,
        Bgm = 1,
        Dialogue = 2
    }
}
