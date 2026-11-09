using System.Globalization;
using System.Text;

namespace ClassicMacIde.Core;

public enum MacTestCommandKind
{
    QueryForm,
    ClickControl,
    Quit,
}

public sealed record MacTestCommand(
    MacTestCommandKind Kind,
    string? ControlId = null);

public sealed record MacTestControlTelemetry(
    string Id,
    string WidgetTypeId,
    string Text,
    int X,
    int Y,
    int Width,
    int Height);

public sealed record MacTestEventTelemetry(
    string Action,
    string ControlId,
    string Outcome);

public sealed record MacTestTelemetry(
    string FormTitle,
    IReadOnlyList<MacTestControlTelemetry> Controls,
    IReadOnlyList<MacTestEventTelemetry> Events,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded =>
        Errors.Count == 0 &&
        Events.All(testEvent => testEvent.Outcome == "HANDLED");
}

public static class MacTestBridgeProtocol
{
    public const string VersionHeader = "MACSTUDIO/2";
    public const string RequestFileName = "MacStudio.in";
    public const string ResponseFileName = "MacStudio.out";

    private const int MaximumCommandCount = 128;
    private const int MaximumControlIdLength = 63;
    private const int MaximumResponseLength = 16 * 1024;
    private const int MaximumResponseRecordLength = 1024;

    public static string SerializeRequest(IEnumerable<MacTestCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var commandList = commands.ToArray();
        if (commandList.Length is 0 or > MaximumCommandCount)
        {
            throw new ArgumentException(
                $"A request must contain between 1 and {MaximumCommandCount} commands.",
                nameof(commands));
        }

        if (commandList[^1].Kind != MacTestCommandKind.Quit ||
            commandList.Take(commandList.Length - 1)
                .Any(command => command.Kind == MacTestCommandKind.Quit))
        {
            throw new ArgumentException(
                "A request must end with exactly one Quit command.",
                nameof(commands));
        }

        var request = new StringBuilder(VersionHeader).Append('\r');
        foreach (var command in commandList)
        {
            ArgumentNullException.ThrowIfNull(command);
            switch (command.Kind)
            {
                case MacTestCommandKind.QueryForm:
                    if (command.ControlId is not null)
                    {
                        throw new ArgumentException(
                            "QueryForm commands cannot include a control ID.",
                            nameof(commands));
                    }
                    request.Append("QUERY_FORM");
                    break;
                case MacTestCommandKind.ClickControl:
                    if (!IsValidControlId(command.ControlId))
                    {
                        throw new ArgumentException(
                            "Control IDs must start with a letter and contain only letters, digits, hyphens, or underscores (up to 63 characters).",
                            nameof(commands));
                    }
                    request.Append("CLICK|").Append(command.ControlId);
                    break;
                case MacTestCommandKind.Quit:
                    if (command.ControlId is not null)
                    {
                        throw new ArgumentException(
                            "Quit commands cannot include a control ID.",
                            nameof(commands));
                    }
                    request.Append("QUIT");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(commands),
                        command.Kind,
                        "Unsupported test command.");
            }
            request.Append('\r');
        }

