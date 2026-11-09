using System.Text;

namespace ClassicMacIde.Core;

public static partial class MacFormCodeGenerator
{
    private static void AppendNonVisualComponentHeaderDeclarations(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components)
    {
        if (components.Any(component => component.ComponentTypeId == "file-access"))
        {
            source.AppendLine("/** Opens a file with the Standard File dialog and reads it into a caller-owned buffer. */");
            source.AppendLine(
                "short MacStudioFileLoad(const char *componentId, unsigned char *data, long capacity, long *length);");
            source.AppendLine("/** Selects a destination with Standard File and writes a caller-owned buffer. */");
            source.AppendLine(
                "short MacStudioFileSave(const char *componentId, const unsigned char *data, long length);");
        }

        if (components.Any(component => component.ComponentTypeId == "serial-port"))
        {
            source.AppendLine("/** Configures the selected built-in Macintosh serial port. */");
            source.AppendLine("short MacStudioSerialOpen(const char *componentId);");
            source.AppendLine("/** Opens a serial component on port 1 (Modem)/2 (Printer) at the selected baud rate. */");
            source.AppendLine(
                "short MacStudioSerialOpenConfigured(const char *componentId, short portIndex, long baudRate);");
            source.AppendLine("/** Stops pending serial I/O for the selected component. */");
            source.AppendLine("short MacStudioSerialClose(const char *componentId);");
            source.AppendLine("/** Returns the most recent Macintosh serial driver error. */");
            source.AppendLine("short MacStudioGetLastSerialError(const char *componentId);");
            source.AppendLine("/** Reads currently buffered serial bytes without waiting for more data. */");
            source.AppendLine(
                "short MacStudioSerialRead(const char *componentId, unsigned char *data, long capacity, long *readCount);");
            source.AppendLine("/** Writes bytes to the selected serial output port. */");
            source.AppendLine(
                "short MacStudioSerialWrite(const char *componentId, const unsigned char *data, long length);");
        }

        if (components.Any(component => component.ComponentTypeId == "protocol-transfer"))
        {
            source.AppendLine("/** Sends a memory buffer using the configured XMODEM or ZMODEM protocol. */");
            source.AppendLine(
                "short MacStudioProtocolSend(const char *componentId, const unsigned char *data, long length);");
            source.AppendLine("/** Receives a transfer into a caller-owned buffer. */");
            source.AppendLine(
                "short MacStudioProtocolReceive(const char *componentId, unsigned char *data, long capacity, long *length);");
            source.AppendLine("/** Receives an XMODEM transfer with an exact unpadded byte length. */");
            source.AppendLine(
                "short MacStudioProtocolReceiveExact(const char *componentId, unsigned char *data, long capacity, long expectedLength, long *length);");
            source.AppendLine("/** Gets the last transfer progress percentage. */");
            source.AppendLine(
                "short MacStudioProtocolGetProgress(const char *componentId, short *percentage);");
        }

        if (components.Count > 0)
        {
            source.AppendLine();
        }
    }

    private static void AppendNonVisualComponentRuntimeDeclarations(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components)
    {
        if (components.Count > 0)
        {
            source.AppendLine(
                "static void MacStudioDispatchNonVisualEventById(const char *componentId, short eventIndex);");
        }
        for (var index = 0; index < components.Count; index++)
        {
            if (components[index].ComponentTypeId == "serial-port")
            {
                source.AppendLine(
                    $"static Boolean gMacStudioSerialOpen{index + 1} = false;");
                source.AppendLine(
                    $"static short gMacStudioSerialInputRef{index + 1} = 0;");
                source.AppendLine(
                    $"static short gMacStudioSerialOutputRef{index + 1} = 0;");
                source.AppendLine(
                    $"static OSErr gMacStudioLastSerialError{index + 1} = noErr;");
                source.AppendLine(
                    $"static unsigned char gMacStudioSerialBuffer{index + 1}[512];");
            }
            else if (components[index].ComponentTypeId == "protocol-transfer")
            {
                source.AppendLine(
                    $"static short gMacStudioProtocolProgress{index + 1} = 0;");
            }
        }
    }

