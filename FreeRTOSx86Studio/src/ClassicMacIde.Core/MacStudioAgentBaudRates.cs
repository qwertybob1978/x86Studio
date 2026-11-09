namespace ClassicMacIde.Core;

public static class MacStudioAgentBaudRates
{
    private static readonly IReadOnlyList<int> SupportedRateValues =
        Array.AsReadOnly(new[]
        {
            300,
            600,
            1200,
            1800,
            2400,
            3600,
            4800,
            7200,
            9600,
            14400,
            19200,
            28800,
            38400,
            57600,
        });

    public const int MaximumRate = 57600;

    public static IReadOnlyList<int> SupportedRates => SupportedRateValues;

    public static uint GetCapabilityFlag(int baudRate) =>
        baudRate switch
        {
            9600 => 1u << 0,
            19200 => 1u << 1,
            38400 => 1u << 2,
            57600 => 1u << 3,
            300 => 1u << 4,
            600 => 1u << 5,
            1200 => 1u << 6,
            1800 => 1u << 7,
            2400 => 1u << 8,
            3600 => 1u << 9,
            4800 => 1u << 10,
            7200 => 1u << 11,
            14400 => 1u << 12,
            28800 => 1u << 13,
            _ => throw new ArgumentOutOfRangeException(
                nameof(baudRate),
                baudRate,
                "The rate is not supported by the MacStudio Agent."),
        };
}
