using System.Collections.Generic;
using UnityEngine;

namespace Core.Audio
{
    public interface IAudioClipProvider
    {
        Awaitable<Dictionary<string, AudioClip>> LoadAsync(
            IReadOnlyList<AudioEntryData> entries);

        void Release();
    }
}
