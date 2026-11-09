using System.Buffers.Binary;
using System.Text;

namespace ClassicMacIde.Core;

public sealed record MacStudioAgentDebugRecord(
    uint Sequence,
    byte Severity,
    string Message);

public static class MacStudioAgentDebugRecordCodec
{
    public const int HeaderSize = 7;
    public const int MaximumMessageLength = 512;

    public static MacStudioAgentDebugRecord Decode(MacStudioAgentFrame frame)
    {
        if (frame.MessageType != (byte)MacStudioAgentMessageType.DebugRecord)
        {
            throw new ArgumentException(
                "The frame is not a DEBUG_RECORD.",
                nameof(frame));
        }

        var payload = frame.Payload.Span;
        if (payload.Length < HeaderSize ||
            payload.Length > HeaderSize + MaximumMessageLength)
        {
            throw new InvalidDataException(
                "The Agent returned a DEBUG_RECORD with an invalid size.");
        }

        var messageLength = BinaryPrimitives.ReadUInt16BigEndian(payload[5..7]);
        if (messageLength > MaximumMessageLength ||
            payload.Length != HeaderSize + messageLength)
        {
            throw new InvalidDataException(
                "The Agent returned a DEBUG_RECORD with an invalid message length.");
        }

        var messageBytes = payload[HeaderSize..];
        foreach (var value in messageBytes)
        {
            if (value != '\t' && value is < 0x20 or > 0x7E)
            {
                throw new InvalidDataException(
                    "The Agent returned non-printable DEBUG_RECORD text.");
            }
        }

        return new MacStudioAgentDebugRecord(
            BinaryPrimitives.ReadUInt32BigEndian(payload[..4]),
            payload[4],
            Encoding.ASCII.GetString(messageBytes));
    }

    public static MacStudioAgentFrame CreateAcknowledgment(
        ReadOnlyMemory<byte> sessionId,
        uint sequence,
        byte resultCode)
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(payload, sequence);
        payload[4] = resultCode;
        return new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.DebugAck,
            sessionId,
            payload);
    }
}
