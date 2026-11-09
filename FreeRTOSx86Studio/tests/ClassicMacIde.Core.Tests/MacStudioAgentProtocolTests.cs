using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioAgentProtocolTests
{
    [Fact]
    public void EncodeMatchesTheVersionOneHelloGoldenFrame()
    {
        var frame = new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.Hello,
            Enumerable.Range(0, MacStudioAgentProtocol.SessionIdLength)
                .Select(value => (byte)value)
                .ToArray(),
            new byte[] { 1, 1, 1, 0 });

        var encoded = MacStudioAgentProtocol.Encode(frame);

        Assert.Equal(
            "4D53414701010000000102030405060708090A0B0C0D0E0F000401010100E1EB",
            Convert.ToHexString(encoded));
    }

    [Fact]
    public void DecoderWaitsForACompleteFrameAcrossAppends()
    {
        var encoded = MacStudioAgentProtocol.Encode(CreateFrame());
        var decoder = new MacStudioAgentFrameDecoder();

        Assert.Empty(decoder.Append(encoded.AsSpan(0, 7)));
        var decoded = Assert.Single(decoder.Append(encoded.AsSpan(7)));

        Assert.Equal((byte)MacStudioAgentMessageType.HelloAck, decoded.MessageType);
        Assert.Equal(CreateFrame().SessionId.ToArray(), decoded.SessionId.ToArray());
        Assert.Equal(CreateFrame().Payload.ToArray(), decoded.Payload.ToArray());
    }

    [Fact]
    public void DecoderExtractsMultipleFramesAndResynchronizesAfterNoise()
    {
        var first = MacStudioAgentProtocol.Encode(CreateFrame());
        var second = MacStudioAgentProtocol.Encode(CreateFrame(
            (byte)MacStudioAgentMessageType.DebugAck,
            [0, 0, 0, 1, 0]));
        var input = new byte[] { 0x00, 0xFF, (byte)'M', (byte)'S' }
            .Concat(first)
            .Concat(second)
            .ToArray();
        var decoder = new MacStudioAgentFrameDecoder();

        var decoded = decoder.Append(input);

        Assert.Equal(2, decoded.Count);
        Assert.Equal((byte)MacStudioAgentMessageType.HelloAck, decoded[0].MessageType);
        Assert.Equal((byte)MacStudioAgentMessageType.DebugAck, decoded[1].MessageType);
    }

    [Fact]
    public void DecoderRejectsBadCrcAndResynchronizesToTheNextFrame()
    {
        var invalid = MacStudioAgentProtocol.Encode(CreateFrame());
        invalid[^1] ^= 0xFF;
        var valid = MacStudioAgentProtocol.Encode(CreateFrame(
            (byte)MacStudioAgentMessageType.HelloAck,
            [1, 0, 1, 0]));
        var decoder = new MacStudioAgentFrameDecoder();

        var decoded = decoder.Append(invalid.Concat(valid).ToArray());

        Assert.Equal((byte)MacStudioAgentMessageType.HelloAck, Assert.Single(decoded).MessageType);
    }

    [Fact]
    public void DecoderRejectsOversizedLengthsAndNonzeroFlags()
    {
        var oversizedHeader = new byte[MacStudioAgentProtocol.HeaderSize];
        "MSAG"u8.CopyTo(oversizedHeader);
        oversizedHeader[4] = MacStudioAgentProtocol.Version;
        oversizedHeader[5] = (byte)MacStudioAgentMessageType.Hello;
        oversizedHeader[24] = 0x04;
        oversizedHeader[25] = 0x01;
        var invalidFlags = MacStudioAgentProtocol.Encode(CreateFrame());
        invalidFlags[7] = 1;
        var valid = MacStudioAgentProtocol.Encode(CreateFrame());
        var decoder = new MacStudioAgentFrameDecoder();

        var decoded = decoder.Append(
            oversizedHeader.Concat(invalidFlags).Concat(valid).ToArray());

        Assert.Equal((byte)MacStudioAgentMessageType.HelloAck, Assert.Single(decoded).MessageType);
    }

    [Fact]
    public void EncodeRejectsInvalidSessionAndOversizedPayload()
    {
        var invalidSession = new MacStudioAgentFrame(1, new byte[15], Array.Empty<byte>());
        var oversizedPayload = new MacStudioAgentFrame(
            1,
            new byte[MacStudioAgentProtocol.SessionIdLength],
            new byte[MacStudioAgentProtocol.MaximumPayloadSize + 1]);

        Assert.Throws<ArgumentException>(() => MacStudioAgentProtocol.Encode(invalidSession));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MacStudioAgentProtocol.Encode(oversizedPayload));
    }

    [Fact]
    public void DecoderAcceptsTheMaximumPayloadFrame()
    {
        var frame = new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.PutBegin,
            new byte[MacStudioAgentProtocol.SessionIdLength],
            new byte[MacStudioAgentProtocol.MaximumPayloadSize]);

        var encoded = MacStudioAgentProtocol.Encode(frame);
        var decoded = Assert.Single(new MacStudioAgentFrameDecoder().Append(encoded));

        Assert.Equal(MacStudioAgentProtocol.MaximumFrameSize, encoded.Length);
        Assert.Equal(MacStudioAgentProtocol.MaximumPayloadSize, decoded.Payload.Length);
    }

    private static MacStudioAgentFrame CreateFrame(
        byte messageType = (byte)MacStudioAgentMessageType.HelloAck,
        byte[]? payload = null) =>
        new(
            messageType,
            Enumerable.Range(0, MacStudioAgentProtocol.SessionIdLength)
                .Select(value => (byte)value)
                .ToArray(),
            payload ?? [0, 1, 2, 3]);
}