    private static void AppendNonVisualComponentRuntimeSource(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry)
    {
        if (components.Count == 0)
        {
            return;
        }

        source.AppendLine();
        source.AppendLine("static short MacStudioNonVisualComponentIndex(const char *componentId)");
        source.AppendLine("{");
        source.AppendLine("    if (componentId == NULL)");
        source.AppendLine("        return 0;");
        for (var index = 0; index < components.Count; index++)
        {
            source.AppendLine(
                $"    if (strcmp(componentId, \"{EscapeCString(components[index].Id)}\") == 0)");
            source.AppendLine($"        return {index + 1};");
        }
        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void MacStudioDispatchNonVisualEventById(const char *componentId, short eventIndex)");
        source.AppendLine("{");
        source.AppendLine("    short componentIndex = MacStudioNonVisualComponentIndex(componentId);");
        source.AppendLine("    if (componentIndex > 0)");
        source.AppendLine(
            "        DispatchNonVisualComponentEvent(gDesignerDialog, componentIndex, eventIndex);");
        source.AppendLine("}");
        source.AppendLine();

        var fileComponents = components
            .Select((component, index) => (component, ComponentIndex: index + 1))
            .Where(item => item.component.ComponentTypeId == "file-access")
            .ToArray();
        var serialComponents = components
            .Select((component, index) => (component, ComponentIndex: index + 1))
            .Where(item => item.component.ComponentTypeId == "serial-port")
            .ToArray();
        if (fileComponents.Length > 0)
        {
            AppendFileRuntimeSource(
                source,
                fileComponents,
                EventIndex(componentRegistry, "file-access", "OnError"));
        }
        if (serialComponents.Length > 0)
        {
            AppendSerialRuntimeSource(
                source,
                serialComponents,
                EventIndex(componentRegistry, "serial-port", "OnError"));
        }
        var protocolComponents = components
            .Select((component, index) => (component, ComponentIndex: index + 1))
            .Where(item => item.component.ComponentTypeId == "protocol-transfer")
            .ToArray();
        if (protocolComponents.Length > 0)
        {
            AppendProtocolRuntimeSource(
                source,
                protocolComponents,
                EventIndex(componentRegistry, "protocol-transfer", "OnError"));
        }
    }

    private static short EventIndex(
        DesignerNonVisualComponentRegistry componentRegistry,
        string componentTypeId,
        string eventId)
    {
        var index = componentRegistry.GetRequired(componentTypeId).Events
            .ToList()
            .FindIndex(eventDefinition => eventDefinition.Id == eventId);
        return index < 0 ? (short)0 : (short)(index + 1);
    }

