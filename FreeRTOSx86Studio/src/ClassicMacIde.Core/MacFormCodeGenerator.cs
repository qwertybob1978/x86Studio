using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassicMacIde.Core;

public static partial class MacFormCodeGenerator
{
    private const int DialogResourceId = 128;
    private const int DialogTitleBarHeight = 20;
    private const int CompletionMarkerModuleCount = 7;
    private const int CompletionMarkerModuleSize = 3;
    private const int CompletionMarkerMargin = 8;
    private static readonly string[] CompletionMarker =
    [
        "1111111",
        "1000001",
        "1011101",
        "1010101",
        "1011101",
        "1000001",
        "1111111",
    ];
    private const string MacStudioDebugApiSource =
        """
        static const unsigned char gMacStudioAgentName[] = {
            14, 'M', 'a', 'c', 'S', 't', 'u', 'd', 'i', 'o', 'A', 'g', 'e', 'n', 't'
        };

        static OSErr MacStudioFindAgent(ProcessSerialNumber *agentProcess)
        {
            ProcessSerialNumber currentProcess;
            unsigned char processName[64];
            ProcessInfoRec processInfo;
            OSErr error;

            if (agentProcess == NULL)
                return paramErr;

            memset(&currentProcess, 0, sizeof(currentProcess));
            for (;;)
            {
                error = GetNextProcess(&currentProcess);
                if (error != noErr)
                    return error;

                memset(processName, 0, sizeof(processName));
                memset(&processInfo, 0, sizeof(processInfo));
                processInfo.processInfoLength = sizeof(processInfo);
                processInfo.processName = (StringPtr)processName;
                error = GetProcessInformation(&currentProcess, &processInfo);
                if (error != noErr)
                    return error;
                if (processName[0] == 14 &&
                    memcmp(processName, gMacStudioAgentName, sizeof(gMacStudioAgentName)) == 0)
                {
                    *agentProcess = currentProcess;
                    return noErr;
                }
            }
        }

        OSErr MacStudioDebugLog(unsigned char severity, const char *message)
        {
            unsigned char request[515];
            unsigned short length = 0;
            ProcessSerialNumber agentProcess;
            AEAddressDesc target;
            AppleEvent event;
            AppleEvent reply;
            OSErr error;
            OSErr disposeError;
            Boolean targetCreated = false;
            Boolean eventCreated = false;
            Boolean replyCreated = false;

            if (message == NULL)
                return paramErr;
            while (length <= 512 && message[length] != '\0')
                ++length;
            if (length > 512)
                return paramErr;
            for (unsigned short index = 0; index < length; ++index)
            {
                unsigned char value = (unsigned char)message[index];
                if (value != '\t' && (value < 0x20 || value > 0x7E))
                    return paramErr;
            }

            error = MacStudioFindAgent(&agentProcess);
            if (error != noErr)
                return error;

            memset(&target, 0, sizeof(target));
            memset(&event, 0, sizeof(event));
            memset(&reply, 0, sizeof(reply));
            error = AECreateDesc(
                typeProcessSerialNumber,
                &agentProcess,
                sizeof(agentProcess),
                &target);
            if (error != noErr)
                goto cleanup;
            targetCreated = true;
            error = AECreateAppleEvent(
                'MSAG',
                'DBUG',
                &target,
                kAutoGenerateReturnID,
                kAnyTransactionID,
                &event);
            if (error != noErr)
                goto cleanup;
            eventCreated = true;

            request[0] = severity;
            request[1] = (unsigned char)(length >> 8);
            request[2] = (unsigned char)length;
            if (length != 0)
                memcpy(request + 3, message, length);
            error = AEPutParamPtr(
                &event,
                keyDirectObject,
                typeChar,
                request,
                3 + length);
            if (error != noErr)
                goto cleanup;
            error = AESend(
                &event,
                &reply,
                kAEWaitReply,
                kAENormalPriority,
                kAEDefaultTimeout,
                (IdleUPP)0,
                (EventFilterUPP)0);
            if (error == noErr)
                replyCreated = true;

        cleanup:
            if (replyCreated)
            {
                disposeError = AEDisposeDesc((AEDesc *)&reply);
                if (error == noErr)
                    error = disposeError;
            }
            if (eventCreated)
            {
                disposeError = AEDisposeDesc((AEDesc *)&event);
                if (error == noErr)
                    error = disposeError;
            }
            if (targetCreated)
            {
                disposeError = AEDisposeDesc((AEDesc *)&target);
                if (error == noErr)
                    error = disposeError;
            }
            return error;
        }
        """;

    public static GeneratedMacForm Generate(
        MacTargetProfile profile,
        string appName,
        string formTitle,
        IReadOnlyList<DesignerControl> controls) =>
        Generate(
            profile,
            appName,
            formTitle,
            "MainForm",
            controls,
            ClassicMacWidgetCatalog.CreateRegistry());

    /// <summary>Generates a Macintosh application form using the supplied widget catalog.</summary>
    public static GeneratedMacForm Generate(
        MacTargetProfile profile,
        string appName,
        string formTitle,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry)
        => Generate(profile, appName, formTitle, "MainForm", controls, widgetRegistry);

    /// <summary>Generates a form with the requested C type name for its main dialog handle.</summary>
    public static GeneratedMacForm Generate(
        MacTargetProfile profile,
        string appName,
        string formTitle,
        string formTypeName,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry,
        IReadOnlyList<DesignerNonVisualComponent>? components = null,
        DesignerNonVisualComponentRegistry? componentRegistry = null,
        MacFormProperties? formProperties = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentException.ThrowIfNullOrWhiteSpace(formTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(formTypeName);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(widgetRegistry);
        components ??= Array.Empty<DesignerNonVisualComponent>();
        componentRegistry ??= ClassicMacNonVisualComponentCatalog.CreateRegistry();
        formProperties ??= new MacFormProperties();
        formProperties.ValidateFor(profile);

        if (!AppNamePattern().IsMatch(appName))
        {
            throw new ArgumentException(
                "Application name must start with a letter and contain only letters, digits, or underscores.",
                nameof(appName));
        }

        if (!ClassicMacCIdentifier.IsValid(formTypeName))
        {
            throw new ArgumentException(
                "Form type name must be a C identifier with at most 31 letters, digits, or underscores.",
                nameof(formTypeName));
        }

        if (controls.Count == 0)
        {
            throw new ArgumentException(
                "Add at least one widget before building the form.",
                nameof(controls));
        }

        if (controls.Count > short.MaxValue)
        {
            throw new ArgumentException(
                "The form exceeds the Classic Macintosh dialog item limit.",
                nameof(controls));
        }

        var clientWidth = formProperties.GetClientWidth(profile);
        var clientHeight = formProperties.GetClientHeight(profile);
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controls)
        {
            ArgumentNullException.ThrowIfNull(control);
            widgetRegistry.ValidateControl(control);
            if (!MacTestBridgeProtocol.IsValidControlId(control.Id))
            {
                throw new ArgumentException(
                    $"Control ID '{control.Id}' is not valid for the generated application and test protocol.",
                    nameof(controls));
            }
            if (!uniqueIds.Add(control.Id))
            {
                throw new ArgumentException(
                    $"Control ID '{control.Id}' is duplicated.",
                    nameof(controls));
            }

            if (!control.FitsWithin(clientWidth, clientHeight))
            {
                throw new ArgumentException(
                    $"Control '{control.Id}' is outside the {clientWidth} x {clientHeight} pixel form area.",
                    nameof(controls));
            }

            ValidateRezString(control.Text, nameof(controls));
        }

        foreach (var component in components)
        {
            ArgumentNullException.ThrowIfNull(component);
            componentRegistry.ValidateComponent(component);
            if (!uniqueIds.Add(component.Id))
            {
                throw new ArgumentException(
                    $"Control or component ID '{component.Id}' is duplicated.",
                    nameof(components));
            }

            if (component.ComponentTypeId == "menu-item")
            {
                var menuTitle = component.Properties.GetValueOrDefault("menuTitle") ?? string.Empty;
                var caption = component.Properties.GetValueOrDefault("caption") ?? string.Empty;
                ValidateRezString(menuTitle, nameof(components));
                ValidateRezString(caption, nameof(components));
                if (string.IsNullOrWhiteSpace(menuTitle) ||
                    string.IsNullOrWhiteSpace(caption))
                {
                    throw new ArgumentException(
                        $"Menu component '{component.Id}' requires a menu title and caption.",
                        nameof(components));
                }
            }
        }

        var duplicateSerialPort = components
            .Where(component => component.ComponentTypeId == "serial-port")
            .GroupBy(
                component => component.Properties.GetValueOrDefault("port") ?? "Modem",
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSerialPort is not null)
        {
            throw new ArgumentException(
                $"Only one serial component may be assigned to the {duplicateSerialPort.Key} port.",
                nameof(components));
        }

        var serialPortIds = components
            .Where(component => component.ComponentTypeId == "serial-port")
            .Select(component => component.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var transfer in components.Where(
                     component => component.ComponentTypeId == "protocol-transfer"))
        {
            var serialPortId = transfer.Properties.GetValueOrDefault("serialPortId");
            if (string.IsNullOrWhiteSpace(serialPortId))
            {
                throw new ArgumentException(
                    $"Protocol component '{transfer.Id}' requires a serial component reference.",
                    nameof(components));
            }
            if (!serialPortIds.Contains(serialPortId))
            {
                throw new ArgumentException(
                    $"Protocol component '{transfer.Id}' references unknown serial component '{serialPortId}'.",
                    nameof(components));
            }
            if (transfer.Properties.GetValueOrDefault("protocol") == "Zmodem")
            {
                var fileName = transfer.Properties.GetValueOrDefault("fileName");
                if (string.IsNullOrWhiteSpace(fileName) ||
                    fileName.Length > 63 ||
                    fileName.Any(character => character < 0x20 || character > 0x7E))
                {
                    throw new ArgumentException(
                        $"ZMODEM component '{transfer.Id}' requires a printable ASCII file name of at most 63 characters.",
                        nameof(components));
                }
            }
        }

        var selectedRadioGroups = controls
            .Where(control =>
                control.WidgetTypeId == "radio-button" &&
                bool.TryParse(
                    control.Properties.GetValueOrDefault("selected"),
                    out var selected) &&
                selected)
            .GroupBy(
                control => control.Properties.GetValueOrDefault("group") ?? "default",
                StringComparer.Ordinal);
        var duplicateRadioGroup = selectedRadioGroups.FirstOrDefault(group => group.Count() > 1);
        if (duplicateRadioGroup is not null)
        {
            throw new ArgumentException(
                $"Radio group '{duplicateRadioGroup.Key}' can have at most one initially selected item.",
                nameof(controls));
        }

        ValidateRezString(formTitle, nameof(formTitle));

        return new GeneratedMacForm(
            GenerateCSource(
                profile,
                formTitle,
                formTypeName,
                controls,
                widgetRegistry,
                components,
                componentRegistry,
                formProperties),
            GenerateHeaderSource(
                formTypeName,
                controls,
                widgetRegistry,
                profile,
                components,
                componentRegistry),
            GenerateResourceSource(
                profile,
                appName,
                formTitle,
                formTypeName,
                controls,
                widgetRegistry,
                components,
                componentRegistry,
                formProperties),
            GenerateCMakeLists(appName),
            CreateMainSource());
    }

