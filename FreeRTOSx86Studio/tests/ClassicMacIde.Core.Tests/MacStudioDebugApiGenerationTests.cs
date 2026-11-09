using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacStudioDebugApiGenerationTests
{
    [Fact]
    public void GeneratedFormExposesTheAgentDebugLoggingApi()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var generated = MacFormCodeGenerator.Generate(
            profile,
            "DebugApiTest",
            "Debug API",
            [new DesignerControl("button-log", "Log", 8, 8, 80, 24)]);

        Assert.Contains(
            "OSErr MacStudioDebugLog(unsigned char severity, const char *message);",
            generated.HeaderSource);
        Assert.Contains("#include <AppleEvents.h>", generated.CSource);
        Assert.Contains("AECreateAppleEvent(", generated.CSource);
        Assert.Contains("'MSAG'", generated.CSource);
        Assert.Contains("'DBUG'", generated.CSource);
        Assert.Contains("length > 512", generated.CSource);
    }
}
