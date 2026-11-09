using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClassicMacIde.Core;

public sealed record MacStudioAgentPackageInfo(
    string FileType,
    string Creator,
    uint DataForkLength,
    uint ResourceForkLength,
    uint PackageLength,
    uint PackageCrc32,
    ReadOnlyMemory<byte> BuildId);

public static class MacStudioAgentPackage
{
    private const int MacBinaryHeaderSize = 128;
    private const int HashBufferSize = 64 * 1024;

    public static async Task<MacStudioAgentPackageInfo> InspectAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        await using var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            HashBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await InspectAsync(stream, cancellationToken);
    }

    public static async Task<MacStudioAgentPackageInfo> InspectAsync(
        Stream packageStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!packageStream.CanRead || !packageStream.CanSeek)
        {
            throw new ArgumentException(
                "A MacBinary package stream must be readable and seekable.",
                nameof(packageStream));
        }

        var packageLength = packageStream.Length;
        if (packageLength < MacBinaryHeaderSize || packageLength > uint.MaxValue)
        {
            throw new InvalidDataException(
                $"MacBinary package length must be between {MacBinaryHeaderSize} and {uint.MaxValue} bytes.");
        }

        var header = new byte[MacBinaryHeaderSize];
        packageStream.Position = 0;
        await packageStream.ReadExactlyAsync(header, cancellationToken);

        var fileNameLength = header[1];
        if (header[0] != 0 || fileNameLength is 0 or > 63)
        {
            throw new InvalidDataException("The MacBinary header has an invalid filename.");
        }

        if (header.AsSpan(2, fileNameLength).Contains((byte)0))
        {
            throw new InvalidDataException("The MacBinary filename contains a null byte.");
        }

        var fileType = ReadFourCharacterCode(header.AsSpan(65, 4), "file type");
        var creator = ReadFourCharacterCode(header.AsSpan(69, 4), "creator");
        if (!string.Equals(fileType, "APPL", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The MacBinary package is type '{fileType}', not an application.");
        }

        var expectedHeaderCrc = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(124, 2));
        var actualHeaderCrc = CalculateHeaderCrc16(header.AsSpan(0, 124));
        if (expectedHeaderCrc != actualHeaderCrc)
        {
            throw new InvalidDataException(
                $"The MacBinary header CRC is invalid (expected {expectedHeaderCrc:X4}, calculated {actualHeaderCrc:X4}).");
        }

        var dataForkLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(83, 4));
        var resourceForkLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(87, 4));
        if (dataForkLength == 0 && resourceForkLength == 0)
        {
            throw new InvalidDataException("The MacBinary application has no fork data.");
        }

        var expectedPackageLength =
            (ulong)MacBinaryHeaderSize +
            GetPaddedForkLength(dataForkLength) +
            GetPaddedForkLength(resourceForkLength);
        if ((ulong)packageLength != expectedPackageLength)
        {
            throw new InvalidDataException(
                $"MacBinary fork lengths require {expectedPackageLength} bytes, but the package contains {packageLength}.");
        }

        packageStream.Position = 0;
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[HashBufferSize];
        var bytesHashed = 0L;
        var crc32 = uint.MaxValue;
        while (true)
        {
            var read = await packageStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var chunk = buffer.AsSpan(0, read);
            sha256.AppendData(chunk);
            crc32 = AppendCrc32(crc32, chunk);
            bytesHashed += read;
        }

        if (bytesHashed != packageLength)
        {
            throw new InvalidDataException(
                $"The package changed while it was being read ({bytesHashed} of {packageLength} bytes).");
        }

        var digest = sha256.GetHashAndReset();
        return new MacStudioAgentPackageInfo(
            fileType,
            creator,
            dataForkLength,
            resourceForkLength,
            checked((uint)packageLength),
            ~crc32,
            digest.AsMemory(0, 16).ToArray());
    }

    private static string ReadFourCharacterCode(
        ReadOnlySpan<byte> value,
        string fieldName)
    {
        foreach (var character in value)
        {
            if (character < 0x20 || character > 0x7E)
            {
                throw new InvalidDataException(
                    $"The MacBinary {fieldName} contains non-printable bytes.");
            }
        }

        return Encoding.ASCII.GetString(value);
    }

    private static ulong GetPaddedForkLength(uint forkLength) =>
        ((ulong)forkLength + 127) / 128 * 128;

    private static ushort CalculateHeaderCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
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
}