        return request.ToString();
    }

    public static bool IsValidControlId(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > MaximumControlIdLength ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        return value.Skip(1).All(character =>
            IsAsciiLetter(character) ||
            character is >= '0' and <= '9' or '-' or '_');
    }

    public static MacTestTelemetry ParseResponse(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);
        if (response.Length > MaximumResponseLength)
        {
            throw new FormatException("Telemetry response exceeds the 16 KiB limit.");
        }

        var lines = response.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }
        if (lines.Length < 4 || lines[0] != VersionHeader)
        {
            throw new FormatException("Telemetry response has an invalid protocol header.");
        }
        if (lines[^1] != "DONE")
        {
            throw new FormatException("Telemetry response is incomplete; the DONE record is missing.");
        }

        var controls = new List<MacTestControlTelemetry>();
        var events = new List<MacTestEventTelemetry>();
        var errors = new List<string>();
        var controlIds = new HashSet<string>(StringComparer.Ordinal);
        string? formTitle = null;
        var readySeen = false;
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length is 0 or > MaximumResponseRecordLength)
            {
                throw new FormatException($"Telemetry record {index} is empty or too long.");
            }

            var fields = line.Split('|');
            switch (fields[0])
            {
                case "READY":
                    if (fields.Length != 1 || index != 1 || readySeen)
                    {
                        throw new FormatException("READY must be the first telemetry record.");
                    }
                    readySeen = true;
                    break;
                case "FORM":
                    if (fields.Length != 2 || formTitle is not null)
                    {
                        throw new FormatException("FORM must appear once with one title field.");
                    }
                    formTitle = DecodeField(fields[1]);
                    if (formTitle.Length == 0)
                    {
                        throw new FormatException("FORM title cannot be empty.");
                    }
                    break;
                case "CONTROL":
                    if (fields.Length != 8)
                    {
                        throw new FormatException(
                            "CONTROL requires an ID, widget type, label, and four coordinates.");
                    }
                    var controlId = DecodeField(fields[1]);
                    if (!IsValidControlId(controlId) || !controlIds.Add(controlId))
                    {
                        throw new FormatException("CONTROL contains an invalid or duplicate ID.");
                    }
                    var widgetTypeId = DecodeField(fields[2]);
                    if (!IsValidControlId(widgetTypeId))
                    {
                        throw new FormatException("CONTROL contains an invalid widget type ID.");
                    }
                    var controlText = DecodeField(fields[3]);
                    controls.Add(new MacTestControlTelemetry(
                        controlId,
                        widgetTypeId,
                        controlText,
                        ParseCoordinate(fields[4], allowZero: true),
                        ParseCoordinate(fields[5], allowZero: true),
                        ParseCoordinate(fields[6], allowZero: false),
                        ParseCoordinate(fields[7], allowZero: false)));
                    break;
                case "EVENT":
                    if (fields.Length != 4)
                    {
                        throw new FormatException("EVENT requires an action, control ID, and outcome.");
                    }
                    var eventId = DecodeField(fields[2]);
                    if (!IsValidControlId(eventId))
                    {
                        throw new FormatException("EVENT contains an invalid control ID.");
                    }
                    events.Add(new MacTestEventTelemetry(
                        DecodeField(fields[1]),
                        eventId,
                        DecodeField(fields[3])));
                    break;
                case "ERROR":
                    if (fields.Length != 3)
                    {
                        throw new FormatException("ERROR requires a code and message.");
                    }
                    errors.Add(
                        $"{DecodeField(fields[1])}: {DecodeField(fields[2])}");
                    break;
                case "DONE":
                    if (fields.Length != 1 || index != lines.Length - 1)
                    {
                        throw new FormatException("DONE must be the final telemetry record.");
                    }
                    break;
                default:
                    throw new FormatException(
                        $"Unknown telemetry record '{fields[0]}'.");
            }
        }

        if (!readySeen || formTitle is null)
        {
            throw new FormatException("Telemetry response is missing READY or FORM.");
        }

        return new MacTestTelemetry(
            formTitle,
            controls.AsReadOnly(),
            events.AsReadOnly(),
            errors.AsReadOnly());
    }

    private static int ParseCoordinate(string value, bool allowZero)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var coordinate) ||
            coordinate < (allowZero ? 0 : 1))
        {
            throw new FormatException($"Invalid telemetry coordinate '{value}'.");
        }

        return coordinate;
    }

    private static string DecodeField(string value)
    {
        var decoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '%')
            {
                if (!IsSafeFieldCharacter(character))
                {
                    throw new FormatException("Telemetry contains an unescaped field character.");
                }
                decoded.Append(character);
                continue;
            }

            if (index + 2 >= value.Length ||
                !byte.TryParse(
                    value.AsSpan(index + 1, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var encoded))
            {
                throw new FormatException("Telemetry contains an invalid percent escape.");
            }
            if (encoded is < 0x20 or > 0x7e)
            {
                throw new FormatException("Telemetry fields must decode to printable ASCII.");
            }
            decoded.Append((char)encoded);
            index += 2;
        }

        return decoded.ToString();
    }

    private static bool IsSafeFieldCharacter(char character) =>
        IsAsciiLetter(character) ||
        character is >= '0' and <= '9' or '.' or '_' or '-';

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
