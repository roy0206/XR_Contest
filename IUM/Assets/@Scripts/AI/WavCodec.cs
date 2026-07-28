using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Minimal RIFF/WAVE support. Encoding feeds Google STT (LINEAR16) and decoding turns the
/// CLOVA Voice response into a clip without depending on platform audio decoders.
/// </summary>
public static class WavCodec
{
    const int HeaderSize = 44;

    /// <summary>Encodes mono/stereo float samples as 16-bit PCM. Safe to call off the main thread.</summary>
    public static byte[] EncodePcm16(float[] samples, int sampleRate, int channels)
    {
        samples ??= Array.Empty<float>();
        channels = Mathf.Max(1, channels);
        sampleRate = Mathf.Max(1, sampleRate);

        var dataSize = samples.Length * 2;
        var buffer = new byte[HeaderSize + dataSize];

        WriteAscii(buffer, 0, "RIFF");
        WriteInt32(buffer, 4, 36 + dataSize);
        WriteAscii(buffer, 8, "WAVE");
        WriteAscii(buffer, 12, "fmt ");
        WriteInt32(buffer, 16, 16);              // PCM subchunk size
        WriteInt16(buffer, 20, 1);               // PCM format
        WriteInt16(buffer, 22, (short)channels);
        WriteInt32(buffer, 24, sampleRate);
        WriteInt32(buffer, 28, sampleRate * channels * 2);
        WriteInt16(buffer, 32, (short)(channels * 2));
        WriteInt16(buffer, 34, 16);              // bits per sample
        WriteAscii(buffer, 36, "data");
        WriteInt32(buffer, 40, dataSize);

        var offset = HeaderSize;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        return buffer;
    }

    /// <summary>
    /// Headerless 16-bit PCM, which is what Google STT expects for LINEAR16.
    /// Safe to call off the main thread.
    /// </summary>
    public static byte[] EncodeRawPcm16(float[] samples)
    {
        samples ??= Array.Empty<float>();
        var buffer = new byte[samples.Length * 2];
        var offset = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        return buffer;
    }

    /// <summary>
    /// Linear resample of mono samples. The on-device recognizer rejects any rate other than the
    /// one it was trained on, and microphones commonly report 44.1/48 kHz.
    /// Safe to call off the main thread.
    /// </summary>
    public static float[] Resample(float[] samples, int sourceRate, int targetRate)
    {
        if (samples == null || samples.Length == 0) return Array.Empty<float>();
        if (sourceRate <= 0 || targetRate <= 0 || sourceRate == targetRate) return samples;

        var ratio = (double)sourceRate / targetRate;
        var length = (int)(samples.Length / ratio);
        if (length <= 0) return Array.Empty<float>();

        var result = new float[length];
        for (var i = 0; i < length; i++)
        {
            var position = i * ratio;
            var index = (int)position;
            var next = index + 1 < samples.Length ? index + 1 : index;
            var fraction = (float)(position - index);
            result[i] = Mathf.Lerp(samples[index], samples[next], fraction);
        }

        return result;
    }

    /// <summary>Averages interleaved channels into mono. Recognition only ever needs one channel.</summary>
    public static float[] Downmix(float[] samples, int channels)
    {
        if (samples == null || channels <= 1) return samples ?? Array.Empty<float>();

        var frames = samples.Length / channels;
        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
                sum += samples[frame * channels + channel];
            mono[frame] = sum / channels;
        }

        return mono;
    }

    /// <summary>Decodes 8/16/32-bit PCM or IEEE float WAV data. Must run on the main thread.</summary>
    public static AudioClip DecodeToClip(byte[] wav, string clipName)
    {
        if (!TryDecode(wav, out var samples, out var sampleRate, out var channels))
            return null;

        var frames = samples.Length / channels;
        if (frames <= 0) return null;

        var clip = AudioClip.Create(clipName, frames, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Headerless little-endian 16-bit PCM to samples, for responses without a RIFF header.</summary>
    public static float[] DecodeRawPcm16(byte[] data)
    {
        if (data == null || data.Length < 2) return Array.Empty<float>();

        var count = data.Length / 2;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(data, i * 2) / (float)short.MaxValue;

        return samples;
    }

    public static bool TryDecode(byte[] wav, out float[] samples, out int sampleRate, out int channels)
    {
        samples = Array.Empty<float>();
        sampleRate = 0;
        channels = 0;

        if (wav == null || wav.Length < 12) return false;
        if (ReadAscii(wav, 0, 4) != "RIFF" || ReadAscii(wav, 8, 4) != "WAVE") return false;

        var format = 1;
        var bitsPerSample = 16;
        var position = 12;
        var dataOffset = -1;
        var dataSize = 0;

        // Chunk walking is required: CLOVA responses can carry a LIST chunk before data.
        while (position + 8 <= wav.Length)
        {
            var chunkId = ReadAscii(wav, position, 4);
            var chunkSize = ReadInt32(wav, position + 4);
            var chunkStart = position + 8;
            if (chunkSize < 0 || chunkStart + chunkSize > wav.Length)
                chunkSize = wav.Length - chunkStart;

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                format = ReadInt16(wav, chunkStart);
                channels = ReadInt16(wav, chunkStart + 2);
                sampleRate = ReadInt32(wav, chunkStart + 4);
                bitsPerSample = ReadInt16(wav, chunkStart + 14);
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkStart;
                dataSize = chunkSize;
                break;
            }

            position = chunkStart + chunkSize + (chunkSize % 2); // chunks are word aligned
        }

        if (dataOffset < 0 || channels <= 0 || sampleRate <= 0) return false;

        var bytesPerSample = Mathf.Max(1, bitsPerSample / 8);
        var count = dataSize / bytesPerSample;
        if (count <= 0) return false;

        samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var offset = dataOffset + i * bytesPerSample;
            samples[i] = format == 3 && bitsPerSample == 32
                ? BitConverter.ToSingle(wav, offset)
                : bitsPerSample switch
                {
                    8 => (wav[offset] - 128) / 128f,
                    32 => ReadInt32(wav, offset) / (float)int.MaxValue,
                    _ => ReadInt16(wav, offset) / (float)short.MaxValue
                };
        }

        return true;
    }

    static void WriteAscii(byte[] buffer, int offset, string value) =>
        Encoding.ASCII.GetBytes(value, 0, value.Length, buffer, offset);

    static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    static string ReadAscii(byte[] buffer, int offset, int length) =>
        offset + length <= buffer.Length ? Encoding.ASCII.GetString(buffer, offset, length) : string.Empty;

    static int ReadInt32(byte[] buffer, int offset) =>
        offset + 4 <= buffer.Length ? BitConverter.ToInt32(buffer, offset) : 0;

    static short ReadInt16(byte[] buffer, int offset) =>
        offset + 2 <= buffer.Length ? BitConverter.ToInt16(buffer, offset) : (short)0;
}
