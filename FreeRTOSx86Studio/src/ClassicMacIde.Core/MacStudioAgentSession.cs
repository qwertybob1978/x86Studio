using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClassicMacIde.Core;

public enum MacStudioAgentConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

public sealed record MacStudioAgentHelloInfo(
    byte ProtocolVersion,
    byte AgentMajor,
    byte AgentMinor,
    byte AgentPatch,
    byte SystemMajor,
    byte SystemMinor,
    ushort MachineType,
    uint Capabilities,
    uint FreeBytes,
    uint SupportedBaudRates);

public sealed record MacStudioAgentFileInfo(
    uint FileLength,
    uint FileCrc32,
    ReadOnlyMemory<byte> BuildId);

public sealed class MacStudioAgentProtocolException(
    ushort errorCode,
    byte rejectedMessageType,
    string reason) : IOException(
        $"The Studio Agent rejected the request ({errorCode}): {reason}")
{
    public ushort ErrorCode { get; } = errorCode;

    public byte RejectedMessageType { get; } = rejectedMessageType;

    public string Reason { get; } = reason;
}

public sealed class MacStudioAgentTransferRejectedException(
    byte resultCode,
    string stage) : IOException(
        $"The Studio Agent rejected the transfer during {stage} (result {resultCode}).")
{
    public byte ResultCode { get; } = resultCode;

    public string Stage { get; } = stage;
}

public sealed class MacStudioAgentSession
{
    private const byte HostMajorVersion = 1;
    private const byte HostMinorVersion = 0;
    private const int ReadBufferSize = 256;

    private readonly Stream _stream;
    private readonly int _maximumAttempts;
    private readonly TimeSpan _responseTimeout;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _operationLock = new();
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
    private readonly MacStudioAgentFrameDecoder _decoder = new();
    private readonly Queue<MacStudioAgentFrame> _pendingFrames = new();
    private CancellationTokenSource _sessionLifetime = new();
    private byte[]? _sessionId;
    private int _activeOperations;
    private bool _transferInProgress;

    public MacStudioAgentSession(
        Stream stream,
        int maximumAttempts = 3,
        TimeSpan? responseTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException(
                "The Agent session requires a readable and writable stream.",
                nameof(stream));
        }

        if (maximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        var timeout = responseTimeout ?? TimeSpan.FromSeconds(2);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
        }