    private static void AppendNonVisualComponentCleanup(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components)
    {
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            if (component.ComponentTypeId != "serial-port")
            {
                continue;
            }

            source.AppendLine(
                $"    if (gMacStudioSerialOpen{index + 1})");
            source.AppendLine(
                $"        MacStudioSerialClose(\"{EscapeCString(component.Id)}\");");
        }
    }

    private static void AppendSerialPortEventPolling(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry)
    {
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            if (component.ComponentTypeId != "serial-port")
            {
                continue;
            }

            var events = componentRegistry.GetRequired(component.ComponentTypeId).Events;
            var dataAvailableIndex = events
                .ToList()
                .FindIndex(eventDefinition => eventDefinition.Id == "OnDataAvailable") + 1;
            var errorIndex = events
                .ToList()
                .FindIndex(eventDefinition => eventDefinition.Id == "OnError") + 1;
            source.AppendLine($"        if (gMacStudioSerialOpen{index + 1})");
            source.AppendLine("        {");
            source.AppendLine("            LONGINT serialAvailable = 0;");
            source.AppendLine(
                $"            OSErr serialStatus = SerGetBuf(gMacStudioSerialInputRef{index + 1}, &serialAvailable);");
            source.AppendLine("            if (serialStatus != noErr)");
            source.AppendLine("            {");
            source.AppendLine(
                $"                gMacStudioLastSerialError{index + 1} = serialStatus;");
            source.AppendLine(
                $"                DispatchNonVisualComponentEvent(dialog, {index + 1}, {errorIndex});");
            source.AppendLine("            }");
            if (component.Events.ContainsKey("OnDataAvailable"))
            {
                source.AppendLine("            else if (serialAvailable > 0)");
                source.AppendLine(
                    $"                DispatchNonVisualComponentEvent(dialog, {index + 1}, {dataAvailableIndex});");
            }
            source.AppendLine("        }");
        }
    }

    private static void AppendFileRuntimeSource(
        StringBuilder source,
        IReadOnlyList<(DesignerNonVisualComponent component, int ComponentIndex)> files,
        short errorEventIndex)
    {
        foreach (var (component, componentIndex) in files)
        {
            AppendPascalStringArray(
                source,
                $"gMacStudioFileOpenPrompt{componentIndex}",
                component.Properties.GetValueOrDefault("openPrompt") ?? "Open:");
            AppendPascalStringArray(
                source,
                $"gMacStudioFileSavePrompt{componentIndex}",
                component.Properties.GetValueOrDefault("savePrompt") ?? "Save as:");
            AppendPascalStringArray(
                source,
                $"gMacStudioFileDefaultName{componentIndex}",
                component.Properties.GetValueOrDefault("defaultName") ?? "Untitled");
        }

        source.AppendLine("static short MacStudioFileComponentConfiguration(");
        source.AppendLine("    const char *componentId, ConstStringPtr *openPrompt,");
        source.AppendLine("    ConstStringPtr *savePrompt, ConstStringPtr *defaultName,");
        source.AppendLine("    OSType *fileType)");
        source.AppendLine("{");
        foreach (var (component, componentIndex) in files)
        {
            source.AppendLine(
                $"    if (strcmp(componentId, \"{EscapeCString(component.Id)}\") == 0)");
            source.AppendLine("    {");
            source.AppendLine($"        *openPrompt = gMacStudioFileOpenPrompt{componentIndex};");
            source.AppendLine($"        *savePrompt = gMacStudioFileSavePrompt{componentIndex};");
            source.AppendLine($"        *defaultName = gMacStudioFileDefaultName{componentIndex};");
            source.AppendLine(
                $"        *fileType = '{component.Properties.GetValueOrDefault("fileType") ?? "TEXT"}';");
            source.AppendLine("        return 1;");
            source.AppendLine("    }");
        }
        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioFileLoad(");
        source.AppendLine("    const char *componentId, unsigned char *data,");
        source.AppendLine("    long capacity, long *length)");
        source.AppendLine("{");
        source.AppendLine("    ConstStringPtr openPrompt;");
        source.AppendLine("    ConstStringPtr savePrompt;");
        source.AppendLine("    ConstStringPtr defaultName;");
        source.AppendLine("    OSType fileType;");
        source.AppendLine("    SFTypeList fileTypes;");
        source.AppendLine("    StandardFileReply reply;");
        source.AppendLine("    INTEGER fileRef = 0;");
        source.AppendLine("    LONGINT fileLength = 0;");
        source.AppendLine("    LONGINT count;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (componentId == NULL || data == NULL || length == NULL || capacity < 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    *length = 0;");
        source.AppendLine("    if (!MacStudioFileComponentConfiguration(");
        source.AppendLine("            componentId, &openPrompt, &savePrompt, &defaultName, &fileType))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    fileTypes[0] = fileType;");
        source.AppendLine("    StandardGetFile(NULL, 1, fileTypes, &reply);");
        source.AppendLine("    if (!reply.sfGood)");
        source.AppendLine("        return -128;");
        source.AppendLine("    error = HOpenDF(reply.sfFile.vRefNum, reply.sfFile.parID,");
        source.AppendLine("        reply.sfFile.name, fsRdPerm, &fileRef);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = GetEOF(fileRef, &fileLength);");
        source.AppendLine("    if (error == noErr && fileLength > capacity)");
        source.AppendLine("        error = memFullErr;");
        source.AppendLine("    if (error == noErr && fileLength > 0)");
        source.AppendLine("    {");
        source.AppendLine("        count = fileLength;");
        source.AppendLine("        error = FSRead(fileRef, &count, data);");
        source.AppendLine("        if (error == noErr && count != fileLength)");
        source.AppendLine("            error = ioErr;");
        source.AppendLine("    }");
        source.AppendLine("    if (fileRef != 0)");
        source.AppendLine("    {");
        source.AppendLine("        OSErr closeError = FSClose(fileRef);");
        source.AppendLine("        if (error == noErr)");
        source.AppendLine("            error = closeError;");
        source.AppendLine("    }");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        MacStudioSetLastSerialError(componentIndex, error);");
        source.AppendLine(
            $"        MacStudioDispatchNonVisualEventById(componentId, {errorEventIndex});");
        source.AppendLine("        return error;");
        source.AppendLine("    }");
        source.AppendLine("    *length = fileLength;");
        source.AppendLine("    return noErr;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioFileSave(");
        source.AppendLine("    const char *componentId, const unsigned char *data, long length)");
        source.AppendLine("{");
        source.AppendLine("    ConstStringPtr openPrompt;");
        source.AppendLine("    ConstStringPtr savePrompt;");
        source.AppendLine("    ConstStringPtr defaultName;");
        source.AppendLine("    OSType fileType;");
        source.AppendLine("    StandardFileReply reply;");
        source.AppendLine("    INTEGER fileRef = 0;");
        source.AppendLine("    LONGINT count;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (componentId == NULL || (data == NULL && length > 0) || length < 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if (!MacStudioFileComponentConfiguration(");
        source.AppendLine("            componentId, &openPrompt, &savePrompt, &defaultName, &fileType))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    StandardPutFile(savePrompt, defaultName, &reply);");
        source.AppendLine("    if (!reply.sfGood)");
        source.AppendLine("        return -128;");
        source.AppendLine("    if (reply.sfReplacing)");
        source.AppendLine("    {");
        source.AppendLine("        error = HDelete(reply.sfFile.vRefNum, reply.sfFile.parID,");
        source.AppendLine("            reply.sfFile.name);");
        source.AppendLine("        if (error != noErr)");
        source.AppendLine("            goto file_save_error;");
        source.AppendLine("    }");
        source.AppendLine("    error = HCreate(reply.sfFile.vRefNum, reply.sfFile.parID,");
        source.AppendLine("        reply.sfFile.name, 'MSID', fileType);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = HOpenDF(reply.sfFile.vRefNum, reply.sfFile.parID,");
        source.AppendLine("            reply.sfFile.name, fsWrPerm, &fileRef);");
        source.AppendLine("    if (error == noErr && length > 0)");
        source.AppendLine("    {");
        source.AppendLine("        count = length;");
        source.AppendLine("        error = FSWrite(fileRef, &count, data);");
        source.AppendLine("        if (error == noErr && count != length)");
        source.AppendLine("            error = ioErr;");
        source.AppendLine("    }");
        source.AppendLine("    if (fileRef != 0)");
        source.AppendLine("    {");
        source.AppendLine("        OSErr closeError = FSClose(fileRef);");
        source.AppendLine("        if (error == noErr)");
        source.AppendLine("            error = closeError;");
        source.AppendLine("    }");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        return noErr;");
        source.AppendLine("file_save_error:");
        source.AppendLine(
            $"    MacStudioDispatchNonVisualEventById(componentId, {errorEventIndex});");
        source.AppendLine("    return error;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendSerialRuntimeSource(
        StringBuilder source,
        IReadOnlyList<(DesignerNonVisualComponent component, int ComponentIndex)> serialPorts,
        short errorEventIndex)
    {
        foreach (var (_, componentIndex) in serialPorts)
        {
            AppendPascalStringArray(
                source,
                $"gMacStudioSerialModemInputDriverName{componentIndex}",
                ".AIn");
            AppendPascalStringArray(
                source,
                $"gMacStudioSerialModemOutputDriverName{componentIndex}",
                ".AOut");
            AppendPascalStringArray(
                source,
                $"gMacStudioSerialPrinterInputDriverName{componentIndex}",
                ".BIn");
            AppendPascalStringArray(
                source,
                $"gMacStudioSerialPrinterOutputDriverName{componentIndex}",
                ".BOut");
            source.AppendLine(
                $"static short gMacStudioSerialPortOverride{componentIndex} = 0;");
            source.AppendLine(
                $"static long gMacStudioSerialBaudOverride{componentIndex} = 0;");
        }
        source.AppendLine();

        source.AppendLine("static short MacStudioSerialBaudConfiguration(long baudRate)");
        source.AppendLine("{");
        source.AppendLine("    switch (baudRate)");
        source.AppendLine("    {");
        foreach (var (baudRate, baudConstant) in new[]
                 {
                     (300, "baud300"),
                     (600, "baud600"),
                     (1200, "baud1200"),
                     (1800, "baud1800"),
                     (2400, "baud2400"),
                     (3600, "baud3600"),
                     (4800, "baud4800"),
                     (7200, "baud7200"),
                     (9600, "baud9600"),
                     (14400, "baud14400"),
                     (19200, "baud19200"),
                     (28800, "baud28800"),
                     (38400, "baud38400"),
                     (57600, "baud57600"),
                 })
        {
            source.AppendLine($"        case {baudRate}: return {baudConstant};");
        }
        source.AppendLine("        default: return -1;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static short MacStudioSerialConfiguration(");
        source.AppendLine("    const char *componentId, short *inputRef, short *outputRef,");
        source.AppendLine("    short *configuration, SerShk *handshake)");
        source.AppendLine("{");
        foreach (var (component, componentIndex) in serialPorts)
        {
            var defaultPrinterPort =
                component.Properties.GetValueOrDefault("port") == "Printer";
            var inputRef = defaultPrinterPort ? "PRNTRIRNUM" : "MODEMIRNUM";
            var outputRef = defaultPrinterPort ? "PRNTRORNUM" : "MODEMORNUM";
            var baud = component.Properties.GetValueOrDefault("baud") switch
            {
                "300" => "baud300",
                "1200" => "baud1200",
                "2400" => "baud2400",
                "4800" => "baud4800",
                "9600" => "baud9600",
                "19200" => "baud19200",
                "38400" => "baud38400",
                "57600" => "baud57600",
                _ => "baud9600",
            };
            var dataBits = component.Properties.GetValueOrDefault("dataBits") == "7"
                ? "data7"
                : "data8";
            var parity = component.Properties.GetValueOrDefault("parity") switch
            {
                "Even" => "evenParity",
                "Odd" => "oddParity",
                _ => "noParity",
            };
            var stopBits = component.Properties.GetValueOrDefault("stopBits") == "2"
                ? "stop20"
                : "stop10";
            var flowControl = component.Properties.GetValueOrDefault("flowControl") ?? "None";
            source.AppendLine(
                $"    if (strcmp(componentId, \"{EscapeCString(component.Id)}\") == 0)");
            source.AppendLine("    {");
            source.AppendLine(
                $"        *inputRef = gMacStudioSerialPortOverride{componentIndex} == 0 ? {inputRef} : gMacStudioSerialPortOverride{componentIndex} == 1 ? MODEMIRNUM : PRNTRIRNUM;");
            source.AppendLine(
                $"        *outputRef = gMacStudioSerialPortOverride{componentIndex} == 0 ? {outputRef} : gMacStudioSerialPortOverride{componentIndex} == 1 ? MODEMORNUM : PRNTRORNUM;");
            source.AppendLine(
                $"        *configuration = (gMacStudioSerialBaudOverride{componentIndex} == 0 ? {baud} : MacStudioSerialBaudConfiguration(gMacStudioSerialBaudOverride{componentIndex})) | {dataBits} | {parity} | {stopBits};");
            source.AppendLine("        handshake->fXOn = 0;");
            source.AppendLine("        handshake->fCTS = 0;");
            source.AppendLine("        handshake->xOn = 0x11;");
            source.AppendLine("        handshake->xOff = 0x13;");
            source.AppendLine("        handshake->errs = 0;");
            source.AppendLine("        handshake->evts = 0;");
            source.AppendLine("        handshake->fInX = 0;");
            source.AppendLine("        handshake->null = 0;");
            if (flowControl == "Hardware")
            {
                source.AppendLine("        handshake->fCTS = 1;");
            }
            else if (flowControl == "Software")
            {
                source.AppendLine("        handshake->fXOn = 1;");
                source.AppendLine("        handshake->fInX = 1;");
            }
            source.AppendLine($"        return {componentIndex};");
            source.AppendLine("    }");
        }
        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static void MacStudioSetSerialOpenState(short componentIndex, Boolean isOpen)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine($"            gMacStudioSerialOpen{componentIndex} = isOpen;");
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static Boolean MacStudioSerialIsOpen(short componentIndex)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine($"            return gMacStudioSerialOpen{componentIndex};");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return false;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static void MacStudioSetSerialReferences(");
        source.AppendLine("    short componentIndex, short inputRef, short outputRef)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine(
                $"            gMacStudioSerialInputRef{componentIndex} = inputRef;");
            source.AppendLine(
                $"            gMacStudioSerialOutputRef{componentIndex} = outputRef;");
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static Boolean MacStudioGetSerialReferences(");
        source.AppendLine("    short componentIndex, short *inputRef, short *outputRef)");
        source.AppendLine("{");
        source.AppendLine("    if (inputRef == NULL || outputRef == NULL)");
        source.AppendLine("        return false;");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine(
                $"            if (!gMacStudioSerialOpen{componentIndex})");
            source.AppendLine("                return false;");
            source.AppendLine(
                $"            *inputRef = gMacStudioSerialInputRef{componentIndex};");
            source.AppendLine(
                $"            *outputRef = gMacStudioSerialOutputRef{componentIndex};");
            source.AppendLine("            if (*inputRef == 0 || *outputRef == 0)");
            source.AppendLine("                return false;");
            source.AppendLine("            return true;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return false;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static void MacStudioSetLastSerialError(short componentIndex, OSErr error)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine(
                $"            gMacStudioLastSerialError{componentIndex} = error;");
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioGetLastSerialError(const char *componentId)");
        source.AppendLine("{");
        source.AppendLine("    if (componentId == NULL)");
        source.AppendLine("        return paramErr;");
        foreach (var (component, componentIndex) in serialPorts)
        {
            source.AppendLine(
                $"    if (strcmp(componentId, \"{EscapeCString(component.Id)}\") == 0)");
            source.AppendLine(
                $"        return gMacStudioLastSerialError{componentIndex};");
        }
        source.AppendLine("    return paramErr;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static OSErr MacStudioOpenSerialDrivers(");
        source.AppendLine("    short componentIndex, short *inputRef, short *outputRef)");
        source.AppendLine("{");
        source.AppendLine("    short configuredInputRef;");
        source.AppendLine("    short configuredOutputRef;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (inputRef == NULL || outputRef == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    configuredInputRef = *inputRef;");
        source.AppendLine("    configuredOutputRef = *outputRef;");
        source.AppendLine("    *inputRef = 0;");
        source.AppendLine("    *outputRef = 0;");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine(
                $"            error = OpenDriver(configuredInputRef == PRNTRIRNUM ? gMacStudioSerialPrinterInputDriverName{componentIndex} : gMacStudioSerialModemInputDriverName{componentIndex}, inputRef);");
            source.AppendLine("            if (error != noErr)");
            source.AppendLine("                return error;");
            source.AppendLine(
                $"            error = OpenDriver(configuredOutputRef == PRNTRORNUM ? gMacStudioSerialPrinterOutputDriverName{componentIndex} : gMacStudioSerialModemOutputDriverName{componentIndex}, outputRef);");
            source.AppendLine("            if (error != noErr)");
            source.AppendLine("            {");
            source.AppendLine("                CloseDriver(*inputRef);");
            source.AppendLine("                *inputRef = 0;");
            source.AppendLine("                return error;");
            source.AppendLine("            }");
            source.AppendLine("            return noErr;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return paramErr;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static OSErr MacStudioSetSerialBuffer(short componentIndex, short inputRef)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine(
                $"            return SerSetBuf(inputRef, (Ptr)gMacStudioSerialBuffer{componentIndex}, (short)sizeof(gMacStudioSerialBuffer{componentIndex}));");
        }
        source.AppendLine("        default:");
        source.AppendLine("            return paramErr;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static void MacStudioSetSerialOverrides(");
        source.AppendLine("    short componentIndex, short portIndex, long baudRate)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in serialPorts)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine(
                $"            gMacStudioSerialPortOverride{componentIndex} = portIndex;");
            source.AppendLine(
                $"            gMacStudioSerialBaudOverride{componentIndex} = baudRate;");
            source.AppendLine("            break;");
        }
        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioSerialOpen(const char *componentId)");
        source.AppendLine("{");
        source.AppendLine("    short inputRef = 0;");
        source.AppendLine("    short outputRef = 0;");
        source.AppendLine("    short configuration = 0;");
        source.AppendLine("    short componentIndex;");
        source.AppendLine("    SerShk handshake;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (componentId == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    componentIndex = MacStudioSerialConfiguration(");
        source.AppendLine("        componentId, &inputRef, &outputRef, &configuration, &handshake);");
        source.AppendLine("    if (componentIndex == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    MacStudioSetLastSerialError(componentIndex, noErr);");
        source.AppendLine("    if (MacStudioSerialIsOpen(componentIndex))");
        source.AppendLine("        return noErr;");
        source.AppendLine("    error = MacStudioOpenSerialDrivers(componentIndex, &inputRef, &outputRef);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        goto serial_open_failed;");
        source.AppendLine("    error = SerReset(inputRef, configuration);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = SerReset(outputRef, configuration);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = MacStudioSetSerialBuffer(componentIndex, inputRef);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = SerHShake(inputRef, &handshake);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        if (outputRef != 0)");
        source.AppendLine("            CloseDriver(outputRef);");
        source.AppendLine("        if (inputRef != 0)");
        source.AppendLine("            CloseDriver(inputRef);");
        source.AppendLine("        goto serial_open_failed;");
        source.AppendLine("    }");
        source.AppendLine("    MacStudioSetSerialReferences(componentIndex, inputRef, outputRef);");
        source.AppendLine("    MacStudioSetSerialOpenState(componentIndex, true);");
        source.AppendLine("    return noErr;");
        source.AppendLine("serial_open_failed:");
        source.AppendLine("    MacStudioSetLastSerialError(componentIndex, error);");
        source.AppendLine("    MacStudioSetSerialOpenState(componentIndex, false);");
        source.AppendLine("    MacStudioSetSerialReferences(componentIndex, 0, 0);");
        source.AppendLine(
            $"    MacStudioDispatchNonVisualEventById(componentId, {errorEventIndex});");
        source.AppendLine("    return error;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioSerialOpenConfigured(");
        source.AppendLine("    const char *componentId, short portIndex, long baudRate)");
        source.AppendLine("{");
        source.AppendLine("    short inputRef = 0;");
        source.AppendLine("    short outputRef = 0;");
        source.AppendLine("    short configuration = 0;");
        source.AppendLine("    short componentIndex;");
        source.AppendLine("    SerShk handshake;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (componentId == NULL || portIndex < 0 || portIndex > 2)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if (baudRate != 0 && MacStudioSerialBaudConfiguration(baudRate) < 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    componentIndex = MacStudioSerialConfiguration(");
        source.AppendLine("        componentId, &inputRef, &outputRef, &configuration, &handshake);");
        source.AppendLine("    if (componentIndex == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if (MacStudioSerialIsOpen(componentIndex))");
        source.AppendLine("    {");
        source.AppendLine("        error = MacStudioSerialClose(componentId);");
        source.AppendLine("        if (error != noErr)");
        source.AppendLine("            return error;");
        source.AppendLine("    }");
        source.AppendLine("    MacStudioSetSerialOverrides(componentIndex, portIndex, baudRate);");
        source.AppendLine("    return MacStudioSerialOpen(componentId);");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("static OSErr MacStudioKillSerialIO(short serialRefNum)");
        source.AppendLine("{");
        source.AppendLine("    ParamBlockRec parameterBlock;");
        source.AppendLine("    memset(&parameterBlock, 0, sizeof(parameterBlock));");
        source.AppendLine("    parameterBlock.cntrlParam.ioCRefNum = serialRefNum;");
        source.AppendLine("    return PBKillIOSync(&parameterBlock);");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioSerialClose(const char *componentId)");
        source.AppendLine("{");
        source.AppendLine("    short inputRef = 0;");
        source.AppendLine("    short outputRef = 0;");
        source.AppendLine("    short configuration = 0;");
        source.AppendLine("    short componentIndex;");
        source.AppendLine("    SerShk handshake;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    OSErr closeError;");
        source.AppendLine("    if (componentId == NULL)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    componentIndex = MacStudioSerialConfiguration(");
        source.AppendLine("        componentId, &inputRef, &outputRef, &configuration, &handshake);");
        source.AppendLine("    if (componentIndex == 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if (!MacStudioGetSerialReferences(componentIndex, &inputRef, &outputRef))");
        source.AppendLine("        return noErr;");
        source.AppendLine("    error = MacStudioKillSerialIO(inputRef);");
        source.AppendLine("    closeError = MacStudioKillSerialIO(outputRef);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = closeError;");
        source.AppendLine("    closeError = CloseDriver(outputRef);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = closeError;");
        source.AppendLine("    closeError = CloseDriver(inputRef);");
        source.AppendLine("    if (error == noErr)");
        source.AppendLine("        error = closeError;");
        source.AppendLine("    MacStudioSetSerialReferences(componentIndex, 0, 0);");
        source.AppendLine("    MacStudioSetSerialOpenState(componentIndex, false);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("    {");
        source.AppendLine("        MacStudioSetLastSerialError(componentIndex, error);");
        source.AppendLine(
            $"        MacStudioDispatchNonVisualEventById(componentId, {errorEventIndex});");
        source.AppendLine("    }");
        source.AppendLine("    return error;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioSerialRead(");
        source.AppendLine("    const char *componentId, unsigned char *data,");
        source.AppendLine("    long capacity, long *readCount)");
        source.AppendLine("{");
        source.AppendLine("    short inputRef = 0;");
        source.AppendLine("    short outputRef = 0;");
        source.AppendLine("    short configuration = 0;");
        source.AppendLine("    short componentIndex;");
        source.AppendLine("    long available = 0;");
        source.AppendLine("    LONGINT count;");
        source.AppendLine("    SerShk handshake;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (componentId == NULL || data == NULL || readCount == NULL || capacity < 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    *readCount = 0;");
        source.AppendLine("    componentIndex = MacStudioSerialConfiguration(");
        source.AppendLine("        componentId, &inputRef, &outputRef, &configuration, &handshake);");
        source.AppendLine("    if (componentIndex == 0 || !MacStudioGetSerialReferences(componentIndex, &inputRef, &outputRef))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if (capacity == 0)");
        source.AppendLine("        return noErr;");
        source.AppendLine("    error = SerGetBuf(inputRef, &available);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        goto serial_read_error;");
        source.AppendLine("    if (available <= 0)");
        source.AppendLine("        return noErr;");
        source.AppendLine("    count = available < capacity ? available : capacity;");
        source.AppendLine("    error = FSRead(inputRef, &count, data);");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine("        goto serial_read_error;");
        source.AppendLine("    *readCount = count;");
        source.AppendLine("    return noErr;");
        source.AppendLine("serial_read_error:");
        source.AppendLine("    MacStudioSetLastSerialError(componentIndex, error);");
        source.AppendLine(
            $"    MacStudioDispatchNonVisualEventById(componentId, {errorEventIndex});");
        source.AppendLine("    return error;");
        source.AppendLine("}");
        source.AppendLine();

        source.AppendLine("short MacStudioSerialWrite(");
        source.AppendLine("    const char *componentId, const unsigned char *data, long length)");
        source.AppendLine("{");
        source.AppendLine("    short inputRef = 0;");
        source.AppendLine("    short outputRef = 0;");
        source.AppendLine("    short configuration = 0;");
        source.AppendLine("    short componentIndex;");
        source.AppendLine("    LONGINT count;");
        source.AppendLine("    SerShk handshake;");
        source.AppendLine("    OSErr error;");
        source.AppendLine("    if (componentId == NULL || (data == NULL && length > 0) || length < 0)");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    componentIndex = MacStudioSerialConfiguration(");
        source.AppendLine("        componentId, &inputRef, &outputRef, &configuration, &handshake);");
        source.AppendLine("    if (componentIndex == 0 || !MacStudioGetSerialReferences(componentIndex, &inputRef, &outputRef))");
        source.AppendLine("        return paramErr;");
        source.AppendLine("    if (length == 0)");
        source.AppendLine("        return noErr;");
        source.AppendLine("    count = length;");
        source.AppendLine("    error = FSWrite(outputRef, &count, data);");
        source.AppendLine("    if (error == noErr && count != length)");
        source.AppendLine("        error = ioErr;");
        source.AppendLine("    if (error != noErr)");
        source.AppendLine(
            $"        MacStudioDispatchNonVisualEventById(componentId, {errorEventIndex});");
        source.AppendLine("    return error;");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void AppendPascalStringArray(
        StringBuilder source,
        string identifier,
        string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        source.Append($"static const unsigned char {identifier}[] = {{ {bytes.Length}");
        foreach (var valueByte in bytes)
        {
            source.Append($", {valueByte}");
        }
        source.AppendLine(" };");
    }
}
