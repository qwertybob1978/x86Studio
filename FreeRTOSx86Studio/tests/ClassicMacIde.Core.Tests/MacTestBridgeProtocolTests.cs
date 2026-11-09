using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacTestBridgeProtocolTests
{
    [Fact]
    public void SerializesVersionedClassicMacRequestWithCarriageReturnLines()
    {
        var request = MacTestBridgeProtocol.SerializeRequest(
        [
            new MacTestCommand(MacTestCommandKind.QueryForm),
            new MacTestCommand(MacTestCommandKind.ClickControl, "button-1"),
            new MacTestCommand(MacTestCommandKind.Quit),
        ]);

        Assert.Equal(
            "MACSTUDIO/2\rQUERY_FORM\rCLICK|button-1\rQUIT\r",
            request);
    }

    [Fact]
    public void RequiresQuitToBeTheFinalRequestCommand()
    {
        Assert.Throws<ArgumentException>(() =>
            MacTestBridgeProtocol.SerializeRequest(
            [
                new MacTestCommand(MacTestCommandKind.Quit),
                new MacTestCommand(MacTestCommandKind.QueryForm),
            ]));

        Assert.Throws<ArgumentException>(() =>
            MacTestBridgeProtocol.SerializeRequest(
            [
                new MacTestCommand(MacTestCommandKind.QueryForm),
            ]));
    }

    [Fact]
    public void RejectsControlIdsThatCannotBeRepresentedInTheRequestFormat()
    {
        Assert.Throws<ArgumentException>(() =>
            MacTestBridgeProtocol.SerializeRequest(
            [
                new MacTestCommand(MacTestCommandKind.ClickControl, "button|1"),
                new MacTestCommand(MacTestCommandKind.Quit),
            ]));
    }

    [Fact]
    public void ParsesFormControlEventAndErrorTelemetry()
    {
        const string response =
            "MACSTUDIO/2\r\n" +
            "READY\r\n" +
            "FORM|Demo%20Window\r\n" +
            "CONTROL|button-1|button|Run%7CTest|12|8|80|24\r\n" +
            "EVENT|CLICK|button-1|HANDLED\r\n" +
            "ERROR|ASSERTION|Expected%20value%20was%20missing\r\n" +
            "DONE\r\n";

        var telemetry = MacTestBridgeProtocol.ParseResponse(response);

        Assert.Equal("Demo Window", telemetry.FormTitle);
        var control = Assert.Single(telemetry.Controls);
        Assert.Equal("button-1", control.Id);
        Assert.Equal("button", control.WidgetTypeId);
        Assert.Equal("Run|Test", control.Text);
        Assert.Equal((12, 8, 80, 24), (control.X, control.Y, control.Width, control.Height));
        var testEvent = Assert.Single(telemetry.Events);
        Assert.Equal("CLICK", testEvent.Action);
        Assert.Equal("button-1", testEvent.ControlId);
        Assert.Equal("HANDLED", testEvent.Outcome);
        Assert.Equal("ASSERTION: Expected value was missing", Assert.Single(telemetry.Errors));
        Assert.False(telemetry.Succeeded);
    }

    [Theory]
    [InlineData("MACSTUDIO/1\rREADY\rFORM|Demo\rDONE\r")]
    [InlineData("MACSTUDIO/2\rREADY\rFORM|Demo\r")]
    [InlineData("MACSTUDIO/2\rREADY\rFORM|Demo\rUNKNOWN\rDONE\r")]
    [InlineData("MACSTUDIO/2\rREADY\rFORM|Demo\rCONTROL|bad|button|Label|-1|0|10|10\rDONE\r")]
    [InlineData("MACSTUDIO/2\rREADY\rFORM|Demo\rCONTROL|button-1|button|Label|0|0|0|10\rDONE\r")]
    [InlineData("MACSTUDIO/2\rREADY\rFORM|Demo\rCONTROL|button-1|bad%20type|Label|0|0|10|10\rDONE\r")]
    public void RejectsMalformedOrIncompleteTelemetry(string response)
    {
        Assert.Throws<FormatException>(() =>
            MacTestBridgeProtocol.ParseResponse(response));
    }
}
