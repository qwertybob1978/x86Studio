using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioAgentPutTests
{
    [Fact]
    public async Task PutSendsPackageAndValidatesCompletionWhenReadyIsCoalescedWithCrcRequest()
    {
        var package = CreatePackage();
        var packagePath = WriteTemporaryPackage(package);
        try
        {
            var stream = new PutAgentStream();
            var session = new MacStudioAgentSession(stream);
            await session.ConnectAsync();

            var packageInfo = await session.PutAsync(packagePath, "DemoApp_1");

            var begin = Assert.Single(
                stream.ControlFrames,
                frame => frame.MessageType == (byte)MacStudioAgentMessageType.PutBegin);
            Assert.Equal(packageInfo.BuildId.ToArray(), begin.Payload.Span[..16].ToArray());
            Assert.Equal(
                (uint)package.Length,
                BinaryPrimitives.ReadUInt32BigEndian(begin.Payload.Span[16..20]));
            Assert.Equal(
                packageInfo.PackageCrc32,
                BinaryPrimitives.ReadUInt32BigEndian(begin.Payload.Span[20..24]));
            Assert.Equal((byte)1, begin.Payload.Span[24]);
            Assert.Equal((byte)9, begin.Payload.Span[25]);
            Assert.Equal("DemoApp_1", Encoding.ASCII.GetString(begin.Payload.Span[26..]));
            Assert.Equal(2, stream.Packets.Count);
            Assert.Equal(package[..128], stream.Packets[0].AsSpan(3, 128).ToArray());
            Assert.Equal(package[128..], stream.Packets[1].AsSpan(3, 128).ToArray());
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task PutFileSendsRawDataWithDos83DestinationName()
    {
        var contents = Enumerable.Range(0, 256)
            .Select(value => (byte)value)
            .ToArray();
        var filePath = WriteTemporaryPackage(contents);
        try
        {
            var stream = new PutAgentStream();
            var session = new MacStudioAgentSession(stream);
            await session.ConnectAsync();

            var fileInfo = await session.PutFileAsync(filePath, "demo_app.exe");

            var begin = Assert.Single(
                stream.ControlFrames,
                frame => frame.MessageType == (byte)MacStudioAgentMessageType.PutBegin);
            Assert.Equal((byte)0, begin.Payload.Span[24]);
            Assert.Equal((byte)12, begin.Payload.Span[25]);
            Assert.Equal("DEMO_APP.EXE", Encoding.ASCII.GetString(begin.Payload.Span[26..]));
            Assert.Equal((uint)contents.Length, fileInfo.FileLength);
            Assert.Equal(16, fileInfo.BuildId.Length);
            Assert.Equal(2, stream.Packets.Count);
            Assert.Equal(contents[..128], stream.Packets[0].AsSpan(3, 128).ToArray());
            Assert.Equal(contents[128..], stream.Packets[1].AsSpan(3, 128).ToArray());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task PutFileRejectsPathComponentsAndNonDosNames()
    {
        var filePath = WriteTemporaryPackage([0x42]);
        try
        {
            var stream = new PutAgentStream();
            var session = new MacStudioAgentSession(stream);
            await session.ConnectAsync();

            await Assert.ThrowsAsync<ArgumentException>(
                () => session.PutFileAsync(filePath, "..\\ESCAPE.EXE"));

            Assert.DoesNotContain(
                stream.ControlFrames,
                frame => frame.MessageType == (byte)MacStudioAgentMessageType.PutBegin);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task PutReportsAgentRejectionAtReadyAndSendsNoPackageData()
    {
        var packagePath = WriteTemporaryPackage(CreatePackage());
        try
        {
            var stream = new PutAgentStream(readyResult: 4);
            var session = new MacStudioAgentSession(stream);
            await session.ConnectAsync();

            var error = await Assert.ThrowsAsync<MacStudioAgentTransferRejectedException>(
                () => session.PutAsync(packagePath, "DemoApp"));

            Assert.Equal((byte)4, error.ResultCode);
            Assert.Equal("READY", error.Stage);
            Assert.Empty(stream.Packets);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task PutRejectsCompletionWithIncorrectVerifiedCrc()
    {
        var packagePath = WriteTemporaryPackage(CreatePackage());
        try
        {
            var stream = new PutAgentStream(corruptCompletionCrc: true);
            var session = new MacStudioAgentSession(stream);
            await session.ConnectAsync();

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => session.PutAsync(packagePath, "DemoApp"));

            Assert.Contains("CRC", error.Message);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task PutExclusivelyOwnsSerialStreamUntilCancelled()
    {
        var packagePath = WriteTemporaryPackage(CreatePackage());
        try
        {
            var stream = new PutAgentStream(holdReady: true);
            var session = new MacStudioAgentSession(stream);
            await session.ConnectAsync();
            using var cancellation = new CancellationTokenSource();

            var transfer = session.PutAsync(packagePath, "DemoApp", cancellation.Token);
            await stream.PutBeginReceived.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.ReceiveAsync());

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task PutTimesOutIfAgentDoesNotSendReady()
    {
        var packagePath = WriteTemporaryPackage(CreatePackage());
        try
        {
            var stream = new PutAgentStream(holdReady: true);
            var session = new MacStudioAgentSession(
                stream,
                responseTimeout: TimeSpan.FromMilliseconds(20));
            await session.ConnectAsync();

            await Assert.ThrowsAsync<TimeoutException>(
                () => session.PutAsync(packagePath, "DemoApp"));
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static byte[] CreatePackage()
    {
        var package = new byte[256];
        var name = Encoding.ASCII.GetBytes("DemoApp");
        package[1] = (byte)name.Length;
        name.CopyTo(package, 2);
        Encoding.ASCII.GetBytes("APPL").CopyTo(package, 65);
        Encoding.ASCII.GetBytes("TEST").CopyTo(package, 69);
        BinaryPrimitives.WriteUInt32BigEndian(package.AsSpan(87, 4), 1);
        package[128] = 0x42;
        ushort crc = 0;
        foreach (var value in package.AsSpan(0, 124))
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        BinaryPrimitives.WriteUInt16BigEndian(package.AsSpan(124, 2), crc);
        return package;
    }

    private static string WriteTemporaryPackage(byte[] package)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, package);
        return path;
    }

    private sealed class PutAgentStream(
        byte readyResult = 0,
        bool corruptCompletionCrc = false,
        bool holdReady = false) : Stream
    {
        private readonly Channel<byte> _incoming = Channel.CreateUnbounded<byte>();
        private readonly MacStudioAgentFrameDecoder _hostDecoder = new();
        private byte[]? _putBeginPayload;
        private bool _receivingPackage;

        public List<MacStudioAgentFrame> ControlFrames { get; } = [];

        public List<byte[]> Packets { get; } = [];

        public TaskCompletionSource PutBeginReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
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
            var read = 1;
            while (read < buffer.Length && _incoming.Reader.TryRead(out var value))
            {
                buffer.Span[read++] = value;
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

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
            if (_receivingPackage)
            {
                ProcessXmodemWrite(buffer.Span);
                return ValueTask.CompletedTask;
            }

            foreach (var frame in _hostDecoder.Append(buffer.Span))
            {
                ControlFrames.Add(frame);
                if (frame.MessageType == (byte)MacStudioAgentMessageType.Hello)
                {
                    Inject(CreateHelloAck(frame.SessionId));
                }
                else if (frame.MessageType == (byte)MacStudioAgentMessageType.PutBegin)
                {
                    _putBeginPayload = frame.Payload.ToArray();
                    PutBeginReceived.TrySetResult();
                    if (!holdReady)
                    {
                        _receivingPackage = readyResult == 0;
                        Inject(
                            CreateFrame(
                                MacStudioAgentMessageType.Ready,
                                frame.SessionId,
                                new byte[] { readyResult }));
                        if (_receivingPackage)
                        {
                            InjectRaw([(byte)'C']);
                        }
                    }
                }
            }

            return ValueTask.CompletedTask;
        }

        private void ProcessXmodemWrite(ReadOnlySpan<byte> data)
        {
            if (data.Length == 133 && data[0] == 0x01)
            {
                Packets.Add(data.ToArray());
                InjectRaw([0x06]);
                return;
            }

            if (data.Length == 1 && data[0] == 0x04)
            {
                _receivingPackage = false;
                InjectRaw([0x06]);
                var payload = new byte[21];
                _putBeginPayload!.AsSpan(0, 16).CopyTo(payload);
                _putBeginPayload.AsSpan(20, 4).CopyTo(payload.AsSpan(17, 4));
                if (corruptCompletionCrc)
                {
                    payload[20] ^= 0x01;
                }

                Inject(
                    CreateFrame(
                        MacStudioAgentMessageType.PutComplete,
                        ControlFrames.Single(
                            frame => frame.MessageType ==
                                (byte)MacStudioAgentMessageType.PutBegin).SessionId,
                        payload));
            }
        }

        private void Inject(MacStudioAgentFrame frame) =>
            InjectRaw(MacStudioAgentProtocol.Encode(frame));

        private void InjectRaw(ReadOnlySpan<byte> bytes)
        {
            foreach (var value in bytes)
            {
                _incoming.Writer.TryWrite(value);
            }
        }

        private static MacStudioAgentFrame CreateHelloAck(
            ReadOnlyMemory<byte> sessionId)
        {
            var payload = new byte[20];
            payload[0] = MacStudioAgentProtocol.Version;
            payload[4] = 7;
            payload[5] = 5;
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6, 2), 4);
            return CreateFrame(MacStudioAgentMessageType.HelloAck, sessionId, payload);
        }

        private static MacStudioAgentFrame CreateFrame(
            MacStudioAgentMessageType messageType,
            ReadOnlyMemory<byte> sessionId,
            ReadOnlyMemory<byte> payload) =>
            new((byte)messageType, sessionId, payload);
    }
}
