using System;

namespace Core.Audio
{
    public readonly struct AudioHandle : IEquatable<AudioHandle>
    {
        public static AudioHandle Invalid => default;

        internal int Id { get; }
        public bool IsValid => Id > 0;

        internal AudioHandle(int id)
        {
            Id = id;
        }

        public bool Equals(AudioHandle other) => Id == other.Id;
        public override bool Equals(object obj) => obj is AudioHandle other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(AudioHandle left, AudioHandle right) => left.Equals(right);
        public static bool operator !=(AudioHandle left, AudioHandle right) => !left.Equals(right);
    }
}
