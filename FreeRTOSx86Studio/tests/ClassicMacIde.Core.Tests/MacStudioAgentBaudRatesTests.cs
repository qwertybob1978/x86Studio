using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioAgentBaudRatesTests
{
    [Theory]
    [InlineData(9600, 1u << 0)]
    [InlineData(19200, 1u << 1)]
    [InlineData(38400, 1u << 2)]
    [InlineData(57600, 1u << 3)]
    [InlineData(300, 1u << 4)]
    [InlineData(600, 1u << 5)]
    [InlineData(1200, 1u << 6)]
    [InlineData(1800, 1u << 7)]
    [InlineData(2400, 1u << 8)]
    [InlineData(3600, 1u << 9)]
    [InlineData(4800, 1u << 10)]
    [InlineData(7200, 1u << 11)]
    [InlineData(14400, 1u << 12)]
    [InlineData(28800, 1u << 13)]
    public void MapsSupportedMacBaudRatesToCapabilities(
        int baudRate,
        uint expectedFlag)
    {
        Assert.Equal(expectedFlag, MacStudioAgentBaudRates.GetCapabilityFlag(baudRate));
        Assert.Contains(baudRate, MacStudioAgentBaudRates.SupportedRates);
    }

    [Fact]
    public void MaximumRateMatchesTheMacSerialManagerMaximum()
    {
        Assert.Equal(57600, MacStudioAgentBaudRates.MaximumRate);
        Assert.Equal(
            MacStudioAgentBaudRates.MaximumRate,
            MacStudioAgentBaudRates.SupportedRates[^1]);
    }

    [Fact]
    public void RejectsUnsupportedBaudRates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MacStudioAgentBaudRates.GetCapabilityFlag(115200));
    }
}