        _stream = stream;
        _maximumAttempts = maximumAttempts;
        _responseTimeout = timeout;
    }

    public MacStudioAgentConnectionState State { get; private set; }

    public MacStudioAgentHelloInfo? AgentInfo { get; private set; }

    public ReadOnlyMemory<byte> SessionId =>
        _sessionId is null ? ReadOnlyMemory<byte>.Empty : _sessionId;

    public async Task<MacStudioAgentHelloInfo> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (State != MacStudioAgentConnectionState.Disconnected)
            {
                throw new InvalidOperationException(
                    "The Agent session is already connecting or connected.");
            }

            State = MacStudioAgentConnectionState.Connecting;
            AgentInfo = null;
            _sessionId = null;
            await ResetInputStateAsync(cancellationToken);
            if (_sessionLifetime.IsCancellationRequested)
            {
                _sessionLifetime.Dispose();
                _sessionLifetime = new CancellationTokenSource();
            }

            var sessionId = RandomNumberGenerator.GetBytes(
                MacStudioAgentProtocol.SessionIdLength);
            _sessionId = sessionId;
            var hello = new MacStudioAgentFrame(
                (byte)MacStudioAgentMessageType.Hello,
                sessionId,
                new byte[]
                {
                    MacStudioAgentProtocol.Version,
                    MacStudioAgentProtocol.Version,
                    HostMajorVersion,
                    HostMinorVersion,
                });
            var encodedHello = MacStudioAgentProtocol.Encode(hello);

            for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
            {
                using var timeoutSource = new CancellationTokenSource(_responseTimeout);
                using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

                try
                {
                    await WriteFrameCoreAsync(encodedHello, attemptSource.Token);
                    while (true)
                    {
                        var response = await ReadFrameCoreAsync(
                            sessionId,
                            attemptSource.Token);
                        if (response.MessageType == (byte)MacStudioAgentMessageType.Error)
                        {
                            throw ParseAgentError(response);
                        }

                        if (response.MessageType !=
                            (byte)MacStudioAgentMessageType.HelloAck)
                        {
                            continue;
                        }

                        AgentInfo = ParseHelloAck(response.Payload.Span);
                        State = MacStudioAgentConnectionState.Connected;
                        return AgentInfo;
                    }
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    if (attempt == _maximumAttempts)
                    {
                        throw new TimeoutException(
                            $"The Studio Agent did not respond after {_maximumAttempts} HELLO attempts.");
                    }
                }
            }

            throw new InvalidOperationException("The Agent handshake ended unexpectedly.");
        }
        finally
        {
            if (State == MacStudioAgentConnectionState.Connecting)
            {
                State = MacStudioAgentConnectionState.Disconnected;
                AgentInfo = null;
                _sessionId = null;
            }

            _connectGate.Release();
        }
    }

    public async Task SendAsync(
        MacStudioAgentFrame frame,
        CancellationToken cancellationToken = default)
    {
        using var operation = EnterOperation();
        EnsureConnected();
        if (!frame.SessionId.Span.SequenceEqual(_sessionId!))
        {
            throw new ArgumentException(
                "The frame must use the active Agent session ID.",
                nameof(frame));
        }

        var activeSessionId = _sessionId!;
        await WriteFrameCoreAsync(
            MacStudioAgentProtocol.Encode(frame),
            cancellationToken,
            activeSessionId,
            requireConnected: true);
    }

    public async Task<MacStudioAgentFrame> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        using var operation = EnterOperation();
        EnsureConnected();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _sessionLifetime.Token);
        var frame = await ReadFrameCoreAsync(_sessionId!, linkedSource.Token);
        ThrowIfAgentError(frame);
        return frame;
    }

    public Task<byte> RunAsync(
        ReadOnlyMemory<byte> buildId,
        CancellationToken cancellationToken = default) =>
        SendBuildCommandAsync(
            MacStudioAgentMessageType.Run,
            MacStudioAgentMessageType.RunResult,
            buildId,
            cancellationToken);

    public Task<byte> StopAsync(
        ReadOnlyMemory<byte> buildId,
        CancellationToken cancellationToken = default) =>
        SendBuildCommandAsync(
            MacStudioAgentMessageType.Stop,
            MacStudioAgentMessageType.StopResult,
            buildId,
            cancellationToken);

    public async Task<MacStudioAgentPackageInfo> PutAsync(
        string packagePath,
        string destinationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var destinationNameBytes = EncodeDestinationName(destinationName);
        EnsureConnected();
        BeginTransfer();
        try
        {
            await using var packageStream = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var packageInfo = await MacStudioAgentPackage.InspectAsync(
                packageStream,
                cancellationToken);
            packageStream.Position = 0;
            await TransferFileAsync(
                packageStream,
                packageInfo.PackageLength,
                packageInfo.PackageCrc32,
                packageInfo.BuildId,
                transferMode: 1,
                destinationNameBytes,
                cancellationToken);
            return packageInfo;
        }
        finally
        {
            EndTransfer();
        }
    }

    public async Task<MacStudioAgentFileInfo> PutFileAsync(
        string filePath,
        string destinationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var destinationNameBytes = EncodeDosDestinationName(destinationName);
        EnsureConnected();
        BeginTransfer();
        try
        {
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fileInfo = await InspectFileAsync(fileStream, cancellationToken);
            fileStream.Position = 0;
            await TransferFileAsync(
                fileStream,
                fileInfo.FileLength,
                fileInfo.FileCrc32,
                fileInfo.BuildId,
                transferMode: 0,
                destinationNameBytes,
                cancellationToken);
            return fileInfo;
        }
        finally
        {
            EndTransfer();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        using var operation = EnterOperation();
        if (State == MacStudioAgentConnectionState.Disconnected)
        {
            return;
        }

        if (State != MacStudioAgentConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "The Agent session cannot disconnect while connecting.");
        }

        var sessionId = _sessionId!;
        _sessionLifetime.Cancel();
        State = MacStudioAgentConnectionState.Disconnected;
        AgentInfo = null;
        _sessionId = null;
        var disconnect = new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.Disconnect,
            sessionId,
            ReadOnlyMemory<byte>.Empty);
        await WriteFrameCoreAsync(
            MacStudioAgentProtocol.Encode(disconnect),
            cancellationToken);
    }

    private async Task<byte> SendBuildCommandAsync(
        MacStudioAgentMessageType requestType,
        MacStudioAgentMessageType responseType,
        ReadOnlyMemory<byte> buildId,
        CancellationToken cancellationToken)
    {
        if (buildId.Length != 16)
        {
            throw new ArgumentException(
                "A Build ID must contain exactly 16 bytes.",
                nameof(buildId));
        }

        using var operation = EnterOperation();
        EnsureConnected();
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            var sessionId = _sessionId!;
            var payload = buildId.ToArray();
            var request = new MacStudioAgentFrame(
                (byte)requestType,
                sessionId,
                payload);
            await WriteFrameCoreAsync(
                MacStudioAgentProtocol.Encode(request),
                cancellationToken,
                sessionId,
                requireConnected: true);

            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _sessionLifetime.Token);
            var response = await ReadResponseFrameAsync(sessionId, linkedSource.Token);
            ThrowIfAgentError(response);
            if (response.MessageType != (byte)responseType ||
                response.Payload.Length != 17 ||
                !response.Payload.Span[..16].SequenceEqual(buildId.Span))
            {
                throw new InvalidDataException(
                    $"The Agent returned a malformed {responseType} response.");
            }

            return response.Payload.Span[16];
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task ResetInputStateAsync(CancellationToken cancellationToken)
    {
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            _decoder.Reset();
            _pendingFrames.Clear();
        }
        finally
        {
            _readGate.Release();
        }
    }

    private async Task WriteFrameCoreAsync(
        ReadOnlyMemory<byte> encodedFrame,
        CancellationToken cancellationToken,
        ReadOnlyMemory<byte> expectedSessionId = default,
        bool requireConnected = false)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (requireConnected)
            {
                EnsureConnected();
                if (!expectedSessionId.Span.SequenceEqual(_sessionId!))
                {
                    throw new InvalidOperationException(
                        "The Agent session changed before the frame was sent.");
                }
            }

            await _stream.WriteAsync(encodedFrame, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<MacStudioAgentFrame> ReadFrameCoreAsync(
        ReadOnlyMemory<byte> expectedSessionId,
        CancellationToken cancellationToken)
    {
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                while (_pendingFrames.TryDequeue(out var pendingFrame))
                {
                    if (pendingFrame.SessionId.Span.SequenceEqual(expectedSessionId.Span))
                    {
                        return pendingFrame;
                    }
                }

                var read = await _stream.ReadAsync(_readBuffer, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "The serial stream closed before the Agent replied.");
                }

                foreach (var frame in _decoder.Append(_readBuffer.AsSpan(0, read)))
                {
                    _pendingFrames.Enqueue(frame);
                }
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    private async Task<MacStudioAgentFrame> ReadResponseFrameAsync(
        ReadOnlyMemory<byte> expectedSessionId,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(_responseTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            return await ReadFrameCoreAsync(expectedSessionId, linkedSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Studio Agent did not respond within {_responseTimeout}.");
        }
    }

    private static MacStudioAgentHelloInfo ParseHelloAck(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 20)
        {
            throw new InvalidDataException(
                $"HELLO_ACK must contain 20 bytes, not {payload.Length}.");
        }

        if (payload[0] != MacStudioAgentProtocol.Version)
        {
            throw new InvalidDataException(
                $"The Agent selected unsupported protocol version {payload[0]}.");
        }

        return new MacStudioAgentHelloInfo(
            payload[0],
            payload[1],
            payload[2],
            payload[3],
            payload[4],
            payload[5],
            BinaryPrimitives.ReadUInt16BigEndian(payload[6..8]),
            BinaryPrimitives.ReadUInt32BigEndian(payload[8..12]),
            BinaryPrimitives.ReadUInt32BigEndian(payload[12..16]),
            BinaryPrimitives.ReadUInt32BigEndian(payload[16..20]));
    }

    private static MacStudioAgentProtocolException ParseAgentError(
        MacStudioAgentFrame frame)
    {
        var payload = frame.Payload.Span;
        if (payload.Length < 4 ||
            payload.Length > 132 ||
            payload[3] != payload.Length - 4)
        {
            throw new InvalidDataException("The Agent returned a malformed ERROR frame.");
        }

        var reason = payload[4..];
        foreach (var value in reason)
        {
            if (value < 0x20 || value > 0x7E)
            {
                throw new InvalidDataException(
                    "The Agent ERROR reason contains non-printable bytes.");
            }
        }

        return new MacStudioAgentProtocolException(
            BinaryPrimitives.ReadUInt16BigEndian(payload[..2]),
            payload[2],
            Encoding.ASCII.GetString(reason));
    }

    private void EnsureConnected()
    {
        if (State != MacStudioAgentConnectionState.Connected)
        {
            throw new InvalidOperationException("The Agent session is not connected.");
        }
    }

    private static void ThrowIfAgentError(MacStudioAgentFrame frame)
    {
        if (frame.MessageType == (byte)MacStudioAgentMessageType.Error)
        {
            throw ParseAgentError(frame);
        }
    }

    private static byte[] EncodeDestinationName(string destinationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationName);
        if (destinationName.Length is 0 or > 31 ||
            !IsAsciiLetter(destinationName[0]))
        {
            throw new ArgumentException(
                "The destination name must start with an ASCII letter and contain at most 31 characters.",
                nameof(destinationName));
        }

        foreach (var character in destinationName)
        {
            if (!IsAsciiLetter(character) &&
                (character < '0' || character > '9') &&
                character != '_')
            {
                throw new ArgumentException(
                    "The destination name may contain only ASCII letters, digits, and underscores.",
                    nameof(destinationName));
            }
        }

        return Encoding.ASCII.GetBytes(destinationName);
    }

    private static byte[] EncodeDosDestinationName(string destinationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationName);
        if (destinationName.Length > 12)
        {
            throw new ArgumentException(
                "The DOS destination name must use the 8.3 filename format.",
                nameof(destinationName));
        }

        var dotIndex = destinationName.IndexOf('.');
        var baseLength = dotIndex < 0 ? destinationName.Length : dotIndex;
        var extensionLength = dotIndex < 0
            ? 0
            : destinationName.Length - dotIndex - 1;
        if (baseLength is < 1 or > 8 ||
            (dotIndex >= 0 && (extensionLength is < 1 or > 3)) ||
            (dotIndex >= 0 && destinationName.IndexOf('.', dotIndex + 1) >= 0))
        {
            throw new ArgumentException(
                "The DOS destination name must use the 8.3 filename format.",
                nameof(destinationName));
        }

        var encoded = new byte[destinationName.Length];
        for (var index = 0; index < destinationName.Length; index++)
        {
            var character = destinationName[index];
            if (index != dotIndex &&
                !IsDosFileNameCharacter(character))
            {
                throw new ArgumentException(
                    "DOS destination names may contain only ASCII letters, digits, underscores, hyphens, and dollar signs.",
                    nameof(destinationName));
            }

            encoded[index] = (byte)char.ToUpperInvariant(character);
        }

        if (string.Equals(
            destinationName,
            "DOSAGT.$$$",
            StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                destinationName,
                "DOSAGT.BAK",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The destination name is reserved for DOS Agent transfer staging.",
                nameof(destinationName));
        }

        return encoded;
    }

    private static bool IsDosFileNameCharacter(char character) =>
        IsAsciiLetter(character) ||
        character is >= '0' and <= '9' or '_' or '-' or '$';

    private static async Task<MacStudioAgentFileInfo> InspectFileAsync(
        FileStream fileStream,
        CancellationToken cancellationToken)
    {
        var fileLength = fileStream.Length;
        if (fileLength is <= 0 or > uint.MaxValue)
        {
            throw new InvalidDataException(
                $"DOS transfer files must contain between 1 and {uint.MaxValue} bytes.");
        }

        fileStream.Position = 0;
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var bytesHashed = 0L;
        var crc32 = uint.MaxValue;
        while (true)
        {
            var read = await fileStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var chunk = buffer.AsSpan(0, read);
            sha256.AppendData(chunk);
            crc32 = AppendCrc32(crc32, chunk);
            bytesHashed += read;
        }

        if (bytesHashed != fileLength)
        {
            throw new InvalidDataException(
                $"The file changed while it was being read ({bytesHashed} of {fileLength} bytes).");
        }

        var digest = sha256.GetHashAndReset();
        return new MacStudioAgentFileInfo(
            checked((uint)fileLength),
            ~crc32,
            digest.AsMemory(0, 16).ToArray());
    }

    private async Task TransferFileAsync(
        Stream fileStream,
        uint fileLength,
        uint fileCrc32,
        ReadOnlyMemory<byte> buildId,
        byte transferMode,
        byte[] destinationNameBytes,
        CancellationToken cancellationToken)
    {
        var sessionId = _sessionId!;
        var beginPayload = new byte[26 + destinationNameBytes.Length];
        buildId.Span.CopyTo(beginPayload);
        BinaryPrimitives.WriteUInt32BigEndian(
            beginPayload.AsSpan(16, 4),
            fileLength);
        BinaryPrimitives.WriteUInt32BigEndian(
            beginPayload.AsSpan(20, 4),
            fileCrc32);
        beginPayload[24] = transferMode;
        beginPayload[25] = (byte)destinationNameBytes.Length;
        destinationNameBytes.CopyTo(beginPayload, 26);

        var beginFrame = new MacStudioAgentFrame(
            (byte)MacStudioAgentMessageType.PutBegin,
            sessionId,
            beginPayload);
        await WriteFrameCoreAsync(
            MacStudioAgentProtocol.Encode(beginFrame),
            cancellationToken,
            sessionId,
            requireConnected: true);

        var ready = await ReadResponseFrameAsync(sessionId, cancellationToken);
        ThrowIfAgentError(ready);
        if (ready.MessageType != (byte)MacStudioAgentMessageType.Ready ||
            ready.Payload.Length != 1)
        {
            throw new InvalidDataException(
                "The Agent returned a malformed READY response.");
        }

        if (ready.Payload.Span[0] != 0)
        {
            throw new MacStudioAgentTransferRejectedException(
                ready.Payload.Span[0],
                "READY");
        }

        if (_pendingFrames.Count != 0)
        {
            throw new InvalidDataException(
                "The Agent sent an unexpected control frame before XMODEM transfer.");
        }

        var rawStream = new BufferedSerialStream(
            _stream,
            _decoder.DrainBufferedBytes());
        await new XModemCrcSender().SendAsync(
            fileStream,
            rawStream,
            fileLength,
            cancellationToken);

        var complete = await ReadResponseFrameAsync(sessionId, cancellationToken);
        ThrowIfAgentError(complete);
        if (complete.MessageType != (byte)MacStudioAgentMessageType.PutComplete ||
            complete.Payload.Length != 21 ||
            !complete.Payload.Span[..16].SequenceEqual(buildId.Span))
        {
            throw new InvalidDataException(
                "The Agent returned a malformed PUT_COMPLETE response.");
        }

        var resultCode = complete.Payload.Span[16];
        if (resultCode != 0)
        {
            throw new MacStudioAgentTransferRejectedException(
                resultCode,
                "PUT_COMPLETE");
        }

        var verifiedCrc = BinaryPrimitives.ReadUInt32BigEndian(
            complete.Payload.Span[17..21]);
        if (verifiedCrc != fileCrc32)
        {
            throw new InvalidDataException(
                "The Agent's verified file CRC does not match the sent file.");
        }
    }

    private static uint AppendCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? (crc >> 1) ^ 0xEDB88320
                    : crc >> 1;
            }
        }

        return crc;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private IDisposable EnterOperation()
    {
        lock (_operationLock)
        {
            if (_transferInProgress)
            {
                throw new InvalidOperationException(
                    "The Agent serial stream is exclusively owned by an active file transfer.");
            }

            _activeOperations++;
        }

        return new OperationLease(this);
    }

    private void BeginTransfer()
    {
        lock (_operationLock)
        {
            if (_transferInProgress || _activeOperations != 0)
            {
                throw new InvalidOperationException(
                    "File transfer requires exclusive ownership of the Agent serial stream.");
            }

            _transferInProgress = true;
        }
    }

    private void EndTransfer()
    {
        lock (_operationLock)
        {
            _transferInProgress = false;
        }
    }

    private void EndOperation()
    {
        lock (_operationLock)
        {
            _activeOperations--;
        }
    }

    private sealed class OperationLease(MacStudioAgentSession session) : IDisposable
    {
        private MacStudioAgentSession? _session = session;

        public void Dispose() =>
            Interlocked.Exchange(ref _session, null)?.EndOperation();
    }

    private sealed class BufferedSerialStream(Stream stream, byte[] prefix) : Stream
    {
        private int _prefixOffset;

        public override bool CanRead => stream.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => stream.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => stream.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            stream.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefixOffset < prefix.Length)
            {
                var bufferedCount = Math.Min(count, prefix.Length - _prefixOffset);
                Array.Copy(prefix, _prefixOffset, buffer, offset, bufferedCount);
                _prefixOffset += bufferedCount;
                return bufferedCount;
            }

            return stream.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_prefixOffset < prefix.Length)
            {
                var bufferedCount = Math.Min(buffer.Length, prefix.Length - _prefixOffset);
                prefix.AsMemory(_prefixOffset, bufferedCount).CopyTo(buffer);
                _prefixOffset += bufferedCount;
                return ValueTask.FromResult(bufferedCount);
            }

            return stream.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteBulkAsync(buffer.AsMemory(offset, count), CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            WriteBulkAsync(buffer, cancellationToken);

        private ValueTask WriteBulkAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken) =>
            stream is IMacStudioAgentBulkWriter bulkWriter
                ? bulkWriter.WriteBulkAsync(buffer, cancellationToken)
                : stream.WriteAsync(buffer, cancellationToken);
    }
}