    /// <summary>Returns whether a name can be used as the generated form's C type.</summary>
    public static bool IsValidFormTypeName(string? formTypeName) =>
        ClassicMacCIdentifier.IsValid(formTypeName);

    /// <summary>Creates the editable C entry-point and form-initialization hook template.</summary>
    public static string CreateMainSource() =>
        """
        #include "designer_form.h"

        int main(void)
        {
            return MacStudioRunForm();
        }

        void MacStudioFormDidLoad(DialogPtr form)
        {
            (void)form;
        }
        """;

    private static string GenerateCSource(
        MacTargetProfile profile,
        string formTitle,
        string formTypeName,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry,
        MacFormProperties formProperties)
    {
        var titleBarHeight = formProperties.ShowTitleBar ? DialogTitleBarHeight : 0;
        var source = new StringBuilder();
        source.AppendLine("#include <Dialogs.h>");
        source.AppendLine("#include <Events.h>");
        source.AppendLine("#include <AppleEvents.h>");
        source.AppendLine("#include <Files.h>");
        source.AppendLine("#include <Fonts.h>");
        source.AppendLine("#include <Menus.h>");
        source.AppendLine("#include <Multiverse.h>");
        source.AppendLine("#include <Processes.h>");
        source.AppendLine("#include <Quickdraw.h>");
        source.AppendLine("#include <TextEdit.h>");
        source.AppendLine("#include <Windows.h>");
        source.AppendLine("#include <string.h>");
        source.AppendLine("#include \"designer_form.h\"");
        source.AppendLine();
        source.AppendLine("static void DispatchUserControlEvent(DialogPtr dialog, short item);");
        source.AppendLine(
            "static void DispatchNonVisualComponentEvent(DialogPtr dialog, short componentIndex, short eventIndex);");
        source.AppendLine("static void BridgeFinish(void);");
        source.AppendLine("static int HandleDesignerControl(short item);");
        source.AppendLine("static void ApplyRadioGroupSelection(short item);");
        source.AppendLine("static void CleanupDesignerWidgets(DialogPtr dialog);");
        source.AppendLine("static Handle DesignerDialogItem(DialogPtr dialog, short item);");
        source.AppendLine("static void InvalidateDesignerDialogItem(DialogPtr dialog, short item);");
        source.AppendLine("static DialogPtr gDesignerDialog = NULL;");
        source.AppendLine("static short gTelemetryFile = -1;");
        source.AppendLine("static short gTelemetryVolumeRefNum = 0;");
        source.AppendLine("static int gTelemetryFailed = 0;");
        source.AppendLine("static int gTestFailed = 0;");
        source.AppendLine("static int gTestSession = 0;");
        source.AppendLine(MacStudioDebugApiSource);
        AppendNonVisualComponentRuntimeDeclarations(source, components);
        source.AppendLine();
        source.AppendLine("static void DispatchUserControlEvent(DialogPtr dialog, short item)");
        source.AppendLine("{");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var definition = widgetRegistry.GetRequired(control.WidgetTypeId);
            if (!definition.IsInteractive)
            {
                continue;
            }

            source.AppendLine($"        case {index + 1}:");
            if (control.Events.TryGetValue("OnClick", out var clickHandler))
            {
                source.AppendLine(
                    $"            {clickHandler}(({formTypeName})dialog, item);");
            }
            else
            {
                source.AppendLine(
                    $"            MacStudioUserControlClicked(dialog, MACSTUDIO_ID_{ComponentAccessorSuffix(control.Id).ToUpperInvariant()}, item);");
            }
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("}");
        AppendNonVisualComponentEventDispatcher(
            source,
            formTypeName,
            components,
            componentRegistry);
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendDeclarations(
                source,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
        }
        if (controls.Any(control =>
                control.Events.Keys.Any(eventId => eventId != "OnClick")))
        {
            throw new NotSupportedException(
                "The Macintosh runtime currently generates OnClick event bindings only.");
        }
        source.AppendLine();
        source.AppendLine("static int BridgeWriteRaw(const char *text)");
        source.AppendLine("{");
        source.AppendLine("    long count = (long)strlen(text);");
        source.AppendLine("    if (gTelemetryFile < 0 || gTelemetryFailed)");
        source.AppendLine("        return 0;");
        source.AppendLine("    if (FSWrite(gTelemetryFile, &count, text) != noErr ||");
        source.AppendLine("        count != (long)strlen(text))");
        source.AppendLine("    {");
        source.AppendLine("        gTelemetryFailed = 1;");
        source.AppendLine("        return 0;");
        source.AppendLine("    }");
        source.AppendLine("    return 1;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeWriteField(const char *text)");
        source.AppendLine("{");
        source.AppendLine("    static const char hex[] = \"0123456789ABCDEF\";");
        source.AppendLine("    char encoded[256];");
        source.AppendLine("    short encodedLength = 0;");
        source.AppendLine("    while (*text != '\\0')");
        source.AppendLine("    {");
        source.AppendLine("        unsigned char value = (unsigned char)*text++;");
        source.AppendLine("        short sequenceLength;");
        source.AppendLine("        if ((value >= 'A' && value <= 'Z') ||");
        source.AppendLine("            (value >= 'a' && value <= 'z') ||");
        source.AppendLine("            (value >= '0' && value <= '9') ||");
        source.AppendLine("            value == '.' || value == '_' || value == '-')");
        source.AppendLine("        {");
        source.AppendLine("            sequenceLength = 1;");
        source.AppendLine("        }");
        source.AppendLine("        else");
        source.AppendLine("        {");
        source.AppendLine("            sequenceLength = 3;");
        source.AppendLine("        }");
        source.AppendLine("        if (encodedLength + sequenceLength >= sizeof(encoded))");
        source.AppendLine("        {");
        source.AppendLine("            encoded[encodedLength] = '\\0';");
        source.AppendLine("            if (!BridgeWriteRaw(encoded))");
        source.AppendLine("                return;");
        source.AppendLine("            encodedLength = 0;");
        source.AppendLine("        }");
        source.AppendLine("        if (sequenceLength == 1)");
        source.AppendLine("            encoded[encodedLength++] = (char)value;");
        source.AppendLine("        else");
        source.AppendLine("        {");
        source.AppendLine("            encoded[encodedLength++] = '%';");
        source.AppendLine("            encoded[encodedLength++] = hex[value >> 4];");
        source.AppendLine("            encoded[encodedLength++] = hex[value & 15];");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("    if (encodedLength > 0)");
        source.AppendLine("    {");
        source.AppendLine("        encoded[encodedLength] = '\\0';");
        source.AppendLine("        BridgeWriteRaw(encoded);");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeWriteNumber(int value)");
        source.AppendLine("{");
        source.AppendLine("    char digits[12];");
        source.AppendLine("    int cursor = 11;");
        source.AppendLine("    digits[cursor] = '\\0';");
        source.AppendLine("    do");
        source.AppendLine("    {");
        source.AppendLine("        digits[--cursor] = (char)('0' + (value % 10));");
        source.AppendLine("        value /= 10;");
        source.AppendLine("    } while (value > 0 && cursor > 0);");
        source.AppendLine("    BridgeWriteRaw(&digits[cursor]);");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeWriteLine(void)");
        source.AppendLine("{");
        source.AppendLine("    BridgeWriteRaw(\"\\r\");");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeWriteError(const char *code, const char *message)");
        source.AppendLine("{");
        source.AppendLine("    BridgeWriteRaw(\"ERROR|\");");
        source.AppendLine("    BridgeWriteField(code);");
        source.AppendLine("    BridgeWriteRaw(\"|\");");
        source.AppendLine("    BridgeWriteField(message);");
        source.AppendLine("    BridgeWriteLine();");
        source.AppendLine("    gTestFailed = 1;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeWriteEvent(const char *action, const char *id, const char *outcome)");
        source.AppendLine("{");
        source.AppendLine("    BridgeWriteRaw(\"EVENT|\");");
        source.AppendLine("    BridgeWriteField(action);");
        source.AppendLine("    BridgeWriteRaw(\"|\");");
        source.AppendLine("    BridgeWriteField(id);");
        source.AppendLine("    BridgeWriteRaw(\"|\");");
        source.AppendLine("    BridgeWriteField(outcome);");
        source.AppendLine("    BridgeWriteLine();");
        source.AppendLine("    if (strcmp(outcome, \"HANDLED\") != 0)");
        source.AppendLine("        gTestFailed = 1;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static Handle DesignerDialogItem(DialogPtr dialog, short item)");
        source.AppendLine("{");
        source.AppendLine("    short itemType;");
        source.AppendLine("    Handle itemHandle;");
        source.AppendLine("    Rect itemBounds;");
        source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &itemBounds);");
        source.AppendLine("    return itemHandle;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static short BridgeControlItem(const char *id)");
        source.AppendLine("{");
        for (var index = 0; index < controls.Count; index++)
        {
            source.AppendLine(
                $"    if (strcmp(id, \"{EscapeCString(controls[index].Id)}\") == 0)");
            source.AppendLine($"        return {index + 1};");
        }
        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static const char *BridgeControlId(short item)");
        source.AppendLine("{");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        for (var index = 0; index < controls.Count; index++)
        {
            source.AppendLine($"        case {index + 1}:");
            source.AppendLine(
                $"            return \"{EscapeCString(controls[index].Id)}\";");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return \"\";");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        AppendPropertyAccessorFunctions(
            source,
            profile,
            controls,
            widgetRegistry,
            titleBarHeight);
        source.AppendLine("static int BridgeStart(void)");
        source.AppendLine("{");
        source.AppendLine("    ProcessSerialNumber processNumber;");
        source.AppendLine("    ProcessInfoRec processInfo;");
        source.AppendLine("    Str255 processName;");
        source.AppendLine("    FSSpec appSpec;");
        source.AppendLine("    FSSpec telemetrySpec;");
        source.AppendLine("    unsigned char telemetryName[256];");
        source.AppendLine("    const char *telemetryFileName = \"MacStudio.out\";");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    error = GetCurrentProcess(&processNumber);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        return 0;");
        source.AppendLine("    memset(&processInfo, 0, sizeof(processInfo));");
        source.AppendLine("    processInfo.processInfoLength = sizeof(processInfo);");
        source.AppendLine("    processInfo.processName = processName;");
        source.AppendLine("    processInfo.processAppSpec = &appSpec;");
        source.AppendLine("    error = GetProcessInformation(&processNumber, &processInfo);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        return 0;");
        source.AppendLine("    telemetryName[0] = (unsigned char)strlen(telemetryFileName);");
        source.AppendLine("    memcpy(&telemetryName[1], telemetryFileName, telemetryName[0]);");
        source.AppendLine("    error = FSMakeFSSpec(appSpec.vRefNum, 2, (ConstStringPtr)telemetryName, &telemetrySpec);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("    {");
        source.AppendLine("        error = FSpDelete(&telemetrySpec);");
        source.AppendLine("        if (error != noErr)");
        source.AppendLine("            return 0;");
        source.AppendLine("    }");
        source.AppendLine("    else if (error != fnfErr)");
        source.AppendLine("        return 0;");
        source.AppendLine("    error = FSpCreate(&telemetrySpec, 'MIDE', 'TEXT', smSystemScript);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        return 0;");
        source.AppendLine("    error = FSpOpenDF(&telemetrySpec, fsWrPerm, &gTelemetryFile);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        return 0;");
        source.AppendLine("    gTelemetryVolumeRefNum = telemetrySpec.vRefNum;");
        source.AppendLine(
            $"    BridgeWriteRaw(\"{MacTestBridgeProtocol.VersionHeader}\\rREADY\\r\");");
        source.AppendLine("    return !gTelemetryFailed;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeWriteMetadata(void)");
        source.AppendLine("{");
        source.AppendLine("    Rect bounds;");
        source.AppendLine("    BridgeWriteRaw(\"FORM|\");");
        source.AppendLine(
            $"    BridgeWriteField(\"{EscapeCString(formTitle)}\");");
        source.AppendLine("    BridgeWriteLine();");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            source.AppendLine("    BridgeWriteRaw(\"CONTROL|\");");
            source.AppendLine(
                $"    BridgeWriteField(\"{EscapeCString(control.Id)}\");");
            source.AppendLine("    BridgeWriteRaw(\"|\");");
            source.AppendLine(
                $"    BridgeWriteField(\"{EscapeCString(control.WidgetTypeId)}\");");
            source.AppendLine("    BridgeWriteRaw(\"|\");");
            source.AppendLine(
                $"    BridgeWriteField(\"{EscapeCString(control.Text)}\");");
            source.AppendLine("    BridgeWriteRaw(\"|\");");
            source.AppendLine(
                $"    if (MacStudioGetControlBounds(gDesignerDialog, \"{EscapeCString(control.Id)}\", &bounds) != noErr)");
            source.AppendLine("    {");
            source.AppendLine("        BridgeWriteError(\"BOUNDS\", \"Could not read runtime control bounds\");");
            source.AppendLine("        return;");
            source.AppendLine("    }");
            source.AppendLine("    BridgeWriteNumber(bounds.left);");
            source.AppendLine("    BridgeWriteRaw(\"|\");");
            source.AppendLine("    BridgeWriteNumber(bounds.top);");
            source.AppendLine("    BridgeWriteRaw(\"|\");");
            source.AppendLine("    BridgeWriteNumber(bounds.right - bounds.left);");
            source.AppendLine("    BridgeWriteRaw(\"|\");");
            source.AppendLine("    BridgeWriteNumber(bounds.bottom - bounds.top);");
            source.AppendLine("    BridgeWriteLine();");
        }
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static int BridgeProcessRequest(void)");
        source.AppendLine("{");
        source.AppendLine("    ProcessSerialNumber processNumber;");
        source.AppendLine("    ProcessInfoRec processInfo;");
        source.AppendLine("    Str255 processName;");
        source.AppendLine("    FSSpec appSpec;");
        source.AppendLine("    FSSpec requestSpec;");
        source.AppendLine("    unsigned char requestName[256];");
        source.AppendLine("    const char *requestFileName = \"MacStudio.in\";");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    short requestFile;");
        source.AppendLine("    long requestLength;");
        source.AppendLine("    long bytesRead;");
        source.AppendLine("    char request[4097];");
        source.AppendLine("    char *line;");
        source.AppendLine("    int quitSeen = 0;");
        source.AppendLine("    error = GetCurrentProcess(&processNumber);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"PROCESS_INFO\", \"Could not identify the application volume\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    memset(&processInfo, 0, sizeof(processInfo));");
        source.AppendLine("    processInfo.processInfoLength = sizeof(processInfo);");
        source.AppendLine("    processInfo.processName = processName;");
        source.AppendLine("    processInfo.processAppSpec = &appSpec;");
        source.AppendLine("    error = GetProcessInformation(&processNumber, &processInfo);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"PROCESS_INFO\", \"Could not locate the application volume\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    requestName[0] = (unsigned char)strlen(requestFileName);");
        source.AppendLine("    memcpy(&requestName[1], requestFileName, requestName[0]);");
        source.AppendLine("    error = FSMakeFSSpec(appSpec.vRefNum, 2, (ConstStringPtr)requestName, &requestSpec);");
        source.AppendLine("    if (error == fnfErr)");
        source.AppendLine("        return 0;");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"REQUEST_FILE\", \"Could not locate the request file\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    error = FSpOpenDF(&requestSpec, fsRdPerm, &requestFile);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"REQUEST_FILE\", \"Could not open the request file\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    gTestSession = 1;");
        source.AppendLine("    error = GetEOF(requestFile, &requestLength);");
        source.AppendLine("    if (error != noErr || requestLength < 1 || requestLength > 4096)");
        source.AppendLine("    {");
        source.AppendLine("        FSClose(requestFile);");
        source.AppendLine("        BridgeWriteError(\"REQUEST_SIZE\", \"Request file must contain 1 to 4096 bytes\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    bytesRead = requestLength;");
        source.AppendLine("    error = FSRead(requestFile, &bytesRead, request);");
        source.AppendLine("    FSClose(requestFile);");
        source.AppendLine("    if (error != noErr || bytesRead != requestLength)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"REQUEST_READ\", \"Could not read the complete request file\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    request[requestLength] = '\\0';");
        source.AppendLine("    line = strtok(request, \"\\r\\n\");");
        source.AppendLine(
            $"    if (line == NULL || strcmp(line, \"{MacTestBridgeProtocol.VersionHeader}\") != 0)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"PROTOCOL\", \"Request protocol header is invalid\");");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    while ((line = strtok(NULL, \"\\r\\n\")) != NULL)");
        source.AppendLine("    {");
        source.AppendLine("        if (strcmp(line, \"QUERY_FORM\") == 0)");
        source.AppendLine("            continue;");
        source.AppendLine("        if (strncmp(line, \"CLICK|\", 6) == 0)");
        source.AppendLine("        {");
        source.AppendLine("            const char *id = line + 6;");
        source.AppendLine("            short item = BridgeControlItem(id);");
        source.AppendLine("            int result;");
        source.AppendLine("            if (item == 0)");
        source.AppendLine("            {");
        source.AppendLine("                BridgeWriteError(\"UNKNOWN_CONTROL\", id);");
        source.AppendLine("                continue;");
        source.AppendLine("            }");
        source.AppendLine("            result = HandleDesignerControl(item);");
        source.AppendLine("            if (result == 0)");
        source.AppendLine("                DispatchUserControlEvent(gDesignerDialog, item);");
        source.AppendLine("            ApplyRadioGroupSelection(item);");
        source.AppendLine("            switch (item)");
        source.AppendLine("            {");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            source.AppendLine($"                case {index + 1}:");
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendTestInteraction(
                source,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
            source.AppendLine("                    break;");
        }
        source.AppendLine("                default:");
        source.AppendLine("                    break;");
        source.AppendLine("            }");
        source.AppendLine("            BridgeWriteEvent(\"CLICK\", id, result == 0 ? \"HANDLED\" : \"UNHANDLED\");");
        source.AppendLine("            continue;");
        source.AppendLine("        }");
        source.AppendLine("        if (strcmp(line, \"QUIT\") == 0)");
        source.AppendLine("        {");
        source.AppendLine("            quitSeen = 1;");
        source.AppendLine("            if (strtok(NULL, \"\\r\\n\") != NULL)");
        source.AppendLine("                BridgeWriteError(\"PROTOCOL\", \"QUIT must be the final request command\");");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine("        BridgeWriteError(\"UNKNOWN_COMMAND\", line);");
        source.AppendLine("    }");
        source.AppendLine("    if (!quitSeen)");
        source.AppendLine("        BridgeWriteError(\"PROTOCOL\", \"Request did not contain QUIT\");");
        source.AppendLine("    return 1;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static int HandleDesignerControl(short item)");
        source.AppendLine("{");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        for (var item = 1; item <= controls.Count; item++)
        {
            source.AppendLine($"        case {item}:");
            source.AppendLine("            return 0;");
        }

        source.AppendLine("        default:");
        source.AppendLine("            return 1;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        AppendRadioGroupSelectionFunction(source, controls);
        source.AppendLine("static void CleanupDesignerWidgets(DialogPtr dialog)");
        source.AppendLine("{");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendCleanup(
                source,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
        }
        for (var index = 0; index < components.Count; index++)
        {
            componentRegistry.GetRequired(components[index].ComponentTypeId)
                .CodeGenerator.AppendCleanup(
                    source,
                    CreateComponentCodeGenerationContext(
                        profile,
                        components,
                        index + 1,
                        formTypeName));
        }
        AppendNonVisualComponentCleanup(source, components);
        source.AppendLine("}");
        source.AppendLine();
        AppendCompletionMarkerFunction(source, profile, formProperties);
        source.AppendLine("int MacStudioRunForm(void)");
        source.AppendLine("{");
        source.AppendLine("    int formRunning = 1;");
        source.AppendLine("    int bridgeStarted = 0;");
        source.AppendLine("    InitGraf(&qd.thePort);");
        source.AppendLine("    InitFonts();");
        source.AppendLine("    InitWindows();");
        source.AppendLine("    InitMenus();");
        AppendMenuInstallation(source, components);
        source.AppendLine("    TEInit();");
        source.AppendLine("    InitDialogs(NULL);");
        source.AppendLine("    InitCursor();");
        source.AppendLine();
        source.AppendLine(
            $"    DialogPtr dialog = GetNewDialog({DialogResourceId}, NULL, (WindowPtr)-1);");
        source.AppendLine("    if (dialog == NULL)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteError(\"FORM_RESOURCE\", \"Could not create the form dialog\");");
        source.AppendLine("        BridgeFinish();");
        source.AppendLine("        return 1;");
        source.AppendLine("    }");
        source.AppendLine("    gDesignerDialog = dialog;");
        source.AppendLine("    ShowWindow((WindowPtr)dialog);");
        source.AppendLine("    bridgeStarted = BridgeStart();");
        source.AppendLine();
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendInitialization(
                source,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
        }
        for (var index = 0; index < components.Count; index++)
        {
            componentRegistry.GetRequired(components[index].ComponentTypeId)
                .CodeGenerator.AppendInitialization(
                    source,
                    CreateComponentCodeGenerationContext(
                        profile,
                        components,
                        index + 1,
                        formTypeName));
        }
        source.AppendLine($"    MacStudioFormDidLoad(({formTypeName})dialog);");
        source.AppendLine("    if (bridgeStarted)");
        source.AppendLine("        BridgeWriteMetadata();");
        source.AppendLine("    if (bridgeStarted && BridgeProcessRequest())");
        source.AppendLine("    {");
        source.AppendLine("        BridgeFinish();");
        source.AppendLine("        if (gTestSession)");
        source.AppendLine("        {");
        source.AppendLine("            DrawTestCompletionMarker(dialog);");
        source.AppendLine("            EventRecord event;");
        source.AppendLine("            while (1)");
        source.AppendLine("                WaitNextEvent(everyEvent, &event, 30, NULL);");
        source.AppendLine("        }");
        source.AppendLine("        CleanupDesignerWidgets(dialog);");
        source.AppendLine("        DisposeDialog(dialog);");
        source.AppendLine("        return (gTestFailed || gTelemetryFailed) ? 1 : 0;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    EventRecord event;");
        source.AppendLine("    short item = 0;");
        source.AppendLine("    while (formRunning)");
        source.AppendLine("    {");
        source.AppendLine("        short clickedItemType;");
        source.AppendLine("        Handle clickedItemHandle;");
        source.AppendLine("        Rect clickedItemBounds;");
        source.AppendLine("        Boolean clickedControl = false;");
        source.AppendLine("        item = 0;");
        source.AppendLine("        WaitNextEvent(everyEvent, &event, 30, NULL);");
        AppendFormWindowEventHandling(source, profile, formProperties);
        AppendMenuEventLoopHandling(source, components, componentRegistry);
        AppendSerialPortEventPolling(source, components, componentRegistry);
        for (var index = 0; index < components.Count; index++)
        {
            componentRegistry.GetRequired(components[index].ComponentTypeId)
                .CodeGenerator.AppendEventLoopHandling(
                    source,
                    CreateComponentCodeGenerationContext(
                        profile,
                        components,
                        index + 1,
                        formTypeName));
        }
        source.AppendLine("        if (IsDialogEvent(&event))");
        source.AppendLine("            DialogSelect(&event, &dialog, &item);");
        source.AppendLine("        if (item > 0)");
        source.AppendLine("        {");
        source.AppendLine(
            "            GetDialogItem(dialog, item, &clickedItemType, &clickedItemHandle, &clickedItemBounds);");
        source.AppendLine("            clickedControl = (clickedItemType & ctrlItem) != 0;");
        source.AppendLine("        }");
        source.AppendLine("        if (item > 0)");
        source.AppendLine("            ApplyRadioGroupSelection(item);");
        source.AppendLine("        if (event.what == mouseDown && !clickedControl)");
        source.AppendLine("        {");
        source.AppendLine("            Point where = event.where;");
        source.AppendLine("            SetPort((GrafPtr)dialog);");
        source.AppendLine("            GlobalToLocal(&where);");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendEventHandling(
                source,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
        }
        source.AppendLine("        }");
        source.AppendLine("        if (item <= 0)");
        source.AppendLine("            continue;");
        source.AppendLine("        const char *controlId = BridgeControlId(item);");
        source.AppendLine("        int result = HandleDesignerControl(item);");
        source.AppendLine("        if (result == 0)");
        source.AppendLine("            DispatchUserControlEvent(dialog, item);");
        source.AppendLine("        ApplyRadioGroupSelection(item);");
        source.AppendLine("        if (*controlId != '\\0')");
        source.AppendLine("        {");
        source.AppendLine("            BridgeWriteEvent(\"CLICK\", controlId, result == 0 ? \"HANDLED\" : \"UNHANDLED\");");
        source.AppendLine("        }");
        source.AppendLine("        else");
        source.AppendLine("        {");
        source.AppendLine("            BridgeWriteError(\"UNKNOWN_CONTROL\", \"Dialog returned an unknown item number\");");
        source.AppendLine("        }");
        source.AppendLine("        switch (item)");
        source.AppendLine("        {");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            source.AppendLine($"            case {index + 1}:");
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendTestInteraction(
                source,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
            source.AppendLine("                break;");
        }
        source.AppendLine("            default:");
        source.AppendLine("                break;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("    CleanupDesignerWidgets(dialog);");
        source.AppendLine("    DisposeDialog(dialog);");
        source.AppendLine("    BridgeFinish();");
        source.AppendLine(
            "    return (gTestFailed || gTelemetryFailed || !bridgeStarted) ? 1 : 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void BridgeFinish(void)");
        source.AppendLine("{");
        source.AppendLine("    if (gTelemetryFile >= 0)");
        source.AppendLine("    {");
        source.AppendLine("        BridgeWriteRaw(\"DONE\\r\");");
        source.AppendLine("        FSClose(gTelemetryFile);");
        source.AppendLine("        gTelemetryFile = -1;");
        source.AppendLine("        FlushVol(0, gTelemetryVolumeRefNum);");
        source.AppendLine("    }");
        source.AppendLine("}");
        for (var index = 0; index < components.Count; index++)
        {
            componentRegistry.GetRequired(components[index].ComponentTypeId)
                .CodeGenerator.AppendSource(
                    source,
                    CreateComponentCodeGenerationContext(
                        profile,
                        components,
                        index + 1,
                        formTypeName));
        }
        AppendNonVisualComponentRuntimeSource(source, components, componentRegistry);
        return source.ToString();
    }

    private static void AppendRadioGroupSelectionFunction(
        StringBuilder source,
        IReadOnlyList<DesignerControl> controls)
    {
        source.AppendLine("static void ApplyRadioGroupSelection(short item)");
        source.AppendLine("{");
        foreach (var (control, index) in controls.Select((control, index) => (control, index)))
        {
            if (control.WidgetTypeId != "radio-button")
            {
                continue;
            }

            var group = control.Properties.GetValueOrDefault("group") ?? "default";
            source.AppendLine($"    if (item == {index + 1})");
            source.AppendLine("    {");
            foreach (var (candidate, candidateIndex) in controls
                         .Select((candidate, candidateIndex) => (candidate, candidateIndex))
                         .Where(candidate =>
                             candidate.candidate.WidgetTypeId == "radio-button" &&
                             string.Equals(
                                 candidate.candidate.Properties.GetValueOrDefault("group") ??
                                     "default",
                                 group,
                                 StringComparison.Ordinal)))
            {
                source.AppendLine("        {");
                source.AppendLine("        short itemType;");
                source.AppendLine("        Handle itemHandle;");
                source.AppendLine("        Rect itemBounds;");
                source.AppendLine(
                    $"        GetDialogItem(gDesignerDialog, {candidateIndex + 1}, &itemType, &itemHandle, &itemBounds);");
                source.AppendLine(
                    $"        SetControlValue((ControlHandle)itemHandle, item == {candidateIndex + 1} ? 1 : 0);");
                source.AppendLine("        }");
            }
            source.AppendLine("    }");
        }
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendCompletionMarkerFunction(
        StringBuilder source,
        MacTargetProfile profile,
        MacFormProperties formProperties)
    {
        var dialogClientWidth = formProperties.GetClientWidth(profile) + 2;
        var dialogClientHeight = formProperties.GetClientHeight(profile) + 2;
        var markerSize = CompletionMarkerModuleCount * CompletionMarkerModuleSize;
        var markerLeft = dialogClientWidth - markerSize - CompletionMarkerMargin;
        var markerTop = dialogClientHeight - markerSize - CompletionMarkerMargin;
        if (markerLeft < 0 || markerTop < 0)
        {
            throw new ArgumentException(
                "The target screen is too small to display the test completion marker.",
                nameof(profile));
        }

        source.AppendLine("static void DrawTestCompletionMarker(DialogPtr dialog)");
        source.AppendLine("{");
        source.AppendLine("    static const unsigned char marker[7][7] =");
        source.AppendLine("    {");
        foreach (var row in CompletionMarker)
        {
            source.AppendLine($"        {{ {string.Join(", ", row.Select(bit => bit == '1' ? "1" : "0"))} }},");
        }
        source.AppendLine("    };");
        source.AppendLine("    Rect cell;");
        source.AppendLine("    short row;");
        source.AppendLine("    short column;");
        source.AppendLine("    SetPort((GrafPtr)dialog);");
        source.AppendLine("    for (row = 0; row < 7; row++)");
        source.AppendLine("    {");
        source.AppendLine("        for (column = 0; column < 7; column++)");
        source.AppendLine("        {");
        source.AppendLine(
            $"            SetRect(&cell, {markerLeft} + column * {CompletionMarkerModuleSize}, {markerTop} + row * {CompletionMarkerModuleSize}, {markerLeft} + (column + 1) * {CompletionMarkerModuleSize}, {markerTop} + (row + 1) * {CompletionMarkerModuleSize});");
        source.AppendLine("            ForeColor(marker[row][column] ? blackColor : whiteColor);");
        source.AppendLine("            PaintRect(&cell);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendFormWindowEventHandling(
        StringBuilder source,
        MacTargetProfile profile,
        MacFormProperties formProperties)
    {
        if (!formProperties.ShowTitleBar)
        {
            return;
        }

        source.AppendLine("        if (event.what == mouseDown)");
        source.AppendLine("        {");
        source.AppendLine("            WindowPtr clickedWindow = NULL;");
        source.AppendLine("            short windowPart = FindWindow(event.where, &clickedWindow);");
        source.AppendLine("            if (clickedWindow == (WindowPtr)dialog)");
        source.AppendLine("            {");
        if (formProperties.Movable ||
            formProperties.Resizable ||
            formProperties.HasCloseBox ||
            formProperties.HasZoomBox)
        {
            source.AppendLine("                if (windowPart == inDrag)");
            source.AppendLine("                {");
            source.AppendLine(
                "                    DragWindow((WindowPtr)dialog, event.where, &qd.screenBits.bounds);");
            source.AppendLine("                    continue;");
            source.AppendLine("                }");
        }
        if (formProperties.Resizable)
        {
            source.AppendLine("                if (windowPart == inGrow)");
            source.AppendLine("                {");
            source.AppendLine("                    Rect growBounds;");
            source.AppendLine("                    long growResult;");
            source.AppendLine(
                $"                    SetRect(&growBounds, 100, 60, {profile.ScreenWidth}, {profile.ScreenHeight});");
            source.AppendLine(
                "                    growResult = GrowWindow((WindowPtr)dialog, event.where, &growBounds);");
            source.AppendLine("                    if (growResult != 0)");
            source.AppendLine(
                "                        SizeWindow((WindowPtr)dialog, LoWord(growResult), HiWord(growResult), true);");
            source.AppendLine("                    continue;");
            source.AppendLine("                }");
        }
        if (formProperties.HasZoomBox)
        {
            source.AppendLine(
                "                if (windowPart == inZoomIn || windowPart == inZoomOut)");
            source.AppendLine("                {");
            source.AppendLine(
                "                    if (TrackBox((WindowPtr)dialog, event.where, windowPart))");
            source.AppendLine(
                "                        ZoomWindow((WindowPtr)dialog, windowPart, true);");
            source.AppendLine("                    continue;");
            source.AppendLine("                }");
        }
        if (formProperties.HasCloseBox)
        {
            source.AppendLine("                if (windowPart == inGoAway)");
            source.AppendLine("                {");
            source.AppendLine(
                "                    if (TrackGoAway((WindowPtr)dialog, event.where))");
            source.AppendLine("                        formRunning = 0;");
            source.AppendLine("                    continue;");
            source.AppendLine("                }");
        }
        source.AppendLine("            }");
        source.AppendLine("        }");
    }

    private static string GenerateHeaderSource(
        string formTypeName,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry,
        MacTargetProfile profile,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry)
    {
        var source = new StringBuilder();
        source.AppendLine("#ifndef MACSTUDIO_DESIGNER_FORM_H");
        source.AppendLine("#define MACSTUDIO_DESIGNER_FORM_H");
        source.AppendLine();
        source.AppendLine("#include <Dialogs.h>");
        source.AppendLine("#include <Quickdraw.h>");
        source.AppendLine();
        source.AppendLine($"typedef DialogPtr {formTypeName};");
        source.AppendLine();
        source.AppendLine("/** Runs the generated form event loop and returns its result. */");
        source.AppendLine("int MacStudioRunForm(void);");
        source.AppendLine("/** Sends a bounded printable debug record to the running MacStudio Agent. */");
        source.AppendLine("OSErr MacStudioDebugLog(unsigned char severity, const char *message);");
        source.AppendLine("/** Called after the form and all designer widgets are initialized. */");
        source.AppendLine($"void MacStudioFormDidLoad({formTypeName} form);");
        source.AppendLine();
        source.AppendLine("/** Reads Pascal text from a text-bearing control by stable ID. */");
        source.AppendLine(
            "short MacStudioGetControlText(DialogPtr dialog, const char *controlId, unsigned char *text);");
        source.AppendLine("/** Updates Pascal text on a text-bearing control by stable ID. */");
        source.AppendLine(
            "short MacStudioSetControlText(DialogPtr dialog, const char *controlId, const unsigned char *text);");
        source.AppendLine("/** Gets a checkbox/radio state, popup/list selection, or scrollbar value. */");
        source.AppendLine(
            "short MacStudioGetControlValue(DialogPtr dialog, const char *controlId, short *value);");
        source.AppendLine("/** Sets a checkbox/radio state, popup/list selection, or scrollbar value. */");
        source.AppendLine(
            "short MacStudioSetControlValue(DialogPtr dialog, const char *controlId, short value);");
        source.AppendLine("/** Gets control bounds relative to the form's content area. */");
        source.AppendLine(
            "short MacStudioGetControlBounds(DialogPtr dialog, const char *controlId, Rect *bounds);");
        source.AppendLine("/** Sets control bounds relative to the form's content area. */");
        source.AppendLine(
            "short MacStudioSetControlBounds(DialogPtr dialog, const char *controlId, const Rect *bounds);");
        source.AppendLine();

        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var definition = widgetRegistry.GetRequired(control.WidgetTypeId);
            var suffix = ComponentAccessorSuffix(control.Id);
            source.AppendLine(
                $"#define MACSTUDIO_ID_{suffix.ToUpperInvariant()} \"{EscapeCString(control.Id)}\"");
            source.AppendLine(
                $"/** Stable-ID accessor set for the {definition.DisplayName} instance '{control.Id}'. */");
            source.AppendLine(
                $"short MacStudioGet{suffix}Bounds(DialogPtr dialog, Rect *bounds);");
            source.AppendLine(
                $"short MacStudioSet{suffix}Bounds(DialogPtr dialog, const Rect *bounds);");
            if (definition.SupportsTextAccess)
            {
                source.AppendLine(
                    $"short MacStudioGet{suffix}Text(DialogPtr dialog, unsigned char *text);");
                source.AppendLine(
                    $"short MacStudioSet{suffix}Text(DialogPtr dialog, const unsigned char *text);");
            }
            if (definition.SupportsValueAccess)
            {
                source.AppendLine(
                    $"short MacStudioGet{suffix}Value(DialogPtr dialog, short *value);");
                source.AppendLine(
                    $"short MacStudioSet{suffix}Value(DialogPtr dialog, short value);");
            }
            source.AppendLine();
        }
        if (controls.Any(control => control.WidgetTypeId == "list-box"))
        {
            source.AppendLine(
                "/** Returns the selected list row (zero-based), or -1 if none is selected. */");
            source.AppendLine(
                "short MacStudioGetListBoxSelection(DialogPtr dialog, const char *controlId, short *row);");
            source.AppendLine(
                "/** Replaces list-box rows with Pascal strings; itemCount is limited to 128. */");
            source.AppendLine(
                "short MacStudioSetListBoxItems(DialogPtr dialog, const char *controlId, const unsigned char *const *items, short itemCount);");
            source.AppendLine();
        }

        for (var index = 0; index < components.Count; index++)
        {
            componentRegistry.GetRequired(components[index].ComponentTypeId)
                .CodeGenerator.AppendHeaderDeclarations(
                    source,
                    CreateComponentCodeGenerationContext(
                        profile,
                        components,
                        index + 1,
                        formTypeName));
        }
        AppendNonVisualComponentHeaderDeclarations(source, components);
        foreach (var component in components)
        {
            source.AppendLine(
                $"#define MACSTUDIO_COMPONENT_{component.Id.ToUpperInvariant()} \"{EscapeCString(component.Id)}\"");
        }
        if (components.Count > 0)
        {
            source.AppendLine();
        }

        foreach (var handlerName in controls
                     .SelectMany(control => control.Events.Values)
                     .Concat(components.SelectMany(component => component.Events.Values))
                     .Distinct(StringComparer.Ordinal))
        {
            source.AppendLine($"void {handlerName}({formTypeName} form, short item);");
        }
        if (controls.Any(control => control.Events.Count > 0) ||
            components.Any(component => component.Events.Count > 0))
        {
            source.AppendLine();
        }

        source.AppendLine(
            $"void MacStudioUserControlClicked({formTypeName} form, const char *controlId, short item);");
        source.AppendLine();
        source.AppendLine("#endif");
        return source.ToString();
    }

    private static void AppendControlBoundsAccessor(
        StringBuilder source,
        int titleBarHeight)
    {
        source.AppendLine("short MacStudioGetControlBounds(DialogPtr dialog, const char *controlId, Rect *bounds)");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        source.AppendLine("    short itemType;");
        source.AppendLine("    Handle itemHandle;");
        source.AppendLine("    if (dialog == NULL || controlId == NULL || bounds == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    if (item == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, bounds);");
        source.AppendLine($"    bounds->top -= {titleBarHeight};");
        source.AppendLine($"    bounds->bottom -= {titleBarHeight};");
        source.AppendLine("    return noErr;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendControlBoundsSetter(
        StringBuilder source,
        MacTargetProfile profile,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry,
        int titleBarHeight)
    {
        source.AppendLine("short MacStudioSetControlBounds(DialogPtr dialog, const char *controlId, const Rect *bounds)");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        source.AppendLine("    short itemType;");
        source.AppendLine("    Handle itemHandle;");
        source.AppendLine("    Rect oldBounds;");
        source.AppendLine("    Rect itemBounds;");
        source.AppendLine("    GrafPtr previousPort;");
        source.AppendLine("    short result = noErr;");
        source.AppendLine("    if (dialog == NULL || controlId == NULL || bounds == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine(
            $"    if (bounds->left < 0 || bounds->top < 0 || bounds->right <= bounds->left || bounds->bottom <= bounds->top || bounds->right > {profile.ScreenWidth - 50} || bounds->bottom > {profile.ScreenHeight - 80})");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    if (item == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &oldBounds);");
        source.AppendLine("    if (itemHandle == NULL && (itemType & ctrlItem) != 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    itemBounds = *bounds;");
        source.AppendLine($"    itemBounds.top += {titleBarHeight};");
        source.AppendLine($"    itemBounds.bottom += {titleBarHeight};");
        source.AppendLine("    GetPort(&previousPort);");
        source.AppendLine("    SetPort((GrafPtr)dialog);");
        source.AppendLine("    if ((itemType & ctrlItem) != 0)");
        source.AppendLine("    {");
        source.AppendLine(
            "        MoveControl((ControlHandle)itemHandle, itemBounds.left, itemBounds.top);");
        source.AppendLine(
            "        SizeControl((ControlHandle)itemHandle, (short)(itemBounds.right - itemBounds.left), (short)(itemBounds.bottom - itemBounds.top));");
        source.AppendLine(
            "        SetDialogItem(dialog, item, itemType, itemHandle, &itemBounds);");
        source.AppendLine("    }");
        source.AppendLine("    else");
        source.AppendLine("        SetDialogItem(dialog, item, itemType, itemHandle, &itemBounds);");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            source.AppendLine($"        case {index + 1}:");
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator
                .AppendControlBoundsSetter(
                    source,
                    CreateCodeGenerationContext(
                        profile,
                        control,
                        index + 1,
                        index == controls.Count - 1,
                        titleBarHeight));
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            result = paramErr;");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("    if (result == noErr)");
        source.AppendLine("    {");
        source.AppendLine("        InvalRect(&oldBounds);");
        source.AppendLine("        InvalRect(&itemBounds);");
        source.AppendLine("    }");
        source.AppendLine("    SetPort(previousPort);");
        source.AppendLine("    return result;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendNamedControlAccessors(
        StringBuilder source,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry)
    {
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var definition = widgetRegistry.GetRequired(control.WidgetTypeId);
            var suffix = ComponentAccessorSuffix(control.Id);
            var idMacro = $"MACSTUDIO_ID_{suffix.ToUpperInvariant()}";
            source.AppendLine(
                $"short MacStudioGet{suffix}Bounds(DialogPtr dialog, Rect *bounds)");
            source.AppendLine("{");
            source.AppendLine(
                $"    return MacStudioGetControlBounds(dialog, {idMacro}, bounds);");
            source.AppendLine("}");
            source.AppendLine();
            source.AppendLine(
                $"short MacStudioSet{suffix}Bounds(DialogPtr dialog, const Rect *bounds)");
            source.AppendLine("{");
            source.AppendLine(
                $"    return MacStudioSetControlBounds(dialog, {idMacro}, bounds);");
            source.AppendLine("}");
            source.AppendLine();
            if (definition.SupportsTextAccess)
            {
                source.AppendLine(
                    $"short MacStudioGet{suffix}Text(DialogPtr dialog, unsigned char *text)");
                source.AppendLine("{");
                source.AppendLine(
                    $"    return MacStudioGetControlText(dialog, {idMacro}, text);");
                source.AppendLine("}");
                source.AppendLine();
                source.AppendLine(
                    $"short MacStudioSet{suffix}Text(DialogPtr dialog, const unsigned char *text)");
                source.AppendLine("{");
                source.AppendLine(
                    $"    return MacStudioSetControlText(dialog, {idMacro}, text);");
                source.AppendLine("}");
                source.AppendLine();
            }
            if (definition.SupportsValueAccess)
            {
                source.AppendLine(
                    $"short MacStudioGet{suffix}Value(DialogPtr dialog, short *value)");
                source.AppendLine("{");
                source.AppendLine(
                    $"    return MacStudioGetControlValue(dialog, {idMacro}, value);");
                source.AppendLine("}");
                source.AppendLine();
                source.AppendLine(
                    $"short MacStudioSet{suffix}Value(DialogPtr dialog, short value)");
                source.AppendLine("{");
                source.AppendLine(
                    $"    return MacStudioSetControlValue(dialog, {idMacro}, value);");
                source.AppendLine("}");
                source.AppendLine();
            }
        }
    }

    private static string ComponentAccessorSuffix(string controlId)
    {
        var suffix = new StringBuilder("Control");
        foreach (var character in controlId)
        {
            if ((character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9'))
            {
                suffix.Append(character);
            }
            else
            {
                suffix.Append('_');
                suffix.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return suffix.ToString();
    }

    private static void AppendPropertyAccessorFunctions(
        StringBuilder source,
        MacTargetProfile profile,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry,
        int titleBarHeight)
    {
        source.AppendLine("static void InvalidateDesignerDialogItem(DialogPtr dialog, short item)");
        source.AppendLine("{");
        source.AppendLine("    short itemType;");
        source.AppendLine("    Handle itemHandle;");
        source.AppendLine("    Rect itemBounds;");
        source.AppendLine("    GrafPtr previousPort;");
        source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &itemBounds);");
        source.AppendLine("    GetPort(&previousPort);");
        source.AppendLine("    SetPort((GrafPtr)dialog);");
        source.AppendLine("    InvalRect(&itemBounds);");
        source.AppendLine("    SetPort(previousPort);");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("short MacStudioGetControlText(DialogPtr dialog, const char *controlId, unsigned char *text)");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        source.AppendLine("    short itemType;");
        source.AppendLine("    Handle itemHandle;");
        source.AppendLine("    Rect itemBounds;");
        source.AppendLine("    if (dialog == NULL || controlId == NULL || text == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    if (item == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &itemBounds);");
        source.AppendLine("    if (itemHandle == NULL ||");
        source.AppendLine("        ((itemType & ctrlItem) == 0 && itemType != statText && itemType != editText))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if ((itemType & ctrlItem) != 0)");
        source.AppendLine("        GetControlTitle((ControlHandle)itemHandle, text);");
        source.AppendLine("    else");
        source.AppendLine("        GetDialogItemText(itemHandle, text);");
        source.AppendLine("    return noErr;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("short MacStudioSetControlText(DialogPtr dialog, const char *controlId, const unsigned char *text)");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        source.AppendLine("    short itemType;");
        source.AppendLine("    Handle itemHandle;");
        source.AppendLine("    Rect itemBounds;");
        source.AppendLine("    if (dialog == NULL || controlId == NULL || text == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    if (item == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &itemBounds);");
        source.AppendLine("    if (itemHandle == NULL ||");
        source.AppendLine("        ((itemType & ctrlItem) == 0 && itemType != statText && itemType != editText))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if ((itemType & ctrlItem) != 0)");
        source.AppendLine("        SetControlTitle((ControlHandle)itemHandle, text);");
        source.AppendLine("    else");
        source.AppendLine("        SetDialogItemText(itemHandle, text);");
        source.AppendLine("    InvalidateDesignerDialogItem(dialog, item);");
        source.AppendLine("    return noErr;");
        source.AppendLine("}");
        source.AppendLine();
        AppendControlValueAccessor(
            source,
            "MacStudioGetControlValue",
            "short *value",
            controls,
            profile,
            widgetRegistry,
            isSetter: false,
            titleBarHeight);
        AppendControlValueAccessor(
            source,
            "MacStudioSetControlValue",
            "short value",
            controls,
            profile,
            widgetRegistry,
            isSetter: true,
            titleBarHeight);
        AppendControlBoundsAccessor(source, titleBarHeight);
        AppendControlBoundsSetter(
            source,
            profile,
            controls,
            widgetRegistry,
            titleBarHeight);
        AppendNamedControlAccessors(source, controls, widgetRegistry);
        AppendListBoxSelectionAccessor(source, controls);
        AppendListBoxItemsAccessor(source, controls);
    }

    private static void AppendListBoxSelectionAccessor(
        StringBuilder source,
        IReadOnlyList<DesignerControl> controls)
    {
        var listBoxIndexes = controls
            .Select((control, index) => (control, index))
            .Where(item => item.control.WidgetTypeId == "list-box")
            .Select(item => item.index)
            .ToArray();
        if (listBoxIndexes.Length == 0)
        {
            return;
        }

        source.AppendLine(
            "short MacStudioGetListBoxSelection(DialogPtr dialog, const char *controlId, short *row)");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        source.AppendLine("    ListHandle list = NULL;");
        source.AppendLine("    Cell cell;");
        source.AppendLine("    if (dialog == NULL || controlId == NULL || row == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        foreach (var index in listBoxIndexes)
        {
            source.AppendLine($"        case {index + 1}:");
            source.AppendLine($"            list = gList{index + 1};");
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return paramErr;");
        source.AppendLine("    }");
        source.AppendLine("    if (list == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    *row = -1;");
        source.AppendLine("    if (LGetSelect(false, &cell, list))");
        source.AppendLine("        *row = cell.v;");
        source.AppendLine("    return noErr;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendListBoxItemsAccessor(
        StringBuilder source,
        IReadOnlyList<DesignerControl> controls)
    {
        var listBoxIndexes = controls
            .Select((control, index) => (control, index))
            .Where(item => item.control.WidgetTypeId == "list-box")
            .Select(item => item.index)
            .ToArray();
        if (listBoxIndexes.Length == 0)
        {
            return;
        }

        source.AppendLine(
            "short MacStudioSetListBoxItems(DialogPtr dialog, const char *controlId, const unsigned char *const *items, short itemCount)");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        source.AppendLine("    short row;");
        source.AppendLine("    ListHandle list = NULL;");
        source.AppendLine(
            "    if (dialog == NULL || controlId == NULL || itemCount < 0 || itemCount > 128 || (itemCount > 0 && items == NULL))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        foreach (var index in listBoxIndexes)
        {
            source.AppendLine($"        case {index + 1}:");
            source.AppendLine($"            list = gList{index + 1};");
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return paramErr;");
        source.AppendLine("    }");
        source.AppendLine("    if (list == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    for (row = 0; row < itemCount; row++)");
        source.AppendLine("    {");
        source.AppendLine("        if (items[row] == NULL)");
        source.AppendLine("            return paramErr;");
        source.AppendLine("    }");
        source.AppendLine("    LDelRow(0, 0, list);");
        source.AppendLine("    if (itemCount > 0)");
        source.AppendLine("        LAddRow(itemCount, 0, list);");
        source.AppendLine("    {");
        source.AppendLine("        Cell cell;");
        source.AppendLine("        cell.h = 0;");
        source.AppendLine("        for (row = 0; row < itemCount; row++)");
        source.AppendLine("        {");
        source.AppendLine("            cell.v = row;");
        source.AppendLine(
            "            LSetCell((Ptr)(items[row] + 1), (short)items[row][0], cell, list);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("    LUpdate(((WindowPtr)dialog)->visRgn, list);");
        source.AppendLine("    return noErr;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendControlValueAccessor(
        StringBuilder source,
        string functionName,
        string valueParameter,
        IReadOnlyList<DesignerControl> controls,
        MacTargetProfile profile,
        DesignerWidgetRegistry widgetRegistry,
        bool isSetter,
        int titleBarHeight)
    {
        source.AppendLine(
            $"short {functionName}(DialogPtr dialog, const char *controlId, {valueParameter})");
        source.AppendLine("{");
        source.AppendLine("    short item;");
        if (!isSetter)
        {
            source.AppendLine("    if (value == NULL)");
            source.AppendLine("        return paramErr;");
        }
        source.AppendLine("    if (dialog == NULL || controlId == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    item = BridgeControlItem(controlId);");
        source.AppendLine("    if (item == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    switch (item)");
        source.AppendLine("    {");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var definition = widgetRegistry.GetRequired(control.WidgetTypeId);
            var context = CreateCodeGenerationContext(
                profile,
                control,
                index + 1,
                index == controls.Count - 1,
                titleBarHeight);
            source.AppendLine($"        case {index + 1}:");
            source.AppendLine("        {");
            if (isSetter)
            {
                definition.CodeGenerator.AppendControlValueSetter(source, context);
            }
            else
            {
                definition.CodeGenerator.AppendControlValueGetter(source, context);
            }
            source.AppendLine("            break;");
            source.AppendLine("        }");
        }
        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("    return paramErr;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static string GenerateResourceSource(
        MacTargetProfile profile,
        string appName,
        string formTitle,
        string formTypeName,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry,
        MacFormProperties formProperties)
    {
        var titleBarHeight = formProperties.ShowTitleBar ? DialogTitleBarHeight : 0;
        var clientWidth = formProperties.GetClientWidth(profile);
        var clientHeight = formProperties.GetClientHeight(profile);
        var dialogWidth = clientWidth + 2;
        var dialogHeight = clientHeight + titleBarHeight + 2;
        var dialogTop = formProperties.Top;
        var dialogLeft = formProperties.Left;
        var resources = new StringBuilder();
        resources.AppendLine("#include \"Dialogs.r\"");
        resources.AppendLine();
        resources.AppendLine($"resource 'DLOG' ({DialogResourceId}) {{");
        resources.AppendLine(
            $"    {{ {dialogTop}, {dialogLeft}, {dialogTop + dialogHeight}, {dialogLeft + dialogWidth} }},");
        resources.AppendLine($"    {WindowProcedure(formProperties)},");
        resources.AppendLine(formProperties.Visible ? "    visible," : "    invisible,");
        resources.AppendLine(formProperties.HasCloseBox ? "    goAway," : "    noGoAway,");
        resources.AppendLine("    0,");
        resources.AppendLine($"    {DialogResourceId},");
        resources.AppendLine($"    \"{EscapeRezString(formTitle)}\",");
        resources.AppendLine(formProperties.CenterOnScreen
            ? "    centerMainScreen"
            : "    noAutoCenter");
        resources.AppendLine("};");
        resources.AppendLine();
        resources.AppendLine($"resource 'DITL' ({DialogResourceId}) {{");
        resources.AppendLine("    {");
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            var definition = widgetRegistry.GetRequired(control.WidgetTypeId);
            definition.CodeGenerator.AppendResourceItem(
                resources,
                new MacWidgetCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    Pixel(control.X),
                    titleBarHeight + Pixel(control.Y),
                    Pixel(control.Width),
                    Pixel(control.Height),
                    EscapeRezString(control.Text),
                    index == controls.Count - 1));
        }

        resources.AppendLine("    }");
        resources.AppendLine("};");
        resources.AppendLine();
        for (var index = 0; index < controls.Count; index++)
        {
            var control = controls[index];
            widgetRegistry.GetRequired(control.WidgetTypeId).CodeGenerator.AppendAdditionalResources(
                resources,
                CreateCodeGenerationContext(
                    profile,
                    control,
                    index + 1,
                    index == controls.Count - 1,
                    titleBarHeight));
        }
        for (var index = 0; index < components.Count; index++)
        {
            componentRegistry.GetRequired(components[index].ComponentTypeId)
                .CodeGenerator.AppendResources(
                    resources,
                    CreateComponentCodeGenerationContext(
                        profile,
                        components,
                        index + 1,
                        formTypeName));
        }
        AppendMenuItemResources(resources, components);
        resources.AppendLine("#include \"Processes.r\"");
        resources.AppendLine();
        resources.AppendLine("resource 'SIZE' (-1) {");
        resources.AppendLine("    reserved,");
        resources.AppendLine("    acceptSuspendResumeEvents,");
        resources.AppendLine("    reserved,");
        resources.AppendLine("    canBackground,");
        resources.AppendLine("    doesActivateOnFGSwitch,");
        resources.AppendLine("    backgroundAndForeground,");
        resources.AppendLine("    dontGetFrontClicks,");
        resources.AppendLine("    ignoreChildDiedEvents,");
        resources.AppendLine("    is32BitCompatible,");
        resources.AppendLine("    notHighLevelEventAware,");
        resources.AppendLine("    onlyLocalHLEvents,");
        resources.AppendLine("    notStationeryAware,");
        resources.AppendLine("    dontUseTextEditServices,");
        resources.AppendLine("    reserved,");
        resources.AppendLine("    reserved,");
        resources.AppendLine("    reserved,");
        resources.AppendLine("    256 * 1024,");
        resources.AppendLine("    256 * 1024");
        resources.AppendLine("};");
        return resources.ToString();
    }

    private static string WindowProcedure(MacFormProperties formProperties)
    {
        if (!formProperties.ShowTitleBar)
        {
            return "plainDBox";
        }

        if (formProperties.Resizable)
        {
            return formProperties.HasZoomBox ? "zoomDocProc" : "documentProc";
        }

        if (formProperties.HasZoomBox)
        {
            return "zoomNoGrow";
        }

        if (formProperties.HasCloseBox)
        {
            return "noGrowDocProc";
        }

        return formProperties.Movable ? "movableDBoxProc" : "dBoxProc";
    }

    private static string GenerateCMakeLists(string appName) =>
        $"""
        cmake_minimum_required(VERSION 3.13)
        project({appName} C)

        add_application({appName}
            main.c
            designer_form.c
            designer_form.r
            user_events.c
        )

        set_target_properties({appName} PROPERTIES
            COMPILE_OPTIONS -ffunction-sections
            LINK_FLAGS "-Wl,-gc-sections -Wl,--mac-single"
        )
        """;

    private static void ValidateRezString(string value, string parameterName)
    {
        if (value.Length > byte.MaxValue)
        {
            throw new ArgumentException(
                "Classic Macintosh resource strings cannot exceed 255 printable ASCII characters.",
                parameterName);
        }

        foreach (var character in value)
        {
            if (character < 0x20 || character > 0x7e)
            {
                throw new ArgumentException(
                    "This initial form generator supports printable ASCII resource text only.",
                    parameterName);
            }
        }
    }

    private static string EscapeRezString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeCString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static MacWidgetCodeGenerationContext CreateCodeGenerationContext(
        MacTargetProfile profile,
        DesignerControl control,
        int itemNumber,
        bool isLastItem,
        int titleBarHeight = DialogTitleBarHeight) =>
        new(
            profile,
            control,
            itemNumber,
            Pixel(control.X),
            titleBarHeight + Pixel(control.Y),
            Pixel(control.Width),
            Pixel(control.Height),
            EscapeRezString(control.Text),
            isLastItem);

    private static int Pixel(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex AppNamePattern();
}
