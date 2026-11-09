using System.Globalization;
using System.Text;

namespace ClassicMacIde.Core;

public static partial class MacFormCodeGenerator
{
    private static void AppendProtocolRuntimeSource(
        StringBuilder source,
        IReadOnlyList<(DesignerNonVisualComponent component, int ComponentIndex)> protocols,
        short errorEventIndex)
    {
        var progressEventIndex = EventIndex(
            ClassicMacNonVisualComponentCatalog.CreateRegistry(),
            "protocol-transfer",
            "OnProgress");
        var completeEventIndex = EventIndex(
            ClassicMacNonVisualComponentCatalog.CreateRegistry(),
            "protocol-transfer",
            "OnComplete");
        source.AppendLine("#include <OSUtils.h>");
        source.AppendLine("#include <Serial.h>");
        source.AppendLine("#include <stdio.h>");
        source.AppendLine();
        source.AppendLine($"#define MACSTUDIO_PROTOCOL_ON_PROGRESS {progressEventIndex}");
        source.AppendLine($"#define MACSTUDIO_PROTOCOL_ON_COMPLETE {completeEventIndex}");
        source.AppendLine($"#define MACSTUDIO_PROTOCOL_ON_ERROR {errorEventIndex}");
        source.AppendLine();

        source.AppendLine("static short MacStudioProtocolConfiguration(");
        source.AppendLine("    const char *componentId, const char **serialId, short *mode,");
        source.AppendLine("    short *timeoutSeconds, const char **fileName, short *componentIndex)");
        source.AppendLine("{");
        foreach (var (component, componentIndex) in protocols)
        {
            var mode = component.Properties.GetValueOrDefault("protocol") switch
            {
                "Xmodem" => 0,
                "XmodemCRC" => 1,
                "Xmodem1K" => 2,
                "Zmodem" => 3,
                _ => 1,
            };
            var timeoutSeconds = short.TryParse(
                component.Properties.GetValueOrDefault("timeoutSeconds"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedTimeout)
                ? Math.Clamp(parsedTimeout, (short)1, (short)120)
                : (short)10;
            var serialId = component.Properties.GetValueOrDefault("serialPortId") ?? string.Empty;
            var fileName = component.Properties.GetValueOrDefault("fileName") ?? "TRANSFER.BIN";
            source.AppendLine(
                $"    if (strcmp(componentId, \"{EscapeCString(component.Id)}\") == 0)");
            source.AppendLine("    {");
            source.AppendLine($"        *serialId = \"{EscapeCString(serialId)}\";");
            source.AppendLine($"        *mode = {mode};");
            source.AppendLine($"        *timeoutSeconds = {timeoutSeconds};");
            source.AppendLine($"        *fileName = \"{EscapeCString(fileName)}\";");
            source.AppendLine($"        *componentIndex = {componentIndex};");
            source.AppendLine("        return 1;");
            source.AppendLine("    }");
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static short MacStudioProtocolProgressValue(short componentIndex)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in protocols)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine($"            return gMacStudioProtocolProgress{componentIndex};");
        }

        source.AppendLine("        default:");
        source.AppendLine("            return 0;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void MacStudioProtocolProgressStore(short componentIndex, short value)");
        source.AppendLine("{");
        source.AppendLine("    short *progress = NULL;");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        foreach (var (_, componentIndex) in protocols)
        {
            source.AppendLine($"        case {componentIndex}:");
            source.AppendLine($"            progress = &gMacStudioProtocolProgress{componentIndex};");
            source.AppendLine("            break;");
        }

        source.AppendLine("        default:");
        source.AppendLine("            return;");
        source.AppendLine("    }");
        source.AppendLine("    if (*progress != value)");
        source.AppendLine("    {");
        source.AppendLine("        *progress = value;");
        source.AppendLine(
            "        DispatchNonVisualComponentEvent(gDesignerDialog, componentIndex, MACSTUDIO_PROTOCOL_ON_PROGRESS);");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine(
            "static short MacStudioProtocolFailure(const char *componentId, OSErr error)");
        source.AppendLine("{");
        source.AppendLine(
            "    MacStudioDispatchNonVisualEventById(componentId, MACSTUDIO_PROTOCOL_ON_ERROR);");
        source.AppendLine("    return error;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void MacStudioProtocolComplete(const char *componentId)");
        source.AppendLine("{");
        source.AppendLine(
            "    MacStudioDispatchNonVisualEventById(componentId, MACSTUDIO_PROTOCOL_ON_COMPLETE);");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("""
            static short MacStudioProtocolPercent(long amount, long total)
            {
                short percent = 0;
                short index;
                long remainder = 0;
                if (total <= 0 || amount >= total)
                    return 100;
                if (amount <= 0)
                    return 0;
                for (index = 0; index < 100; ++index)
                {
                    if (remainder >= total - amount)
                    {
                        remainder -= total - amount;
                        ++percent;
                    }
                    else
                        remainder += amount;
                }
                return percent;
            }
            """);
        source.AppendLine();
        source.AppendLine("""
            #define MS_XMODEM_SOH 0x01
            #define MS_XMODEM_STX 0x02
            #define MS_XMODEM_EOT 0x04
            #define MS_XMODEM_ACK 0x06
            #define MS_XMODEM_NAK 0x15
            #define MS_XMODEM_CAN 0x18
            #define MS_XMODEM_CRC 'C'
            #define MS_XMODEM_SUB 0x1A

            #define MS_ZPAD 0x2A
            #define MS_ZDLE 0x18
            #define MS_ZBIN 'A'
            #define MS_ZHEX 'B'
            #define MS_ZBIN32 'C'
            #define MS_ZRQINIT 0
            #define MS_ZRINIT 1
            #define MS_ZACK 3
            #define MS_ZFILE 4
            #define MS_ZSKIP 5
            #define MS_ZNAK 6
            #define MS_ZABORT 7
            #define MS_ZFIN 8
            #define MS_ZRPOS 9
            #define MS_ZDATA 10
            #define MS_ZEOF 11
            #define MS_ZCRCE 0x68
            #define MS_ZCRCG 0x69
            #define MS_ZCRCQ 0x6A
            #define MS_ZCRCW 0x6B
            #define MS_Z_CANFC32 0x20
            #define MS_Z_ESCCTL 0x40

            static OSErr MacStudioProtocolReadByte(
                const char *serialId, short timeoutSeconds, unsigned char *value)
            {
                ULONGINT start = TickCount();
                ULONGINT timeoutTicks = (ULONGINT)timeoutSeconds * 60;
                while ((ULONGINT)(TickCount() - start) < timeoutTicks)
                {
                    long count = 0;
                    OSErr error = MacStudioSerialRead(serialId, value, 1, &count);
                    if (error != noErr)
                        return error;
                    if (count > 0)
                        return noErr;
                    Delay(1, NULL);
                }
                return ioErr;
            }

            static OSErr MacStudioProtocolWriteByte(
                const char *serialId, unsigned char value)
            {
                return MacStudioSerialWrite(serialId, &value, 1);
            }

            static unsigned short MacStudioCrc16Update(
                unsigned short crc, unsigned char value)
            {
                short bit;
                crc ^= (unsigned short)value << 8;
                for (bit = 0; bit < 8; ++bit)
                    crc = (crc & 0x8000)
                        ? (unsigned short)((crc << 1) ^ 0x1021)
                        : (unsigned short)(crc << 1);
                return crc;
            }

            static unsigned short MacStudioCrc16(
                const unsigned char *data, long length)
            {
                unsigned short crc = 0;
                long index;
                for (index = 0; index < length; ++index)
                    crc = MacStudioCrc16Update(crc, data[index]);
                return crc;
            }

            static unsigned short MacStudioZCrc16(
                const unsigned char *data, long length)
            {
                return (unsigned short)~MacStudioCrc16(data, length);
            }

            static unsigned long MacStudioCrc32Update(
                unsigned long crc, unsigned char value)
            {
                short bit;
                crc ^= value;
                for (bit = 0; bit < 8; ++bit)
                    crc = (crc & 1)
                        ? (crc >> 1) ^ 0xEDB88320UL
                        : crc >> 1;
                return crc;
            }

            static unsigned long MacStudioCrc32(
                const unsigned char *data, long length)
            {
                unsigned long crc = 0xFFFFFFFFUL;
                long index;
                for (index = 0; index < length; ++index)
                    crc = MacStudioCrc32Update(crc, data[index]);
                return ~crc;
            }

            static short MacStudioXmodemReadControl(
                const char *serialId, short timeoutSeconds, short mode,
                unsigned char *value)
            {
                short attempts;
                for (attempts = 0; attempts < 10; ++attempts)
                {
                    OSErr error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, value);
                    if (error != noErr)
                        return error;
                    if (*value == MS_XMODEM_CAN)
                    {
                        unsigned char second = 0;
                        if (MacStudioProtocolReadByte(
                                serialId, timeoutSeconds, &second) == noErr &&
                            second == MS_XMODEM_CAN)
                            return abortErr;
                    }
                    if (*value == MS_XMODEM_ACK || *value == MS_XMODEM_NAK ||
                        *value == MS_XMODEM_CRC)
                        return noErr;
                }
                return ioErr;
            }

            static OSErr MacStudioXmodemSend(
                const char *serialId, short mode, short timeoutSeconds,
                const unsigned char *data, long length, short componentIndex)
            {
                unsigned char response = 0;
                unsigned char block[1024];
                unsigned char packet[1029];
                unsigned short crc;
                long offset = 0;
                short blockNumber = 1;
                short blockSize = mode == 2 ? 1024 : 128;
                short retries;
                OSErr error;

                for (retries = 0; retries < 10; ++retries)
                {
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &response);
                    if (error == noErr &&
                        ((mode == 0 && response == MS_XMODEM_NAK) ||
                         (mode != 0 && response == MS_XMODEM_CRC)))
                        break;
                    if (error != noErr && retries == 9)
                        return error;
                }
                if (retries == 10)
                    return ioErr;

                while (offset < length)
                {
                    long count = length - offset;
                    long index;
                    if (count > blockSize)
                        count = blockSize;
                    for (index = 0; index < blockSize; ++index)
                        block[index] = index < count
                            ? data[offset + index]
                            : MS_XMODEM_SUB;
                    packet[0] = blockSize == 1024
                        ? MS_XMODEM_STX
                        : MS_XMODEM_SOH;
                    packet[1] = (unsigned char)blockNumber;
                    packet[2] = (unsigned char)~blockNumber;
                    memcpy(packet + 3, block, blockSize);
                    if (mode == 0)
                    {
                        unsigned char checksum = 0;
                        for (index = 0; index < blockSize; ++index)
                            checksum = (unsigned char)(checksum + block[index]);
                        packet[3 + blockSize] = checksum;
                    }
                    else
                    {
                        crc = MacStudioCrc16(block, blockSize);
                        packet[3 + blockSize] = (unsigned char)(crc >> 8);
                        packet[4 + blockSize] = (unsigned char)crc;
                    }

                    for (retries = 0; retries < 10; ++retries)
                    {
                        error = MacStudioSerialWrite(
                            serialId, packet, 3 + blockSize + (mode == 0 ? 1 : 2));
                        if (error != noErr)
                            return error;
                        error = MacStudioXmodemReadControl(
                            serialId, timeoutSeconds, mode, &response);
                        if (error == noErr && response == MS_XMODEM_ACK)
                            break;
                        if (error == abortErr)
                            return error;
                        if (error != noErr && retries == 9)
                            return error;
                    }
                    if (retries == 10)
                        return ioErr;
                    offset += count;
                    ++blockNumber;
                    MacStudioProtocolProgressStore(
                        componentIndex, MacStudioProtocolPercent(offset, length));
                }

                for (retries = 0; retries < 10; ++retries)
                {
                    error = MacStudioProtocolWriteByte(serialId, MS_XMODEM_EOT);
                    if (error != noErr)
                        return error;
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &response);
                    if (error == noErr && response == MS_XMODEM_ACK)
                        return noErr;
                    if (error != noErr && retries == 9)
                        return error;
                }
                return ioErr;
            }

            static OSErr MacStudioXmodemReceive(
                const char *serialId, short mode, short timeoutSeconds,
                unsigned char *data, long capacity, long *length,
                short componentIndex, long expectedLength)
            {
                unsigned char packet[1024];
                unsigned char response = mode == 0
                    ? MS_XMODEM_NAK
                    : MS_XMODEM_CRC;
                unsigned char expectedBlock = 1;
                unsigned char previousBlock = 0;
                short blockSize;
                short retries = 0;
                OSErr error;

                *length = 0;
                error = MacStudioProtocolWriteByte(serialId, response);
                if (error != noErr)
                    return error;
                while (retries < 10)
                {
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &response);
                    if (error != noErr)
                    {
                        error = MacStudioProtocolWriteByte(
                            serialId, mode == 0 ? MS_XMODEM_NAK : MS_XMODEM_NAK);
                        if (error != noErr)
                            return error;
                        ++retries;
                        continue;
                    }
                    if (response == MS_XMODEM_EOT)
                    {
                        long expectedPaddedLength;
                        error = MacStudioProtocolWriteByte(
                            serialId, MS_XMODEM_ACK);
                        if (error != noErr)
                            return error;
                        if (expectedLength >= 0)
                        {
                            expectedPaddedLength =
                                ((expectedLength + 127) / 128) * 128;
                            if (*length != expectedPaddedLength)
                                return ioErr;
                            *length = expectedLength;
                        }
                        else
                        {
                            while (*length > 0 &&
                                data[*length - 1] == MS_XMODEM_SUB)
                                --*length;
                        }
                        MacStudioProtocolProgressStore(componentIndex, 100);
                        return noErr;
                    }
                    if (response == MS_XMODEM_CAN)
                        return abortErr;
                    if (response != MS_XMODEM_SOH &&
                        !(mode == 2 && response == MS_XMODEM_STX))
                    {
                        error = MacStudioProtocolWriteByte(
                            serialId, MS_XMODEM_NAK);
                        if (error != noErr)
                            return error;
                        ++retries;
                        continue;
                    }

                    blockSize = response == MS_XMODEM_STX ? 1024 : 128;
                    {
                        unsigned char blockNumber;
                        unsigned char complement;
                        unsigned short receivedCrc = 0;
                        unsigned short actualCrc;
                        unsigned char checksum = 0;
                        long index;
                        error = MacStudioProtocolReadByte(
                            serialId, timeoutSeconds, &blockNumber);
                        if (error == noErr)
                            error = MacStudioProtocolReadByte(
                                serialId, timeoutSeconds, &complement);
                        for (index = 0; error == noErr && index < blockSize; ++index)
                            error = MacStudioProtocolReadByte(
                                serialId, timeoutSeconds, &packet[index]);
                        if (error == noErr && mode == 0)
                        {
                            unsigned char receivedChecksum;
                            error = MacStudioProtocolReadByte(
                                serialId, timeoutSeconds, &receivedChecksum);
                            for (index = 0; index < blockSize; ++index)
                                checksum = (unsigned char)(checksum + packet[index]);
                            if (error == noErr && checksum != receivedChecksum)
                                error = ioErr;
                        }
                        else if (error == noErr)
                        {
                            unsigned char high;
                            unsigned char low = 0;
                            high = 0;
                            error = MacStudioProtocolReadByte(
                                serialId, timeoutSeconds, &high);
                            if (error == noErr)
                                error = MacStudioProtocolReadByte(
                                    serialId, timeoutSeconds, &low);
                            if (error == noErr)
                            {
                                receivedCrc = ((unsigned short)high << 8) | low;
                                actualCrc = MacStudioCrc16(packet, blockSize);
                                if (receivedCrc != actualCrc)
                                    error = ioErr;
                            }
                        }
                        if (error == noErr &&
                            (unsigned char)(blockNumber + complement) != 0xFF)
                            error = ioErr;
                        if (error == noErr && blockNumber == expectedBlock)
                        {
                            if (*length + blockSize > capacity)
                            {
                                MacStudioProtocolWriteByte(serialId, MS_XMODEM_CAN);
                                MacStudioProtocolWriteByte(serialId, MS_XMODEM_CAN);
                                return memFullErr;
                            }
                            memcpy(data + *length, packet, blockSize);
                            *length += blockSize;
                            previousBlock = blockNumber;
                            ++expectedBlock;
                            retries = 0;
                            error = MacStudioProtocolWriteByte(
                                serialId, MS_XMODEM_ACK);
                            if (error != noErr)
                                return error;
                            if (capacity > 0)
                            {
                                short percent = MacStudioProtocolPercent(*length, capacity);
                                if (percent > 99)
                                    percent = 99;
                                MacStudioProtocolProgressStore(
                                    componentIndex, percent);
                            }
                        }
                        else if (error == noErr && blockNumber == previousBlock)
                        {
                            error = MacStudioProtocolWriteByte(
                                serialId, MS_XMODEM_ACK);
                        }
                        else
                        {
                            error = MacStudioProtocolWriteByte(
                                serialId, MS_XMODEM_NAK);
                            ++retries;
                        }
                        if (error != noErr)
                            return error;
                    }
                }
                return ioErr;
            }

            static short MacStudioZHexValue(unsigned char value)
            {
                if (value >= '0' && value <= '9')
                    return (short)(value - '0');
                if (value >= 'a' && value <= 'f')
                    return (short)(value - 'a' + 10);
                if (value >= 'A' && value <= 'F')
                    return (short)(value - 'A' + 10);
                return -1;
            }

            static void MacStudioZPutEscaped(
                unsigned char *output, long *length, unsigned char value)
            {
                if (value < 0x20 || value == 0x7F ||
                    value == 0x90 || value == 0x91 ||
                    value == 0x93 || value == MS_ZDLE)
                {
                    output[(*length)++] = MS_ZDLE;
                    output[(*length)++] = value ^ 0x40;
                }
                else
                    output[(*length)++] = value;
            }

            static OSErr MacStudioZReadEscapedByte(
                const char *serialId, short timeoutSeconds, unsigned char *value)
            {
                unsigned char escaped;
                OSErr error = MacStudioProtocolReadByte(
                    serialId, timeoutSeconds, value);
                if (error != noErr || *value != MS_ZDLE)
                    return error;
                error = MacStudioProtocolReadByte(
                    serialId, timeoutSeconds, &escaped);
                if (error == noErr)
                    *value = escaped ^ 0x40;
                return error;
            }

            static OSErr MacStudioZWriteHeader(
                const char *serialId, unsigned char type,
                const unsigned char fields[4], short useCrc32, short hex)
            {
                unsigned char bytes[9];
                unsigned char output[32];
                unsigned char hexDigits[] = "0123456789abcdef";
                long outputLength = 0;
                short index;
                bytes[0] = type;
                memcpy(bytes + 1, fields, 4);
                if (hex)
                {
                    unsigned short crc = MacStudioZCrc16(bytes, 5);
                    output[outputLength++] = MS_ZPAD;
                    output[outputLength++] = MS_ZPAD;
                    output[outputLength++] = MS_ZDLE;
                    output[outputLength++] = MS_ZHEX;
                    for (index = 0; index < 5; ++index)
                    {
                        output[outputLength++] = hexDigits[bytes[index] >> 4];
                        output[outputLength++] = hexDigits[bytes[index] & 0x0F];
                    }
                    output[outputLength++] = hexDigits[(crc >> 12) & 0x0F];
                    output[outputLength++] = hexDigits[(crc >> 8) & 0x0F];
                    output[outputLength++] = hexDigits[(crc >> 4) & 0x0F];
                    output[outputLength++] = hexDigits[crc & 0x0F];
                    output[outputLength++] = '\r';
                    output[outputLength++] = '\n';
                    if (type != MS_ZACK && type != MS_ZFIN)
                        output[outputLength++] = 0x11;
                }
                else if (useCrc32)
                {
                    unsigned long crc = MacStudioCrc32(bytes, 5);
                    output[outputLength++] = MS_ZPAD;
                    output[outputLength++] = MS_ZPAD;
                    output[outputLength++] = MS_ZDLE;
                    output[outputLength++] = MS_ZBIN32;
                    for (index = 0; index < 5; ++index)
                        MacStudioZPutEscaped(output, &outputLength, bytes[index]);
                    for (index = 0; index < 4; ++index)
                        MacStudioZPutEscaped(
                            output, &outputLength, (unsigned char)(crc >> (index * 8)));
                }
                else
                {
                    unsigned short crc = MacStudioZCrc16(bytes, 5);
                    output[outputLength++] = MS_ZPAD;
                    output[outputLength++] = MS_ZPAD;
                    output[outputLength++] = MS_ZDLE;
                    output[outputLength++] = MS_ZBIN;
                    for (index = 0; index < 5; ++index)
                        MacStudioZPutEscaped(output, &outputLength, bytes[index]);
                    MacStudioZPutEscaped(output, &outputLength, (unsigned char)(crc >> 8));
                    MacStudioZPutEscaped(output, &outputLength, (unsigned char)crc);
                }
                return MacStudioSerialWrite(serialId, output, outputLength);
            }

            static OSErr MacStudioZReadHeader(
                const char *serialId, short timeoutSeconds, unsigned char *type,
                unsigned char fields[4], short *useCrc32)
            {
                unsigned char value;
                unsigned char format;
                unsigned char bytes[9];
                unsigned char crcBytes[4];
                short nibble;
                short index;
                OSErr error;
                for (;;)
                {
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &value);
                    if (error != noErr)
                        return error;
                    if (value != MS_ZPAD)
                        continue;
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &format);
                    if (error != noErr)
                        return error;
                    if (format == MS_ZPAD)
                    {
                        error = MacStudioProtocolReadByte(
                            serialId, timeoutSeconds, &format);
                        if (error != noErr)
                            return error;
                    }
                    if (format != MS_ZDLE)
                        continue;
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &format);
                    if (error != noErr)
                        return error;
                    break;
                }

                if (format == MS_ZHEX)
                {
                    unsigned short receivedCrc = 0;
                    unsigned short actualCrc;
                    for (index = 0; index < 7; ++index)
                    {
                        unsigned char high;
                        unsigned char low;
                        error = MacStudioProtocolReadByte(
                            serialId, timeoutSeconds, &high);
                        if (error == noErr)
                            error = MacStudioProtocolReadByte(
                                serialId, timeoutSeconds, &low);
                        if (error != noErr)
                            return error;
                        nibble = MacStudioZHexValue(high);
                        if (nibble < 0)
                            return ioErr;
                        bytes[index] = (unsigned char)(nibble << 4);
                        nibble = MacStudioZHexValue(low);
                        if (nibble < 0)
                            return ioErr;
                        bytes[index] |= (unsigned char)nibble;
                    }
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &value);
                    if (error == noErr && value == '\r')
                    {
                        error = MacStudioProtocolReadByte(
                            serialId, timeoutSeconds, &value);
                        if (error == noErr && value != '\n')
                            error = ioErr;
                    }
                    else if (error == noErr)
                        error = ioErr;
                    if (error != noErr)
                        return error;
                    receivedCrc = ((unsigned short)bytes[5] << 8) | bytes[6];
                    actualCrc = MacStudioZCrc16(bytes, 5);
                    if (receivedCrc != actualCrc)
                        return ioErr;
                    *type = bytes[0];
                    memcpy(fields, bytes + 1, 4);
                    *useCrc32 = 0;
                    return noErr;
                }

                if (format == MS_ZBIN32)
                    *useCrc32 = 1;
                else if (format == MS_ZBIN)
                    *useCrc32 = 0;
                else
                    return ioErr;
                for (index = 0; index < 5; ++index)
                {
                    error = MacStudioZReadEscapedByte(
                        serialId, timeoutSeconds, &bytes[index]);
                    if (error != noErr)
                        return error;
                }
                if (*useCrc32)
                {
                    unsigned long receivedCrc = 0;
                    unsigned long actualCrc = MacStudioCrc32(bytes, 5);
                    for (index = 0; index < 4; ++index)
                    {
                        error = MacStudioZReadEscapedByte(
                            serialId, timeoutSeconds, &crcBytes[index]);
                        if (error != noErr)
                            return error;
                        receivedCrc |= (unsigned long)crcBytes[index] << (index * 8);
                    }
                    if (receivedCrc != actualCrc)
                        return ioErr;
                }
                else
                {
                    unsigned short receivedCrc;
                    unsigned short actualCrc = MacStudioZCrc16(bytes, 5);
                    error = MacStudioZReadEscapedByte(
                        serialId, timeoutSeconds, &crcBytes[0]);
                    if (error == noErr)
                        error = MacStudioZReadEscapedByte(
                            serialId, timeoutSeconds, &crcBytes[1]);
                    if (error != noErr)
                        return error;
                    receivedCrc = ((unsigned short)crcBytes[0] << 8) | crcBytes[1];
                    if (receivedCrc != actualCrc)
                        return ioErr;
                }
                *type = bytes[0];
                memcpy(fields, bytes + 1, 4);
                return noErr;
            }

            static OSErr MacStudioZSendSubpacket(
                const char *serialId, const unsigned char *data, long length,
                unsigned char endMarker, short useCrc32)
            {
                unsigned char output[2064];
                unsigned char crcInput[1025];
                unsigned long crc;
                unsigned short crc16;
                long outputLength = 0;
                long index;
                if (length < 0 || length > 1024)
                    return paramErr;
                memcpy(crcInput, data, length);
                crcInput[length] = endMarker;
                crc = MacStudioCrc32(crcInput, length + 1);
                for (index = 0; index < length; ++index)
                    MacStudioZPutEscaped(output, &outputLength, data[index]);
                output[outputLength++] = MS_ZDLE;
                output[outputLength++] = endMarker;
                if (useCrc32)
                {
                    for (index = 0; index < 4; ++index)
                        MacStudioZPutEscaped(
                            output, &outputLength, (unsigned char)(crc >> (index * 8)));
                }
                else
                {
                    crc16 = MacStudioZCrc16(crcInput, length + 1);
                    MacStudioZPutEscaped(
                        output, &outputLength, (unsigned char)(crc16 >> 8));
                    MacStudioZPutEscaped(
                        output, &outputLength, (unsigned char)crc16);
                }
                return MacStudioSerialWrite(serialId, output, outputLength);
            }

            static OSErr MacStudioZReadSubpacket(
                const char *serialId, short timeoutSeconds, short useCrc32,
                unsigned char *data, long capacity, long *length,
                unsigned char *endMarker)
            {
                unsigned long crc32 = 0xFFFFFFFFUL;
                unsigned short crc16 = 0;
                unsigned char value;
                unsigned char escaped;
                unsigned char crcBytes[4];
                unsigned long receivedCrc = 0;
                unsigned short receivedCrc16 = 0;
                long index;
                OSErr error;
                *length = 0;
                for (;;)
                {
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &value);
                    if (error != noErr)
                        return error;
                    if (value == MS_ZDLE)
                    {
                        error = MacStudioProtocolReadByte(
                            serialId, timeoutSeconds, &escaped);
                        if (error != noErr)
                            return error;
                        if (escaped == MS_ZCRCE || escaped == MS_ZCRCG ||
                            escaped == MS_ZCRCQ || escaped == MS_ZCRCW)
                        {
                            *endMarker = escaped;
                            break;
                        }
                        value = escaped ^ 0x40;
                    }
                    if (*length >= capacity)
                        return memFullErr;
                    data[(*length)++] = value;
                    if (useCrc32)
                        crc32 = MacStudioCrc32Update(crc32, value);
                    else
                        crc16 = MacStudioCrc16Update(crc16, value);
                }
                if (useCrc32)
                    crc32 = MacStudioCrc32Update(crc32, *endMarker) ^ 0xFFFFFFFFUL;
                else
                    crc16 = (unsigned short)~MacStudioCrc16Update(crc16, *endMarker);
                for (index = 0; index < (useCrc32 ? 4 : 2); ++index)
                {
                    error = MacStudioZReadEscapedByte(
                        serialId, timeoutSeconds, &crcBytes[index]);
                    if (error != noErr)
                        return error;
                    if (useCrc32)
                        receivedCrc |= (unsigned long)crcBytes[index] << (index * 8);
                    else
                        receivedCrc16 = (unsigned short)(
                            (receivedCrc16 << 8) | crcBytes[index]);
                }
                if ((useCrc32 && receivedCrc != crc32) ||
                    (!useCrc32 && receivedCrc16 != crc16))
                    return ioErr;
                return noErr;
            }

            static void MacStudioZSetPosition(
                unsigned char fields[4], unsigned long position)
            {
                fields[0] = (unsigned char)position;
                fields[1] = (unsigned char)(position >> 8);
                fields[2] = (unsigned char)(position >> 16);
                fields[3] = (unsigned char)(position >> 24);
            }

            static unsigned long MacStudioZGetPosition(
                const unsigned char fields[4])
            {
                return (unsigned long)fields[0] |
                    ((unsigned long)fields[1] << 8) |
                    ((unsigned long)fields[2] << 16) |
                    ((unsigned long)fields[3] << 24);
            }

            static OSErr MacStudioZSendCancel(const char *serialId)
            {
                unsigned char cancel[8];
                short index;
                for (index = 0; index < 8; ++index)
                    cancel[index] = MS_ZDLE;
                return MacStudioSerialWrite(serialId, cancel, sizeof(cancel));
            }

            static OSErr MacStudioZSendReceiverInit(const char *serialId)
            {
                unsigned char fields[4] = { 0, 4, 0,
                    MS_Z_CANFC32 | MS_Z_ESCCTL | 0x03 };
                return MacStudioZWriteHeader(
                    serialId, MS_ZRINIT, fields, 0, 1);
            }

            static OSErr MacStudioZmodemSend(
                const char *serialId, short timeoutSeconds, const char *fileName,
                const unsigned char *data, long length, short componentIndex)
            {
                unsigned char fields[4] = { 0, 0, 0, 0 };
                unsigned char type;
                unsigned char metadata[256];
                unsigned char nameLength = 0;
                short useCrc32 = 0;
                short peerFlags = 0;
                short attempts;
                short restartCount = 0;
                long offset;
                long metadataLength;
                long textLength;
                OSErr error;

                for (attempts = 0; attempts < 10; ++attempts)
                {
                    error = MacStudioZWriteHeader(
                        serialId, MS_ZRQINIT, fields, 0, 1);
                    if (error != noErr)
                        return error;
                    error = MacStudioZReadHeader(
                        serialId, timeoutSeconds, &type, fields, &useCrc32);
                    if (error == noErr && type == MS_ZRINIT)
                    {
                        peerFlags = fields[3];
                        break;
                    }
                }
                if (attempts == 10)
                    return ioErr;
                useCrc32 = (peerFlags & MS_Z_CANFC32) != 0;

                while (nameLength < 63 && fileName[nameLength] != '\0')
                {
                    metadata[nameLength] = (unsigned char)fileName[nameLength];
                    ++nameLength;
                }
                metadata[nameLength] = 0;
                textLength = sprintf(
                    (char *)metadata + nameLength + 1,
                    "%ld 0 0 0 1 0", length);
                metadataLength = nameLength + 1 + textLength + 1;
                fields[0] = 1;
                fields[1] = 0;
                fields[2] = 0;
                fields[3] = 0;
                error = MacStudioZWriteHeader(
                    serialId, MS_ZFILE, fields, useCrc32, 0);
                if (error == noErr)
                    error = MacStudioZSendSubpacket(
                        serialId, metadata, metadataLength, MS_ZCRCW, useCrc32);
                if (error != noErr)
                    return error;
                error = MacStudioZReadHeader(
                    serialId, timeoutSeconds, &type, fields, &useCrc32);
                if (error != noErr || type == MS_ZSKIP)
                    return error == noErr ? ioErr : error;
                if (type != MS_ZRPOS)
                    return ioErr;
                offset = (long)MacStudioZGetPosition(fields);

                for (;;)
                {
                    short restart = 0;
                    if (++restartCount > 10)
                        return ioErr;
                    while (offset < length || (length == 0 && offset == 0))
                    {
                        unsigned char endMarker = MS_ZCRCW;
                        long count = length - offset;
                        if (count > 512)
                            count = 512;
                        fields[0] = (unsigned char)offset;
                        fields[1] = (unsigned char)(offset >> 8);
                        fields[2] = (unsigned char)(offset >> 16);
                        fields[3] = (unsigned char)(offset >> 24);
                        error = MacStudioZWriteHeader(
                            serialId, MS_ZDATA, fields, useCrc32, 0);
                        if (error == noErr)
                            error = MacStudioZSendSubpacket(
                                serialId,
                                length > 0 ? data + offset :
                                    (const unsigned char *)"",
                                count, endMarker, useCrc32);
                        if (error != noErr)
                            return error;
                        if (length == 0)
                            offset = 1;
                        else
                            offset += count;
                        error = MacStudioZReadHeader(
                            serialId, timeoutSeconds, &type, fields, &useCrc32);
                        if (error != noErr)
                            return error;
                        if (type == MS_ZRPOS)
                        {
                            offset = (long)MacStudioZGetPosition(fields);
                            restart = 1;
                            break;
                        }
                        if (type != MS_ZACK)
                            return ioErr;
                        if (length > 0)
                            MacStudioProtocolProgressStore(
                                componentIndex, MacStudioProtocolPercent(offset, length));
                        if (length == 0)
                            break;
                    }
                    if (restart)
                        continue;

                    MacStudioZSetPosition(fields, (unsigned long)length);
                    error = MacStudioZWriteHeader(
                        serialId, MS_ZEOF, fields, 0, 1);
                    if (error != noErr)
                        return error;
                    error = MacStudioZReadHeader(
                        serialId, timeoutSeconds, &type, fields, &useCrc32);
                    if (error != noErr)
                        return error;
                    if (type == MS_ZRPOS)
                    {
                        offset = (long)MacStudioZGetPosition(fields);
                        continue;
                    }
                    if (type != MS_ZRINIT)
                        return ioErr;
                    break;
                }

                error = MacStudioZWriteHeader(
                    serialId, MS_ZFIN, fields, 0, 1);
                if (error != noErr)
                    return error;
                error = MacStudioZReadHeader(
                    serialId, timeoutSeconds, &type, fields, &useCrc32);
                if (error != noErr || type != MS_ZFIN)
                    return error == noErr ? ioErr : error;
                error = MacStudioProtocolWriteByte(serialId, 'O');
                if (error == noErr)
                    error = MacStudioProtocolWriteByte(serialId, 'O');
                return error;
            }

            static OSErr MacStudioZmodemReceive(
                const char *serialId, short timeoutSeconds, unsigned char *data,
                long capacity, long *length, short componentIndex)
            {
                unsigned char type;
                unsigned char fields[4];
                short useCrc32;
                unsigned char metadata[256];
                unsigned char endMarker;
                long metadataLength;
                long packetLength;
                long total = 0;
                short attempts = 0;
                OSErr error;
                *length = 0;

                for (;;)
                {
                    error = MacStudioZReadHeader(
                        serialId, timeoutSeconds, &type, fields, &useCrc32);
                    if (error != noErr)
                    {
                        if (++attempts >= 10)
                            return error;
                        error = MacStudioZSendReceiverInit(serialId);
                        if (error != noErr)
                            return error;
                        continue;
                    }
                    attempts = 0;
                    if (type == MS_ZRQINIT)
                    {
                        error = MacStudioZSendReceiverInit(serialId);
                        if (error != noErr)
                            return error;
                        continue;
                    }
                    if (type != MS_ZFILE)
                        return ioErr;
                    error = MacStudioZReadSubpacket(
                        serialId, timeoutSeconds, useCrc32, metadata,
                        sizeof(metadata), &metadataLength, &endMarker);
                    if (error != noErr || endMarker != MS_ZCRCW)
                        return error == noErr ? ioErr : error;
                    break;
                }

                fields[0] = 0;
                fields[1] = 0;
                fields[2] = 0;
                fields[3] = 0;
                error = MacStudioZWriteHeader(
                    serialId, MS_ZRPOS, fields, 0, 1);
                if (error != noErr)
                    return error;

                for (;;)
                {
                    error = MacStudioZReadHeader(
                        serialId, timeoutSeconds, &type, fields, &useCrc32);
                    if (error != noErr)
                        return error;
                    if (type == MS_ZDATA)
                    {
                        unsigned long position = MacStudioZGetPosition(fields);
                        if (position != (unsigned long)total)
                        {
                            MacStudioZSetPosition(fields, (unsigned long)total);
                            error = MacStudioZWriteHeader(
                                serialId, MS_ZRPOS, fields, 0, 1);
                            if (error != noErr)
                                return error;
                            continue;
                        }
                        for (;;)
                        {
                            if (total > capacity)
                            {
                                MacStudioZSendCancel(serialId);
                                return memFullErr;
                            }
                            error = MacStudioZReadSubpacket(
                                serialId, timeoutSeconds, useCrc32, data + total,
                                capacity - total, &packetLength, &endMarker);
                            if (error == memFullErr)
                            {
                                MacStudioZSendCancel(serialId);
                                return error;
                            }
                            if (error != noErr)
                            {
                                MacStudioZSetPosition(fields, (unsigned long)total);
                                MacStudioZWriteHeader(
                                    serialId, MS_ZRPOS, fields, 0, 1);
                                break;
                            }
                            total += packetLength;
                            if (capacity > 0)
                            {
                                short percent = MacStudioProtocolPercent(total, capacity);
                                if (percent > 99)
                                    percent = 99;
                                MacStudioProtocolProgressStore(
                                    componentIndex, percent);
                            }
                            if (endMarker == MS_ZCRCW || endMarker == MS_ZCRCQ)
                            {
                                MacStudioZSetPosition(fields, (unsigned long)total);
                                error = MacStudioZWriteHeader(
                                    serialId, MS_ZACK, fields, 0, 1);
                                if (error != noErr)
                                    return error;
                            }
                            if (endMarker == MS_ZCRCE || endMarker == MS_ZCRCW)
                                break;
                        }
                        continue;
                    }
                    if (type == MS_ZEOF)
                    {
                        if (MacStudioZGetPosition(fields) != (unsigned long)total)
                        {
                            MacStudioZSetPosition(fields, (unsigned long)total);
                            error = MacStudioZWriteHeader(
                                serialId, MS_ZRPOS, fields, 0, 1);
                            if (error != noErr)
                                return error;
                            continue;
                        }
                        error = MacStudioZSendReceiverInit(serialId);
                        if (error != noErr)
                            return error;
                        break;
                    }
                    if (type == MS_ZABORT)
                        return abortErr;
                    if (type == MS_ZFIN)
                    {
                        error = MacStudioZWriteHeader(
                            serialId, MS_ZFIN, fields, 0, 1);
                        if (error == noErr)
                            error = MacStudioProtocolWriteByte(serialId, 'O');
                        if (error == noErr)
                            error = MacStudioProtocolWriteByte(serialId, 'O');
                        if (error == noErr)
                            return ioErr;
                        return error;
                    }
                    return ioErr;
                }

                for (attempts = 0; attempts < 10; ++attempts)
                {
                    error = MacStudioZReadHeader(
                        serialId, timeoutSeconds, &type, fields, &useCrc32);
                    if (error == noErr && type == MS_ZFIN)
                        break;
                    if (error != noErr)
                        return error;
                }
                if (attempts == 10)
                    return ioErr;
                error = MacStudioZWriteHeader(
                    serialId, MS_ZFIN, fields, 0, 1);
                if (error == noErr)
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &type);
                if (error == noErr && type == 'O')
                    error = MacStudioProtocolReadByte(
                        serialId, timeoutSeconds, &type);
                if (error != noErr || type != 'O')
                    return error == noErr ? ioErr : error;
                *length = total;
                MacStudioProtocolProgressStore(componentIndex, 100);
                return noErr;
            }

            short MacStudioProtocolSend(
                const char *componentId, const unsigned char *data, long length)
            {
                const char *serialId;
                const char *fileName;
                short mode;
                short timeoutSeconds;
                short componentIndex;
                short serialIndex;
                OSErr error;
                if (componentId == NULL || (data == NULL && length > 0) || length < 0)
                    return paramErr;
                if (!MacStudioProtocolConfiguration(
                        componentId, &serialId, &mode, &timeoutSeconds,
                        &fileName, &componentIndex))
                    return paramErr;
                serialIndex = MacStudioNonVisualComponentIndex(serialId);
                if (serialIndex == 0)
                    return MacStudioProtocolFailure(componentId, paramErr);
                if (!MacStudioSerialIsOpen(serialIndex))
                {
                    error = MacStudioSerialOpen(serialId);
                    if (error != noErr)
                        return MacStudioProtocolFailure(componentId, error);
                }
                MacStudioProtocolProgressStore(componentIndex, 0);
                error = mode == 3
                    ? MacStudioZmodemSend(
                        serialId, timeoutSeconds, fileName, data, length,
                        componentIndex)
                    : MacStudioXmodemSend(
                        serialId, mode, timeoutSeconds, data, length,
                        componentIndex);
                if (error != noErr)
                {
                    if (mode == 3)
                        MacStudioZSendCancel(serialId);
                    return MacStudioProtocolFailure(componentId, error);
                }
                MacStudioProtocolProgressStore(componentIndex, 100);
                MacStudioProtocolComplete(componentId);
                return noErr;
            }

            static short MacStudioProtocolReceiveCore(
                const char *componentId, unsigned char *data, long capacity,
                long expectedLength, long *length)
            {
                const char *serialId;
                const char *fileName;
                short mode;
                short timeoutSeconds;
                short componentIndex;
                short serialIndex;
                OSErr error;
                if (componentId == NULL || data == NULL || length == NULL ||
                    capacity < 0 || expectedLength < -1 ||
                    (expectedLength >= 0 &&
                     (expectedLength > capacity ||
                      expectedLength > 0x7FFFFFFFL - 127)))
                    return paramErr;
                *length = 0;
                if (!MacStudioProtocolConfiguration(
                        componentId, &serialId, &mode, &timeoutSeconds,
                        &fileName, &componentIndex))
                    return paramErr;
                if (expectedLength >= 0 && mode == 3)
                    return paramErr;
                serialIndex = MacStudioNonVisualComponentIndex(serialId);
                if (serialIndex == 0)
                    return MacStudioProtocolFailure(componentId, paramErr);
                if (!MacStudioSerialIsOpen(serialIndex))
                {
                    error = MacStudioSerialOpen(serialId);
                    if (error != noErr)
                        return MacStudioProtocolFailure(componentId, error);
                }
                MacStudioProtocolProgressStore(componentIndex, 0);
                error = mode == 3
                    ? MacStudioZmodemReceive(
                        serialId, timeoutSeconds, data, capacity, length,
                        componentIndex)
                    : MacStudioXmodemReceive(
                        serialId, mode, timeoutSeconds, data, capacity, length,
                        componentIndex, expectedLength);
                if (error != noErr)
                {
                    if (mode == 3)
                        MacStudioZSendCancel(serialId);
                    return MacStudioProtocolFailure(componentId, error);
                }
                MacStudioProtocolProgressStore(componentIndex, 100);
                MacStudioProtocolComplete(componentId);
                return noErr;
            }

            short MacStudioProtocolReceive(
                const char *componentId, unsigned char *data, long capacity,
                long *length)
            {
                return MacStudioProtocolReceiveCore(
                    componentId, data, capacity, -1, length);
            }

            short MacStudioProtocolReceiveExact(
                const char *componentId, unsigned char *data, long capacity,
                long expectedLength, long *length)
            {
                if (expectedLength < 0)
                    return paramErr;
                return MacStudioProtocolReceiveCore(
                    componentId, data, capacity, expectedLength, length);
            }

            short MacStudioProtocolGetProgress(
                const char *componentId, short *percentage)
            {
                const char *serialId;
                const char *fileName;
                short mode;
                short timeoutSeconds;
                short componentIndex;
                if (componentId == NULL || percentage == NULL)
                    return paramErr;
                if (!MacStudioProtocolConfiguration(
                        componentId, &serialId, &mode, &timeoutSeconds,
                        &fileName, &componentIndex))
                    return paramErr;
                *percentage = MacStudioProtocolProgressValue(componentIndex);
                return noErr;
            }
            """);
        source.AppendLine();
    }
}
