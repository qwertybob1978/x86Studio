using System.Buffers.Binary;

namespace ClassicMacIde.Core;

public enum MacStudioAgentMessageType : byte
{
    Hello = 0x01,
    HelloAck = 0x02,
    PutBegin = 0x10,
    Ready = 0x11,
    PutComplete = 0x12,
    Run = 0x20,
    RunResult = 0x21,
    DebugRecord = 0x30,
    DebugAck = 0x31,
    Stop = 0x40,
    StopResult = 0x41,
    Disconnect = 0x7E,
    Error = 0x7F,
}

public readonly record struct MacStudioAgentFrame(
    byte MessageType,
    ReadOnlyMemory<byte> SessionId,
    ReadOnlyMemory<byte> Payload);

public interface IMacStudioAgentBulkWriter
{
    ValueTask WriteBulkAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default);
}

public static class MacStudioAgentProtocol
{
    public const byte Version = 1;
    public const int SessionIdLength = 16;
    public const int HeaderSize = 26;
    public const int CrcSize = 2;
    public const int MaximumPayloadSize = 1024;
    public const int MaximumFrameSize = HeaderSize + MaximumPayloadSize + CrcSize;

    internal static ReadOnlySpan<byte> Magic => "MSAG"u8;

    public static byte[] Encode(MacStudioAgentFrame frame)
    {
        if (frame.SessionId.Length != SessionIdLength)
        {
            throw new ArgumentException(
                $"A session ID must contain {SessionIdLength} bytes.",
                nameof(frame));
        }

        if (frame.Payload.Length > MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                $"Payloads cannot exceed {MaximumPayloadSize} bytes.");
        }

        var frameLength = HeaderSize + frame.Payload.Length + CrcSize;
        var bytes = new byte[frameLength];
        var header = bytes.AsSpan(0, HeaderSize);
        Magic.CopyTo(header);
        header[4] = Version;
        header[5] = frame.MessageType;
        frame.SessionId.Span.CopyTo(header[8..24]);
        BinaryPrimitives.WriteUInt16BigEndian(
            header[24..26],
            checked((ushort)frame.Payload.Length));
        frame.Payload.Span.CopyTo(bytes.AsSpan(HeaderSize, frame.Payload.Length));

        var crc = CalculateCrc16(bytes.AsSpan(0, frameLength - CrcSize));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(frameLength - CrcSize), crc);
        return bytes;
    }

    internal static ushort CalculateCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc;
    }
}

public sealed class MacStudioAgentFrameDecoder
{
    private readonly byte[] _buffer = new byte[MacStudioAgentProtocol.MaximumFrameSize];
    private int _length;

    public IReadOnlyList<MacStudioAgentFrame> Append(ReadOnlySpan<byte> data)
    {
        var frames = new List<MacStudioAgentFrame>();
        foreach (var value in data)
        {
            if (_length == _buffer.Length)
            {
                DropPrefix(1);
            }

            _buffer[_length++] = value;
            DecodeAvailable(frames);
        }

        return frames;
    }

    public void Reset() => _length = 0;

    internal byte[] DrainBufferedBytes()
    {
        var bufferedBytes = _buffer.AsSpan(0, _length).ToArray();
        Reset();
        return bufferedBytes;
    }

    private void DecodeAvailable(List<MacStudioAgentFrame> frames)
    {
        while (_length >= MacStudioAgentProtocol.Magic.Length)
        {
            var magicOffset = FindMagic();
            if (magicOffset < 0)
            {
                DropPrefix(Math.Max(0, _length - (MacStudioAgentProtocol.Magic.Length - 1)));
                return;
            }

            if (magicOffset > 0)
            {
                DropPrefix(magicOffset);
            }

            if (_length < MacStudioAgentProtocol.HeaderSize)
            {
                return;
            }

            var header = _buffer.AsSpan(0, MacStudioAgentProtocol.HeaderSize);
            var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(header[24..26]);
            if (header[4] != MacStudioAgentProtocol.Version ||
                header[6] != 0 ||
                header[7] != 0 ||
                payloadLength > MacStudioAgentProtocol.MaximumPayloadSize)
            {
                DropPrefix(1);
                continue;
            }

            var frameLength =
                MacStudioAgentProtocol.HeaderSize + payloadLength + MacStudioAgentProtocol.CrcSize;
            if (_length < frameLength)
            {
                return;
            }

            var expectedCrc = BinaryPrimitives.ReadUInt16BigEndian(
                _buffer.AsSpan(frameLength - MacStudioAgentProtocol.CrcSize));
            var actualCrc = MacStudioAgentProtocol.CalculateCrc16(
                _buffer.AsSpan(0, frameLength - MacStudioAgentProtocol.CrcSize));
            if (expectedCrc != actualCrc)
            {
                DropPrefix(1);
                continue;
            }

            var sessionId = _buffer.AsSpan(8, MacStudioAgentProtocol.SessionIdLength).ToArray();
            var payload = _buffer.AsSpan(
                MacStudioAgentProtocol.HeaderSize,
                payloadLength).ToArray();
            frames.Add(new MacStudioAgentFrame(header[5], sessionId, payload));
            DropPrefix(frameLength);
        }
    }

    private int FindMagic()
    {
        for (var index = 0; index <= _length - MacStudioAgentProtocol.Magic.Length; index++)
        {
            if (_buffer.AsSpan(index, MacStudioAgentProtocol.Magic.Length)
                .SequenceEqual(MacStudioAgentProtocol.Magic))
            {
                return index;
            }
        }

        return -1;
    }

    private void DropPrefix(int count)
    {
        if (count == 0)
        {
            return;
        }

        _length -= count;
        if (_length > 0)
        {
            Array.Copy(_buffer, count, _buffer, 0, _length);
        }
    }
}
