using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioAgentPackageTests
{
    [Fact]
    public async Task InspectParsesMacBinaryForksAndCalculatesPackageIdentity()
    {
        var dataFork = Encoding.ASCII.GetBytes("code");
        var resourceFork = Enumerable.Range(0, 129).Select(value => (byte)value).ToArray();
        var package = CreatePackage(dataFork, resourceFork);
        using var stream = new MemoryStream(package, writable: false);

        var info = await MacStudioAgentPackage.InspectAsync(stream);

        Assert.Equal("APPL", info.FileType);
        Assert.Equal("TEST", info.Creator);
        Assert.Equal((uint)dataFork.Length, info.DataForkLength);
        Assert.Equal((uint)resourceFork.Length, info.ResourceForkLength);
        Assert.Equal((uint)package.Length, info.PackageLength);
        Assert.Equal(CalculateCrc32(package), info.PackageCrc32);
        Assert.Equal(SHA256.HashData(package)[..16], info.BuildId.ToArray());
    }

    [Fact]
    public async Task InspectRejectsInvalidMacBinaryHeaderCrc()
    {
        var package = CreatePackage([], [1, 2, 3]);
        package[124] ^= 0xFF;
        using var stream = new MemoryStream(package, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MacStudioAgentPackage.InspectAsync(stream));
    }

    [Fact]
    public async Task InspectRejectsNonApplicationFinderType()
    {
        var package = CreatePackage([], [1], fileType: "TEXT");
        using var stream = new MemoryStream(package, writable: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => MacStudioAgentPackage.InspectAsync(stream));

        Assert.Contains("not an application", error.Message);
    }

    [Fact]
    public async Task InspectRejectsForkLengthMismatchAndTrailingBytes()
    {
        var package = CreatePackage([], [1, 2, 3]);
        var truncated = new MemoryStream(package[..^1], writable: false);
        var trailing = new MemoryStream(
            package.Concat(new byte[] { 0 }).ToArray(),
            writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MacStudioAgentPackage.InspectAsync(truncated));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => MacStudioAgentPackage.InspectAsync(trailing));
    }

    [Fact]
    public async Task InspectRejectsEmptyForksAndInvalidFilenameLength()
    {
        var emptyPackage = CreatePackage([], []);
        var invalidName = CreatePackage([], [1]);
        invalidName[1] = 0;
        WriteHeaderCrc(invalidName);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => MacStudioAgentPackage.InspectAsync(
                new MemoryStream(emptyPackage, writable: false)));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => MacStudioAgentPackage.InspectAsync(
                new MemoryStream(invalidName, writable: false)));
    }

    private static byte[] CreatePackage(
        byte[] dataFork,
        byte[] resourceFork,
        string fileType = "APPL")
    {
        var header = new byte[128];
        var name = Encoding.ASCII.GetBytes("UnitTest");
        header[1] = (byte)name.Length;
        name.CopyTo(header, 2);
        Encoding.ASCII.GetBytes(fileType).CopyTo(header, 65);
        Encoding.ASCII.GetBytes("TEST").CopyTo(header, 69);
        BinaryPrimitives.WriteUInt32BigEndian(
            header.AsSpan(83, 4),
            (uint)dataFork.Length);
        BinaryPrimitives.WriteUInt32BigEndian(
            header.AsSpan(87, 4),
            (uint)resourceFork.Length);
        WriteHeaderCrc(header);

        return header
            .Concat(dataFork)
            .Concat(new byte[GetPaddingLength(dataFork.Length)])
            .Concat(resourceFork)
            .Concat(new byte[GetPaddingLength(resourceFork.Length)])
            .ToArray();
    }

    private static void WriteHeaderCrc(byte[] package)
    {
        var crc = CalculateHeaderCrc16(package.AsSpan(0, 124));
        BinaryPrimitives.WriteUInt16BigEndian(package.AsSpan(124, 2), crc);
    }

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

    private static uint CalculateCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
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

        return ~crc;
    }

    private static int GetPaddingLength(int forkLength) =>
        (128 - forkLength % 128) % 128;
}
