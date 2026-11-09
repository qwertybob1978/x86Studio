using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class MacFormCodeGeneratorTests
{
    [Fact]
    public void GeneratesDialogDimensionsAndControlsFromDesignerCoordinates()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var generated = MacFormCodeGenerator.Generate(
            profile,
            "DesignerForm",
            "Test Window",
            [
                new DesignerControl("button-1", "Run", 12, 12, 80, 24),
                new DesignerControl("button-2", "Quit", 100, 40, 60, 24),
            ]);

        Assert.Contains("{ 32, 24, 316, 488 }", generated.ResourceSource);
        Assert.Contains(
            "{ 32, 12, 56, 92 }, Button { enabled, \"Run\" }",
            generated.ResourceSource);
        Assert.Contains(
            "{ 60, 100, 84, 160 }, Button { enabled, \"Quit\" }",
            generated.ResourceSource);
        Assert.Contains("case 1:", generated.CSource);
        Assert.Contains("case 2:", generated.CSource);
        Assert.Contains("BridgeProcessRequest()", generated.CSource);
        Assert.Contains("bridgeStarted && BridgeProcessRequest()", generated.CSource);
        Assert.Contains("DrawTestCompletionMarker(dialog)", generated.CSource);
        Assert.Contains("WaitNextEvent(everyEvent", generated.CSource);
        Assert.Contains("marker[row][column] ? blackColor : whiteColor", generated.CSource);
        Assert.Contains("435 + column * 3", generated.CSource);
        Assert.Contains("235 + row * 3", generated.CSource);
        Assert.Contains("InvalidateDesignerDialogItem(dialog, item);", generated.CSource);
        Assert.Contains("InvalRect(&itemBounds);", generated.CSource);
        Assert.DoesNotContain("DrawDialog(dialog);", generated.CSource);
        Assert.Contains("result = HandleDesignerControl(item);", generated.CSource);
        Assert.Contains("BridgeWriteMetadata()", generated.CSource);
        Assert.Contains("MacStudioGetControlBounds(gDesignerDialog", generated.CSource);
        Assert.Contains("BridgeWriteNumber(bounds.left);", generated.CSource);
        Assert.Contains("char encoded[256];", generated.CSource);
        Assert.Contains("encodedLength + sequenceLength >= sizeof(encoded)", generated.CSource);
        Assert.Contains("MacStudio.in", generated.CSource);
        Assert.Contains("MacStudio.out", generated.CSource);
        Assert.Contains("MACSTUDIO/2", generated.CSource);
        Assert.Contains("BridgeWriteField(\"button\")", generated.CSource);
        Assert.Contains("FlushVol(0, gTelemetryVolumeRefNum)", generated.CSource);
        Assert.Contains("BridgeWriteEvent(\"CLICK\", controlId", generated.CSource);
        Assert.Contains(
            "MacStudioUserControlClicked(dialog, MACSTUDIO_ID_CONTROLBUTTON_002D1, item)",
            generated.CSource);
        Assert.Contains(
            "MacStudioGetControlText(DialogPtr dialog, const char *controlId",
            generated.HeaderSource);
        Assert.Contains(
            "MacStudioSetControlValue(DialogPtr dialog, const char *controlId",
            generated.HeaderSource);
        Assert.Contains("short MacStudioGetControlText(", generated.CSource);
        Assert.Contains("short MacStudioSetControlText(", generated.CSource);
        Assert.Contains("short MacStudioGetControlValue(", generated.CSource);
        Assert.Contains("short MacStudioSetControlValue(", generated.CSource);
        Assert.Contains("BridgeControlItem(controlId)", generated.CSource);
        Assert.Contains("#include \"designer_form.h\"", generated.CSource);
        Assert.Contains("256 * 1024,", generated.ResourceSource);
        Assert.Contains("256 * 1024", generated.ResourceSource);
        Assert.Contains("add_application(DesignerForm", generated.CMakeLists);
        Assert.Contains("main.c", generated.CMakeLists);
        Assert.Contains("designer_form.r", generated.CMakeLists);
        Assert.Contains("user_events.c", generated.CMakeLists);
        Assert.Contains("int MacStudioRunForm(void)", generated.CSource);
        Assert.DoesNotContain("int main(void)", generated.CSource);
        Assert.Contains("int main(void)", generated.MainSource);
        Assert.Contains("MacStudioRunForm()", generated.MainSource);
    }

    [Fact]
    public void GeneratesResizableClassicWindowControlsAndPosition()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var generated = MacFormCodeGenerator.Generate(
            profile,
            "DesignerForm",
            "Resizable Window",
            "MainForm",
            [widgets.CreateControl("button-run", "button", 8, 8)],
            widgets,
            formProperties: new MacFormProperties
            {
                Resizable = true,
                HasCloseBox = true,
                HasZoomBox = true,
                CenterOnScreen = false,
                Left = 40,
                Top = 30,
                Width = 320,
                Height = 200,
            });

        Assert.Contains("{ 30, 40, 252, 362 }", generated.ResourceSource);
        Assert.Contains("zoomDocProc,", generated.ResourceSource);
        Assert.Contains("visible,", generated.ResourceSource);
        Assert.Contains("goAway,", generated.ResourceSource);
        Assert.Contains("noAutoCenter", generated.ResourceSource);
        Assert.Contains("GrowWindow((WindowPtr)dialog", generated.CSource);
        Assert.Contains("TrackGoAway((WindowPtr)dialog", generated.CSource);
        Assert.Contains("ZoomWindow((WindowPtr)dialog", generated.CSource);
        Assert.Contains("while (formRunning)", generated.CSource);
    }

    [Fact]
    public void GeneratesAClassicProgressBarWithAValueAccessor()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var progressBar = widgets.CreateControl("TransferProgress", "progress-bar", 8, 8);
        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Transfer",
            "TransferForm",
            [progressBar],
            widgets);

        Assert.Contains("UserItem { enabled }", generated.ResourceSource);
        Assert.Contains("DrawProgressBar1", generated.CSource);
        Assert.Contains("fillBounds.right", generated.CSource);
        Assert.Contains("gProgressValue1 = value", generated.CSource);
        Assert.Contains("short MacStudioSetControlTransferProgressValue", generated.HeaderSource);
    }

    [Fact]
    public void GeneratesATitlelessInvisibleFormWithoutTitleBarControlOffsets()
    {
        var profile = Assert.Single(
            MacTargetProfiles.All,
            profile => profile.Id == "mac-se-68k");
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var generated = MacFormCodeGenerator.Generate(
            profile,
            "DesignerForm",
            "Hidden Window",
            "MainForm",
            [widgets.CreateControl("button-run", "button", 8, 6)],
            widgets,
            formProperties: new MacFormProperties
            {
                ShowTitleBar = false,
                Visible = false,
                Width = 300,
                Height = 180,
            });

        Assert.Contains("plainDBox,", generated.ResourceSource);
        Assert.Contains("invisible,", generated.ResourceSource);
        Assert.Contains("{ 6, 8, 30, 88 }, Button", generated.ResourceSource);
        Assert.Contains("bounds->top -= 0;", generated.CSource);
        Assert.DoesNotContain("windowPart == inGrow", generated.CSource);
    }

    [Fact]
    public void GeneratesBoundOnClickHandlersAndKeepsTheLegacyFallback()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var controls = new[]
        {
            registry.CreateControl("button-save", "button", 12, 12, "Save")
                .WithEvent("OnClick", "SaveButtonClick"),
            registry.CreateControl("button-2", "button", 100, 12, "Cancel"),
        };

        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Test Window",
            "MainForm",
            controls,
            registry);

        Assert.Contains(
            "SaveButtonClick((MainForm)dialog, item);",
            generated.CSource);
        Assert.Contains(
            "void SaveButtonClick(MainForm form, short item);",
            generated.HeaderSource);
        Assert.Contains(
            "MacStudioUserControlClicked(dialog, MACSTUDIO_ID_CONTROLBUTTON_002D2, item);",
            generated.CSource);
    }

    [Fact]
    public void GeneratesMenuBarResourcesAndRoutesBoundMenuClicks()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var components = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var menuItem = components.CreateComponent("FileMenuOpen", "menu-item")
            .WithProperty("caption", "Open")
            .WithProperty("shortcut", "O")
            .WithEvent("OnClick", "OpenMenuItemClick");
        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Test Window",
            "MainForm",
            [widgets.CreateControl("button1", "button", 12, 12)],
            widgets,
            [menuItem],
            components);

        Assert.Contains("resource 'MENU' (600)", generated.ResourceSource);
        Assert.Contains("\"File\"", generated.ResourceSource);
        Assert.Contains("\"Open\", noIcon, 'O', noMark, plain;", generated.ResourceSource);
        Assert.Contains("GetMenu(600)", generated.CSource);
        Assert.Contains("InsertMenu(designerMenu600, 0)", generated.CSource);
        Assert.Contains("MenuSelect(event.where)", generated.CSource);
        Assert.Contains("HiliteMenu(0)", generated.CSource);
        Assert.Contains(
            "DispatchNonVisualComponentEvent(dialog, 1, 1)",
            generated.CSource);
        Assert.Contains(
            "OpenMenuItemClick((MainForm)dialog, eventIndex);",
            generated.CSource);
        Assert.Contains(
            "void OpenMenuItemClick(MainForm form, short item);",
            generated.HeaderSource);
        Assert.Contains(
            "#define MACSTUDIO_COMPONENT_FILEMENUOPEN \"FileMenuOpen\"",
            generated.HeaderSource);
    }

    [Fact]
    public void GeneratesFileAndDistinctSerialPortRuntimeServices()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var components = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var fileAccess = components.CreateComponent("FileAccess1", "file-access")
            .WithProperty("fileType", "TEXT");
        var modem = components.CreateComponent("ModemPort", "serial-port")
            .WithEvent("OnDataAvailable", "ModemDataAvailable");
        var printer = components.CreateComponent("PrinterPort", "serial-port")
            .WithProperty("port", "Printer")
            .WithEvent("OnError", "PrinterSerialError");
        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Test Window",
            "MainForm",
            [widgets.CreateControl("button1", "button", 12, 12)],
            widgets,
            [fileAccess, modem, printer],
            components);

        Assert.Contains("short MacStudioFileLoad(", generated.HeaderSource);
        Assert.Contains("short MacStudioFileSave(", generated.HeaderSource);
        Assert.Contains("short MacStudioSerialOpen(", generated.HeaderSource);
        Assert.Contains("short MacStudioSerialOpenConfigured(", generated.HeaderSource);
        Assert.Contains(
            "short MacStudioGetLastSerialError(const char *componentId);",
            generated.HeaderSource);
        Assert.Contains("StandardGetFile(NULL, 1, fileTypes, &reply)", generated.CSource);
        Assert.Contains("StandardPutFile(savePrompt, defaultName, &reply)", generated.CSource);
        Assert.Contains("static OSErr MacStudioSetSerialBuffer(short componentIndex", generated.CSource);
        Assert.Contains(
            $"case 2:{Environment.NewLine}            return SerSetBuf(inputRef, (Ptr)gMacStudioSerialBuffer2",
            generated.CSource);
        Assert.Contains(
            $"case 3:{Environment.NewLine}            return SerSetBuf(inputRef, (Ptr)gMacStudioSerialBuffer3",
            generated.CSource);
        Assert.Contains(
            "SerGetBuf(gMacStudioSerialInputRef2, &serialAvailable)",
            generated.CSource);
        Assert.Contains(
            "SerGetBuf(gMacStudioSerialInputRef3, &serialAvailable)",
            generated.CSource);
        Assert.Contains(
            "gMacStudioLastSerialError2 = serialStatus;",
            generated.CSource);
        Assert.Contains(
            "static const unsigned char gMacStudioSerialModemInputDriverName2[] = { 4, 46, 65, 73, 110 };",
            generated.CSource);
        Assert.Contains(
            "static const unsigned char gMacStudioSerialPrinterInputDriverName3[] = { 4, 46, 66, 73, 110 };",
            generated.CSource);
        Assert.Contains(
            "OpenDriver(configuredInputRef == PRNTRIRNUM ? gMacStudioSerialPrinterInputDriverName2 : gMacStudioSerialModemInputDriverName2, inputRef)",
            generated.CSource);
        Assert.Contains(
            "OpenDriver(configuredOutputRef == PRNTRORNUM ? gMacStudioSerialPrinterOutputDriverName2 : gMacStudioSerialModemOutputDriverName2, outputRef)",
            generated.CSource);
        Assert.Contains("case 57600: return baud57600;", generated.CSource);
        Assert.Contains("CloseDriver(outputRef);", generated.CSource);
        Assert.Contains(
            "MacStudioGetSerialReferences(componentIndex, &inputRef, &outputRef)",
            generated.CSource);
        Assert.Contains("return PBKillIOSync(&parameterBlock);", generated.CSource);
        Assert.DoesNotContain("KillIO(", generated.CSource);
        Assert.Contains(
            "MacStudioSerialClose(\"ModemPort\")",
            generated.CSource);
        Assert.Contains(
            "MacStudioSerialClose(\"PrinterPort\")",
            generated.CSource);
        Assert.Contains(
            "void ModemDataAvailable(MainForm form, short item);",
            generated.HeaderSource);
        Assert.Contains(
            "void PrinterSerialError(MainForm form, short item);",
            generated.HeaderSource);
    }

    [Fact]
    public void RejectsMultipleSerialComponentsAssignedToTheSamePhysicalPort()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var components = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var serialPorts = new[]
        {
            components.CreateComponent("ModemPort1", "serial-port"),
            components.CreateComponent("ModemPort2", "serial-port"),
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            MacFormCodeGenerator.Generate(
                MacTargetProfiles.All[0],
                "DesignerForm",
                "Test Window",
                "MainForm",
                [widgets.CreateControl("button1", "button", 12, 12)],
                widgets,
                serialPorts,
                components));

        Assert.Contains("Only one serial component", exception.Message);
    }

    [Fact]
    public void GeneratesXmodemAndZmodemTransferEngines()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var components = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var serialPort = components.CreateComponent("ModemPort", "serial-port");
        var transfer = components.CreateComponent("Transfer1", "protocol-transfer")
            .WithProperty("serialPortId", "ModemPort")
            .WithEvent("OnError", "TransferError");
        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Test Window",
            "MainForm",
            [widgets.CreateControl("button1", "button", 12, 12)],
            widgets,
            [serialPort, transfer],
            components);

        Assert.Contains("short MacStudioProtocolSend(", generated.HeaderSource);
        Assert.Contains("short MacStudioProtocolReceive(", generated.HeaderSource);
        Assert.Contains("short MacStudioProtocolReceiveExact(", generated.HeaderSource);
        Assert.Contains("short MacStudioProtocolGetProgress(", generated.HeaderSource);
        Assert.Contains("#define MACSTUDIO_PROTOCOL_ON_ERROR 3", generated.CSource);
        Assert.Contains("static OSErr MacStudioXmodemSend(", generated.CSource);
        Assert.Contains("static OSErr MacStudioZmodemReceive(", generated.CSource);
        Assert.Contains("MacStudioProtocolProgressStore", generated.CSource);
        Assert.Contains("MacStudioZSendSubpacket", generated.CSource);
        Assert.Contains("void TransferError(MainForm form, short item);", generated.HeaderSource);
    }

    [Fact]
    public void RejectsProtocolTransferWithoutSerialReference()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var components = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var transfer = components.CreateComponent("Transfer1", "protocol-transfer");

        var error = Assert.Throws<ArgumentException>(() =>
            MacFormCodeGenerator.Generate(
                MacTargetProfiles.All[0],
                "DesignerForm",
                "Test Window",
                "MainForm",
                [widgets.CreateControl("button1", "button", 12, 12)],
                widgets,
                [transfer],
                components));

        Assert.Contains("requires a serial component reference", error.Message);
    }

    [Fact]
    public void RejectsInvalidZmodemFileName()
    {
        var widgets = ClassicMacWidgetCatalog.CreateRegistry();
        var components = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var serialPort = components.CreateComponent("ModemPort", "serial-port");
        var transfer = components.CreateComponent("Transfer1", "protocol-transfer")
            .WithProperty("serialPortId", "ModemPort")
            .WithProperty("protocol", "Zmodem")
            .WithProperty("fileName", new string('A', 64));

        var error = Assert.Throws<ArgumentException>(() =>
            MacFormCodeGenerator.Generate(
                MacTargetProfiles.All[0],
                "DesignerForm",
                "Test Window",
                "MainForm",
                [widgets.CreateControl("button1", "button", 12, 12)],
                widgets,
                [serialPort, transfer],
                components));

        Assert.Contains("printable ASCII file name of at most 63 characters", error.Message);
    }

    [Fact]
    public void GeneratesNativeDialogItemsForTheBuiltInClassicWidgetTypes()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var controls = new[]
        {
            registry.CreateControl("label-1", "static-text", 10, 10),
            registry.CreateControl("edit-1", "editable-text", 10, 34),
            registry.CreateControl("button-1", "button", 10, 62),
            registry.CreateControl("check-1", "checkbox", 10, 90),
            registry.CreateControl("radio-1", "radio-button", 10, 116),
            registry.CreateControl("popup-1", "popup-menu", 10, 142),
            registry.CreateControl("list-1", "list-box", 150, 10)
                .WithProperty("selectedIndex", "1"),
            registry.CreateControl("scroll-1", "scrollbar", 300, 10),
        };

        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "WidgetShowcase",
            "Widget Showcase",
            controls,
            registry);

        Assert.Contains("StaticText { enabled, \"Label\" }", generated.ResourceSource);
        Assert.Contains("EditText { enabled, \"Text\" }", generated.ResourceSource);
        Assert.Contains("Button { enabled, \"Button\" }", generated.ResourceSource);
        Assert.Contains("CheckBox { enabled, \"Check Box\" }", generated.ResourceSource);
        Assert.Contains("RadioButton { enabled, \"Radio Button\" }", generated.ResourceSource);
        Assert.Contains("UserItem { enabled }", generated.ResourceSource);
        Assert.Contains("BridgeWriteField(\"static-text\")", generated.CSource);
        Assert.Contains("BridgeWriteField(\"scrollbar\")", generated.CSource);
        Assert.Contains("ShowWindow((WindowPtr)dialog)", generated.CSource);
        Assert.True(
            generated.CSource.IndexOf("ShowWindow((WindowPtr)dialog);") <
            generated.CSource.IndexOf("bridgeStarted = BridgeStart();"));
        Assert.True(
            generated.CSource.IndexOf("bridgeStarted = BridgeStart();") <
            generated.CSource.IndexOf("MacStudioFormDidLoad(("));
        Assert.True(
            generated.CSource.IndexOf("MacStudioFormDidLoad((") <
            generated.CSource.IndexOf("BridgeWriteMetadata();"));
        Assert.Contains("GetMenu(", generated.CSource);
        Assert.Contains(
            "SetControlTitle((ControlHandle)DesignerDialogItem(dialog, 6), gPopupTitle6)",
            generated.CSource);
        Assert.Contains(
            "SetControlTitle((ControlHandle)DesignerDialogItem(gDesignerDialog, 6), gPopupTitle6)",
            generated.CSource);
        Assert.DoesNotContain("SetDialogItemText(DesignerDialogItem", generated.CSource);
        Assert.Contains("PopUpMenuSelect(", generated.CSource);
        Assert.Contains("LNew(", generated.CSource);
        Assert.Contains("LSetCell(", generated.CSource);
        Assert.Contains("NewControl(", generated.CSource);
        Assert.Contains("TrackControl(", generated.CSource);
        Assert.Contains("GetDialogItemText(", generated.CSource);
        Assert.Contains("LGetSelect(", generated.CSource);
        Assert.Contains("GetControlValue(", generated.CSource);
        Assert.Contains("DialogSelect(&event, &dialog, &item)", generated.CSource);
        Assert.Contains("ApplyRadioGroupSelection(item)", generated.CSource);
        Assert.Contains("gPopupSelection6 - 1", generated.CSource);
        Assert.Contains("LGetSelect(true, &selectedCell, gList7)", generated.CSource);
        Assert.Contains(
            $"selectedCell.h = 0;{Environment.NewLine}    selectedCell.v = 0;{Environment.NewLine}    *value = LGetSelect(true, &selectedCell, gList7)",
            generated.CSource);
        Assert.Contains("GetControlValue(gScroll8)", generated.CSource);
        Assert.Contains("LSetSelect(false, selectedCell, gList7)", generated.CSource);
        Assert.Contains("LSize((short)(itemBounds.right - itemBounds.left)", generated.CSource);
        Assert.Contains("MoveControl(gScroll8, itemBounds.left, itemBounds.top)", generated.CSource);
    }

    [Fact]
    public void GeneratesAllQuickDrawGraphicsVariantsAndNamedComponentAccessors()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var graphics = registry.Definitions
            .Where(definition => definition.Category == DesignerWidgetCategory.Graphics)
            .ToArray();
        Assert.Equal(10, graphics.Length);

        var controls = graphics.Select((definition, index) =>
        {
            var control = registry.CreateControl(
                definition.Id,
                definition.Id,
                10 + index % 5 * 88,
                10 + index / 5 * 100);
            return definition.Id.StartsWith("graphic-arc-", StringComparison.Ordinal)
                ? control.WithProperty("startAngle", "45").WithProperty("sweepAngle", "120")
                : control;
        }).ToArray();

        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "GraphicShowcase",
            "Graphic Showcase",
            controls,
            registry);

        Assert.Equal(
            10,
            generated.ResourceSource.Split("UserItem { enabled }").Length - 1);
        Assert.Contains("FrameRect(&bounds)", generated.CSource);
        Assert.Contains("PaintRect(&bounds)", generated.CSource);
        Assert.Contains("FillRect(&bounds, &qd.gray)", generated.CSource);
        Assert.Contains("FrameOval(&bounds)", generated.CSource);
        Assert.Contains("PaintOval(&bounds)", generated.CSource);
        Assert.Contains("FillOval(&bounds, &qd.gray)", generated.CSource);
        Assert.Contains("FrameArc(&bounds, 45, 120)", generated.CSource);
        Assert.Contains("PaintArc(&bounds, 45, 120)", generated.CSource);
        Assert.Contains("FillArc(&bounds, 45, 120, &qd.gray)", generated.CSource);
        Assert.DoesNotContain("RGBForeColor", generated.CSource);
        Assert.Contains("static pascal void DrawGraphic1(DialogPtr dialog, short item)", generated.CSource);
        Assert.Contains("SetDialogItem(dialog, 1, itemType, (Handle)DrawGraphic1", generated.CSource);
        Assert.Contains("short MacStudioGetControlBounds(", generated.CSource);
        Assert.Contains(
            "#define MACSTUDIO_ID_CONTROLGRAPHIC_002DLINE \"graphic-line\"",
            generated.HeaderSource);
        Assert.Contains(
            "short MacStudioGetControlgraphic_002DlineBounds(DialogPtr dialog, Rect *bounds);",
            generated.HeaderSource);
    }

    [Fact]
    public void GeneratesTargetColorQuickDrawForDitheredAndSolidGraphics()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var profile = Assert.Single(
            MacTargetProfiles.All,
            item => item.Id == "mac-color-classic-68k");
        var controls = new[]
        {
            registry.CreateControl("dither-1", "graphic-rectangle-dithered", 10, 10)
                .WithProperty("color", "#336699")
                .WithProperty("backgroundColor", "#EEDDCC"),
            registry.CreateControl("solid-1", "graphic-circle-filled", 90, 10)
                .WithProperty("color", "#CC3300"),
        };

        var generated = MacFormCodeGenerator.Generate(
            profile,
            "ColorGraphics",
            "Color graphics",
            controls,
            registry);

        Assert.Contains(
            "RGBColor drawColor1 = { 0x3333, 0x6666, 0x9999 };",
            generated.CSource);
        Assert.Contains(
            "RGBColor backgroundColor1 = { 0xEEEE, 0xDDDD, 0xCCCC };",
            generated.CSource);
        Assert.Contains("RGBForeColor(&drawColor1);", generated.CSource);
        Assert.Contains("RGBBackColor(&backgroundColor1);", generated.CSource);
        Assert.Contains("FillRect(&bounds, &qd.gray)", generated.CSource);
        Assert.Contains("RGBColor drawColor2 = { 0xCCCC, 0x3333, 0x0000 };",
            generated.CSource);
        Assert.Contains("ForeColor(blackColor);", generated.CSource);
        Assert.Contains("BackColor(whiteColor);", generated.CSource);
    }

    [Fact]
    public void RejectsMalformedGraphicColorProperties()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var control = registry.CreateControl(
                "rectangle-1",
                "graphic-rectangle-filled",
                10,
                10)
            .WithProperty("color", "blue");

        Assert.Throws<ArgumentException>(() => registry.ValidateControl(control));
    }

    [Fact]
    public void GeneratesLineEndpointsFromEditablePercentageProperties()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var line = registry.CreateControl("line-main", "graphic-line", 10, 10)
            .WithProperty("startX", "25")
            .WithProperty("startY", "75")
            .WithProperty("endX", "80")
            .WithProperty("endY", "10");

        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Line endpoints",
            [line],
            registry);

        Assert.Contains("* 25 / 100", generated.CSource);
        Assert.Contains("* 75 / 100", generated.CSource);
        Assert.Contains("* 80 / 100", generated.CSource);
        Assert.Contains("* 10 / 100", generated.CSource);
    }

    [Fact]
    public void GeneratesEditableMainSourceAndRuntimeBoundsAccessorsForFormType()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Editable Form",
            "EditorMainForm",
            [registry.CreateControl("run-button", "button", 16, 16)],
            registry);

        Assert.Contains("typedef DialogPtr EditorMainForm;", generated.HeaderSource);
        Assert.Contains("void MacStudioFormDidLoad(EditorMainForm form);", generated.HeaderSource);
        Assert.Contains(
            "short MacStudioSetControlBounds(DialogPtr dialog, const char *controlId, const Rect *bounds);",
            generated.HeaderSource);
        Assert.Contains(
            "short MacStudioSetControlrun_002DbuttonBounds(DialogPtr dialog, const Rect *bounds);",
            generated.HeaderSource);
        Assert.Contains("MacStudioFormDidLoad((EditorMainForm)dialog);", generated.CSource);
        Assert.Contains("MoveControl((ControlHandle)itemHandle", generated.CSource);
        Assert.Contains("SetPort(previousPort);", generated.CSource);
        Assert.Contains("void MacStudioFormDidLoad(DialogPtr form)", generated.MainSource);
        Assert.True(MacFormCodeGenerator.IsValidFormTypeName("EditorMainForm"));
        Assert.False(MacFormCodeGenerator.IsValidFormTypeName("2bad"));
        Assert.False(MacFormCodeGenerator.IsValidFormTypeName("int"));
    }

    [Fact]
    public void RejectsArcAnglesOutsideQuickDrawRanges()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var arc = registry.CreateControl(
                "arc-1",
                "graphic-arc-outline",
                10,
                10)
            .WithProperty("startAngle", "360");

        Assert.Throws<ArgumentException>(() => registry.ValidateControl(arc));

        arc = registry.CreateControl(
                "arc-1",
                "graphic-arc-outline",
                10,
                10)
            .WithProperty("sweepAngle", "0");
        Assert.Throws<ArgumentException>(() => registry.ValidateControl(arc));
    }

    [Fact]
    public void EscapesResourceStringQuotesAndBackslashes()
    {
        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Window \"A\"",
            [new DesignerControl("button-1", "Path \\ Run", 0, 0, 80, 24)]);

        Assert.Contains("\"Window \\\"A\\\"\"", generated.ResourceSource);
        Assert.Contains("\"Path \\\\ Run\"", generated.ResourceSource);
    }

    [Fact]
    public void RequiresAtLeastOneControlToBuildAModalForm()
    {
        Assert.Throws<ArgumentException>(() => MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Empty",
            []));
    }

    [Fact]
    public void RejectsControlsOutsideTheTargetFormPixelArea()
    {
        Assert.Throws<ArgumentException>(() => MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Out of bounds",
            [new DesignerControl("button-1", "Too wide", 400, 0, 80, 24)]));
    }

    [Fact]
    public void RejectsDuplicateControlIds()
    {
        Assert.Throws<ArgumentException>(() => MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Duplicate IDs",
            [
                new DesignerControl("button-1", "One", 0, 0, 40, 20),
                new DesignerControl("button-1", "Two", 50, 0, 40, 20),
            ]));
    }

    [Fact]
    public void RejectsControlIdsThatCannotBeRepresentedByTheTestBridge()
    {
        Assert.Throws<ArgumentException>(() => MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Invalid ID",
            [new DesignerControl("button|1", "Run", 0, 0, 40, 20)]));
    }

    [Fact]
    public void RejectsNonAsciiResourceTextInsteadOfSilentlyCorruptingIt()
    {
        Assert.Throws<ArgumentException>(() => MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "DesignerForm",
            "Café",
            [new DesignerControl("button-1", "Run", 0, 0, 40, 20)]));
    }
}
