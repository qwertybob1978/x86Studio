using System.Buffers.Binary;
using System.Text;
using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioAgentDebugRecordTests
{
    [Fact]
    public void DecodeReadsSequenceSeverityAndPrintableText()
    {
        var frame = CreateDebugRecord(17, 2, "Ready\tto run");

        var record = MacStudioAgentDebugRecordCodec.Decode(frame);

        Assert.Equal(17u, record.Sequence);
        Assert.Equal((byte)2, record.Severity);
        Assert.Equal("Ready\tto run", record.Message);
    }

    [Fact]
    public void DecodeRejectsMismatchedMessageLength()
    {
        var frame = CreateDebugRecord(1, 0, "Test");
        var payload = frame.Payload.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5, 2), 3);

        Assert.Throws<InvalidDataException>(() =>
            MacStudioAgentDebugRecordCodec.Decode(frame with { Payload = payload }));
    }

    [Fact]
    public void DecodeRejectsNonPrintableText()
    {
        var frame = CreateDebugRecord(1, 0, "Test");
        var payload = frame.Payload.ToArray();
        payload[7] = 0x1B;

        Assert.Throws<InvalidDataException>(() =>
            MacStudioAgentDebugRecordCodec.Decode(frame with { Payload = payload }));
    }

    [Fact]
    public void AcknowledgmentContainsSequenceAndResultCode()
    {
        var sessionId = new byte[MacStudioAgentProtocol.SessionIdLength];

        var acknowledgment = MacStudioAgentDebugRecordCodec.CreateAcknowledgment(
            sessionId,
            0x12345678,
            0);

        Assert.Equal((byte)MacStudioAgentMessageType.DebugAck, acknowledgment.MessageType);
        Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32BigEndian(
            acknowledgment.Payload.Span[..4]));
        Assert.Equal((byte)0, acknowledgment.Payload.Span[4]);
    }

    private static MacStudioAgentFrame CreateDebugRecord(
        uint sequence,
        byte severity,
        string message)
    {
        var messageBytes = Encoding.ASCII.GetBytes(message);
        var payload = new byte[MacStudioAgentDebugRecordCodec.HeaderSize + messageBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, sequence);
        payload[4] = severity;
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(5, 2),
            (ushort)messageBytes.Length);
        messageBytes.CopyTo(payload, MacStudioAgentDebugRecordCodec.HeaderSize);
        return new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.DebugRecord,
            new byte[MacStudioAgentProtocol.SessionIdLength],
            payload);
    }
}
