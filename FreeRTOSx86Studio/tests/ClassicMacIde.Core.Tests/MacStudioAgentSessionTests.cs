using System.Buffers.Binary;
using System.IO;
using System.Threading.Channels;
using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioAgentSessionTests
{
    [Fact]
    public async Task ConnectNegotiatesVersionAndParsesHelloAck()
    {
        var stream = new TestDuplexStream((hello, _) => CreateHelloAck(hello.SessionId));
        var session = new MacStudioAgentSession(stream);

        var info = await session.ConnectAsync();

        Assert.Equal(MacStudioAgentConnectionState.Connected, session.State);
        Assert.Equal(
            new MacStudioAgentHelloInfo(1, 0, 1, 0, 7, 5, 4, 0x10, 0x1234, 1),
            info);
        Assert.Equal(16, session.SessionId.Length);
        Assert.Equal(1, stream.HelloCount);
    }

    [Fact]
    public async Task ConnectRetriesWhenResponseHasAStaleSessionId()
    {
        var stream = new TestDuplexStream((hello, attempt) =>
            attempt == 1
                ? CreateHelloAck(new byte[MacStudioAgentProtocol.SessionIdLength])
                : CreateHelloAck(hello.SessionId));
        var session = new MacStudioAgentSession(
            stream,
            maximumAttempts: 3,
            responseTimeout: TimeSpan.FromMilliseconds(30));

        await session.ConnectAsync();

        Assert.Equal(2, stream.HelloCount);
        Assert.Equal(MacStudioAgentConnectionState.Connected, session.State);
    }

    [Fact]
    public async Task ConnectTimesOutAfterTheConfiguredNumberOfAttempts()
    {
        var stream = new TestDuplexStream((_, _) => null);
        var session = new MacStudioAgentSession(
            stream,
            maximumAttempts: 2,
            responseTimeout: TimeSpan.FromMilliseconds(20));

        var error = await Assert.ThrowsAsync<TimeoutException>(
            () => session.ConnectAsync());

        Assert.Contains("2 HELLO attempts", error.Message);
        Assert.Equal(2, stream.HelloCount);
        Assert.Equal(MacStudioAgentConnectionState.Disconnected, session.State);
        Assert.Empty(session.SessionId.ToArray());
    }

    [Fact]
    public async Task ConnectHonorsCallerCancellation()
    {
        var stream = new TestDuplexStream((_, _) => null);
        var session = new MacStudioAgentSession(
            stream,
            responseTimeout: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.ConnectAsync(cancellation.Token));

        Assert.Equal(MacStudioAgentConnectionState.Disconnected, session.State);
    }

    [Fact]
    public async Task ConnectRejectsMalformedHelloAck()
    {
        var stream = new TestDuplexStream((hello, _) => new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.HelloAck,
            hello.SessionId,
            new byte[19]));
        var session = new MacStudioAgentSession(stream);

        await Assert.ThrowsAsync<InvalidDataException>(() => session.ConnectAsync());

        Assert.Equal(MacStudioAgentConnectionState.Disconnected, session.State);
    }

    [Fact]
    public async Task ConnectSurfacesAgentErrorDetails()
    {
        var stream = new TestDuplexStream((hello, _) => new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.Error,
            hello.SessionId,
            new byte[] { 0, 5, 1, 3, (byte)'B', (byte)'a', (byte)'d' }));
        var session = new MacStudioAgentSession(stream);

        var error = await Assert.ThrowsAsync<MacStudioAgentProtocolException>(
            () => session.ConnectAsync());

        Assert.Equal(5, error.ErrorCode);
        Assert.Equal((byte)MacStudioAgentMessageType.Hello, error.RejectedMessageType);
        Assert.Equal("Bad", error.Reason);
    }

    [Fact]
    public async Task SendRejectsFramesFromAnotherSession()
    {
        var stream = new TestDuplexStream((hello, _) => CreateHelloAck(hello.SessionId));
        var session = new MacStudioAgentSession(stream);
        await session.ConnectAsync();
        var frame = new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.DebugAck,
            new byte[MacStudioAgentProtocol.SessionIdLength],
            new byte[] { 0, 0, 0, 1, 0 });

        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync(frame));

        Assert.Equal(1, stream.HelloCount);
        Assert.Single(stream.Writes);
    }

    [Fact]
    public async Task ReceiveIgnoresFramesFromOtherSessions()
    {
        var stream = new TestDuplexStream((hello, _) => CreateHelloAck(hello.SessionId));
        var session = new MacStudioAgentSession(stream);
        await session.ConnectAsync();
        stream.Inject(new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.DebugRecord,
            new byte[MacStudioAgentProtocol.SessionIdLength],
            new byte[] { 1 }));
        stream.Inject(new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.DebugRecord,
            session.SessionId,
            new byte[] { 2 }));

        var received = await session.ReceiveAsync();

        Assert.Equal((byte)MacStudioAgentMessageType.DebugRecord, received.MessageType);
        Assert.Equal(new byte[] { 2 }, received.Payload.ToArray());
    }

    [Fact]
    public async Task DisconnectSendsFrameAndResetsSessionState()
    {
        var stream = new TestDuplexStream((hello, _) => CreateHelloAck(hello.SessionId));
        var session = new MacStudioAgentSession(stream);
        await session.ConnectAsync();

        await session.DisconnectAsync();

        Assert.Equal(MacStudioAgentConnectionState.Disconnected, session.State);
        Assert.Null(session.AgentInfo);
        Assert.Empty(session.SessionId.ToArray());
        var written = new MacStudioAgentFrameDecoder()
            .Append(stream.Writes.SelectMany(bytes => bytes).ToArray());
        Assert.Collection(
            written,
            hello => Assert.Equal(
                (byte)MacStudioAgentMessageType.Hello,
                hello.MessageType),
            disconnect => Assert.Equal(
                (byte)MacStudioAgentMessageType.Disconnect,
                disconnect.MessageType));
    }

    [Theory]
    [InlineData(MacStudioAgentMessageType.Run, MacStudioAgentMessageType.RunResult)]
    [InlineData(MacStudioAgentMessageType.Stop, MacStudioAgentMessageType.StopResult)]
    public async Task BuildCommandsSendTheBuildIdAndReturnTheAgentResult(
        MacStudioAgentMessageType requestType,
        MacStudioAgentMessageType responseType)
    {
        var buildId = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var stream = new TestDuplexStream(
            (hello, _) => CreateHelloAck(hello.SessionId),
            request => CreateBuildResult(request, responseType, buildId, 0));
        var session = new MacStudioAgentSession(stream);
        await session.ConnectAsync();

        var result = requestType == MacStudioAgentMessageType.Run
            ? await session.RunAsync(buildId)
            : await session.StopAsync(buildId);

        Assert.Equal((byte)0, result);
        var frames = new MacStudioAgentFrameDecoder()
            .Append(stream.Writes.SelectMany(bytes => bytes).ToArray());
        var command = Assert.Single(frames, frame => frame.MessageType == (byte)requestType);
        Assert.Equal(buildId, command.Payload.ToArray());
    }

    [Fact]
    public async Task RunRejectsAResultForADifferentBuild()
    {
        var buildId = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var otherBuildId = Enumerable.Range(2, 16).Select(value => (byte)value).ToArray();
        var stream = new TestDuplexStream(
            (hello, _) => CreateHelloAck(hello.SessionId),
            request => CreateBuildResult(
            request,
            MacStudioAgentMessageType.RunResult,
            otherBuildId,
            0));
        var session = new MacStudioAgentSession(stream);
        await session.ConnectAsync();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => session.RunAsync(buildId));
    }

    [Fact]
    public async Task RunSurfacesAgentProtocolErrors()
    {
        var buildId = new byte[16];
        var stream = new TestDuplexStream(
            (hello, _) => CreateHelloAck(hello.SessionId),
            request => new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.Error,
            request.SessionId,
            new byte[] { 0, 1, (byte)MacStudioAgentMessageType.Run, 3, (byte)'B', (byte)'a', (byte)'d' }));
        var session = new MacStudioAgentSession(stream);
        await session.ConnectAsync();

        var error = await Assert.ThrowsAsync<MacStudioAgentProtocolException>(
            () => session.RunAsync(buildId));

        Assert.Equal("Bad", error.Reason);
        Assert.Equal((byte)MacStudioAgentMessageType.Run, error.RejectedMessageType);
    }

    [Fact]
    public async Task StopTimesOutWhenTheAgentDoesNotRespond()
    {
        var stream = new TestDuplexStream(
            (hello, _) => CreateHelloAck(hello.SessionId),
            _ => null);
        var session = new MacStudioAgentSession(
            stream,
            responseTimeout: TimeSpan.FromMilliseconds(20));
        await session.ConnectAsync();

        await Assert.ThrowsAsync<TimeoutException>(
            () => session.StopAsync(new byte[16]));
    }

    private static MacStudioAgentFrame CreateHelloAck(ReadOnlyMemory<byte> sessionId)
    {
        var payload = new byte[20];
        payload[0] = MacStudioAgentProtocol.Version;
        payload[1] = 0;
        payload[2] = 1;
        payload[3] = 0;
        payload[4] = 7;
        payload[5] = 5;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 4);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8, 4), 0x10);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(12, 4), 0x1234);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16, 4), 1);
        return new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.HelloAck,
            sessionId,
            payload);
    }

    private static MacStudioAgentFrame CreateBuildResult(
        MacStudioAgentFrame request,
        MacStudioAgentMessageType responseType,
        byte[] buildId,
        byte resultCode)
    {
        var payload = new byte[17];
        buildId.CopyTo(payload, 0);
        payload[16] = resultCode;
        return new MacStudioAgentFrame(
            (byte)responseType,
            request.SessionId,
            payload);
    }

    private sealed class TestDuplexStream(
        Func<MacStudioAgentFrame, int, MacStudioAgentFrame?> responseFactory,
        Func<MacStudioAgentFrame, MacStudioAgentFrame?>? messageResponseFactory = null) : Stream
    {
        private readonly Channel<byte> _incoming = Channel.CreateUnbounded<byte>();
        private readonly MacStudioAgentFrameDecoder _hostDecoder = new();

        public int HelloCount { get; private set; }

        public List<byte[]> Writes { get; } = [];

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Inject(MacStudioAgentFrame frame)
        {
            foreach (var value in MacStudioAgentProtocol.Encode(frame))
            {
                _incoming.Writer.TryWrite(value);
            }
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count))
                .AsTask()
                .GetAwaiter()
                .GetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            var first = await _incoming.Reader.ReadAsync(cancellationToken);
            buffer.Span[0] = first;
            var count = 1;
            while (count < buffer.Length && _incoming.Reader.TryRead(out var value))
            {
                buffer.Span[count++] = value;
            }

            return count;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count))
                .AsTask()
                .GetAwaiter()
                .GetResult();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var written = buffer.ToArray();
            Writes.Add(written);
            foreach (var frame in _hostDecoder.Append(written))
            {
                if (frame.MessageType != (byte)MacStudioAgentMessageType.Hello)
                {
                    if (messageResponseFactory?.Invoke(frame) is { } messageResponse)
                    {
                        Inject(messageResponse);
                    }
                    continue;
                }

                HelloCount++;
                var response = responseFactory(frame, HelloCount);
                if (response is { } reply)
                {
                    Inject(reply);
                }
            }

            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }
}
