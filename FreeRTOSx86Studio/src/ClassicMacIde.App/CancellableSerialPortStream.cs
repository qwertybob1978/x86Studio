using System.IO;
using System.IO.Ports;
using System.Threading.Channels;
using ClassicMacIde.Core;

namespace ClassicMacIde.App;

internal sealed class CancellableSerialPortStream : Stream, IMacStudioAgentBulkWriter
{
    private const int ReadChunkSize = 4096;

    private readonly SerialPort _port;
    private readonly Stream _writeStream;
    private readonly TimeSpan _perByteWriteDelay;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Channel<byte[]> _received = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly object _portReadLock = new();
    private byte[]? _currentBuffer;
    private int _currentOffset;
    private bool _disposed;

    public CancellableSerialPortStream(
        SerialPort port,
        TimeSpan perByteWriteDelay = default)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentOutOfRangeException.ThrowIfLessThan(perByteWriteDelay, TimeSpan.Zero);
        if (!port.IsOpen)
        {
            throw new InvalidOperationException(
                "The serial port must be open before creating its stream.");
        }

        _port = port;
        _writeStream = port.BaseStream;
        _perByteWriteDelay = perByteWriteDelay;
        _port.DataReceived += Port_DataReceived;
        DrainInput();
    }

    public override bool CanRead => !_disposed && _port.IsOpen;

    public override bool CanSeek => false;

    public override bool CanWrite => !_disposed && _port.IsOpen;

    public override long Length =>
        throw new NotSupportedException("A serial stream has no length.");

    public override long Position
    {
        get => throw new NotSupportedException("A serial stream has no position.");
        set => throw new NotSupportedException("A serial stream has no position.");
    }

    public override void Flush() => _writeStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _writeStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return ValueTask.FromResult(0);
        }

        return ReadCoreAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteCoreAsync(
            buffer.AsMemory(offset, count),
            CancellationToken.None,
            applyPerByteDelay: true)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        WriteCoreAsync(
            buffer.AsMemory(offset, count),
            cancellationToken,
            applyPerByteDelay: true).AsTask();

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(buffer, cancellationToken, applyPerByteDelay: true);

    public ValueTask WriteBulkAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        WriteCoreAsync(buffer, cancellationToken, applyPerByteDelay: false);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("A serial stream cannot seek.");

    public override void SetLength(long value) =>
        throw new NotSupportedException("A serial stream has no length.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_portReadLock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _port.DataReceived -= Port_DataReceived;
                    _received.Writer.TryComplete(
                        new ObjectDisposedException(nameof(CancellableSerialPortStream)));
                }
            }
        }

        base.Dispose(disposing);
    }

    private async ValueTask<int> ReadCoreAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        while (_currentBuffer is null)
        {
            if (!await _received.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            if (!_received.Reader.TryRead(out _currentBuffer))
            {
                continue;
            }

            _currentOffset = 0;
        }

        var bytesToCopy = Math.Min(
            buffer.Length,
            _currentBuffer.Length - _currentOffset);
        _currentBuffer.AsMemory(_currentOffset, bytesToCopy).CopyTo(buffer);
        _currentOffset += bytesToCopy;
        if (_currentOffset == _currentBuffer.Length)
        {
            _currentBuffer = null;
            _currentOffset = 0;
        }

        return bytesToCopy;
    }

    private async ValueTask WriteCoreAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken,
        bool applyPerByteDelay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (!applyPerByteDelay || _perByteWriteDelay == TimeSpan.Zero)
            {
                await _writeStream.WriteAsync(buffer, cancellationToken);
                return;
            }

            for (var index = 0; index < buffer.Length; index++)
            {
                await _writeStream.WriteAsync(buffer.Slice(index, 1), cancellationToken);
                if (index + 1 < buffer.Length)
                {
                    await Task.Delay(_perByteWriteDelay, cancellationToken);
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void Port_DataReceived(
        object sender,
        SerialDataReceivedEventArgs eventArgs) =>
        DrainInput();

    private void DrainInput()
    {
        lock (_portReadLock)
        {
            if (_disposed || !_port.IsOpen)
            {
                return;
            }

            try
            {
                while (true)
                {
                    var bytesAvailable = _port.BytesToRead;
                    if (bytesAvailable == 0)
                    {
                        return;
                    }

                    var buffer = new byte[Math.Min(bytesAvailable, ReadChunkSize)];
                    var bytesRead = _port.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        return;
                    }

                    if (bytesRead != buffer.Length)
                    {
                        Array.Resize(ref buffer, bytesRead);
                    }

                    _received.Writer.TryWrite(buffer);
                }
            }
            catch (Exception error) when (
                error is IOException or
                    InvalidOperationException or
                    ObjectDisposedException or
                    TimeoutException or
                    UnauthorizedAccessException)
            {
                _received.Writer.TryComplete(error);
            }
        }
    }
}
