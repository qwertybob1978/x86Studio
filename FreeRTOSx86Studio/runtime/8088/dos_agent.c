#include <ctype.h>
#include <conio.h>
#include <dos.h>
#include <errno.h>
#include <i86.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "FreeRTOS.h"
#include "task.h"
#include "dos_agent.h"

#define DOS_AGENT_PROTOCOL_VERSION       1U
#define DOS_AGENT_HEADER_SIZE            26U
#define DOS_AGENT_CRC_SIZE               2U
#define DOS_AGENT_MAX_PAYLOAD_SIZE       1024U
#define DOS_AGENT_FRAME_BUFFER_SIZE      ( DOS_AGENT_HEADER_SIZE + DOS_AGENT_MAX_PAYLOAD_SIZE + DOS_AGENT_CRC_SIZE )
#define DOS_AGENT_RX_BUFFER_SIZE         256U
#define DOS_AGENT_DEBUG_MESSAGE_SIZE     128U
#define DOS_AGENT_DEBUG_QUEUE_LENGTH     4U
#define DOS_AGENT_DEBUG_QUEUE_MASK       3U
#define DOS_AGENT_MAX_TX_PAYLOAD_SIZE    ( 7U + DOS_AGENT_DEBUG_MESSAGE_SIZE )
#define DOS_AGENT_DEBUG_RETRY_TICKS      18U
#define DOS_AGENT_PUT_BEGIN              0x10U
#define DOS_AGENT_READY                  0x11U
#define DOS_AGENT_PUT_COMPLETE           0x12U
#define DOS_AGENT_XMODEM_BLOCK_SIZE      128U
#define DOS_AGENT_XMODEM_PACKET_SIZE     ( 2U + DOS_AGENT_XMODEM_BLOCK_SIZE + 2U )
#define DOS_AGENT_XMODEM_REQUEST_TICKS   configTICK_RATE_HZ
#define DOS_AGENT_XMODEM_TIMEOUT_TICKS   ( configTICK_RATE_HZ * 3U )
#define DOS_AGENT_TRANSFER_TIMEOUT_TICKS ( configTICK_RATE_HZ * 40U )
#define DOS_AGENT_TRANSFER_TEMP_NAME     "DOSAGT.$$$"
#define DOS_AGENT_TRANSFER_BACKUP_NAME   "DOSAGT.BAK"
#define DOS_AGENT_TRANSFER_INVALID       1U
#define DOS_AGENT_TRANSFER_UNSUPPORTED   2U
#define DOS_AGENT_TRANSFER_OPEN_FAILED   3U
#define DOS_AGENT_TRANSFER_NO_SPACE      4U
#define DOS_AGENT_TRANSFER_IO_FAILED    5U
#define DOS_AGENT_TRANSFER_BAD_CRC       6U

typedef struct DosAgentDebugMessage
{
    unsigned long sequence;
    unsigned short length;
    unsigned char severity;
    char text[DOS_AGENT_DEBUG_MESSAGE_SIZE];
} DosAgentDebugMessage;

typedef void ( __interrupt __far * DosAgentInterrupt )( void );

static unsigned short usUartBase;
static unsigned char ucUartIrq;
static unsigned char ucOldUartInterruptMask;
static unsigned char ucOldUartInterruptEnable;
static unsigned char ucOldUartLineControl;
static unsigned char ucOldUartModemControl;
static unsigned char ucOldUartDivisorLow;
static unsigned char ucOldUartDivisorHigh;
static unsigned char ucUartInitialized;
static unsigned char ucAgentInitialized;
static DosAgentInterrupt pxOldUartInterrupt;
static volatile unsigned char ucReceiveBuffer[DOS_AGENT_RX_BUFFER_SIZE];
static volatile unsigned short usReceiveHead;
static volatile unsigned short usReceiveTail;
static unsigned char ucFrameBuffer[DOS_AGENT_FRAME_BUFFER_SIZE];
static unsigned short usFrameLength;
static unsigned char ucSessionId[16];
static unsigned char ucSessionActive;
static DosAgentDebugMessage xDebugQueue[DOS_AGENT_DEBUG_QUEUE_LENGTH];
static unsigned char ucDebugQueueHead;
static unsigned char ucDebugQueueTail;
static unsigned char ucPendingDebugActive;
static unsigned char ucPendingDebugSent;
static DosAgentDebugMessage xPendingDebug;
static unsigned long ulNextDebugSequence = 1UL;
static TickType_t xLastDebugSendTick;
static FILE * pxTransferFile;
static unsigned char ucTransferActive;
static unsigned char ucXmodemStarted;
static unsigned char ucXmodemPacketActive;
static unsigned char ucTransferHasBlock;
static unsigned char ucXmodemPacket[DOS_AGENT_XMODEM_PACKET_SIZE];
static unsigned short usXmodemPacketLength;
static unsigned char ucTransferNextBlock;
static unsigned char ucTransferLastBlock;
static unsigned char ucTransferDestination[13];
static unsigned char ucTransferBuildId[16];
static unsigned long ulTransferBytesRemaining;
static unsigned long ulTransferExpectedCrc32;
static unsigned long ulTransferRunningCrc32;
static TickType_t xTransferStartedTick;
static TickType_t xTransferLastByteTick;
static TickType_t xTransferLastRequestTick;

#if defined(FREERTOSX86_SMOKE)
static unsigned long ulSmokeReceivedBytes;
static unsigned long ulSmokeValidFrames;
static unsigned long ulSmokeInvalidCrcFrames;
static unsigned long ulSmokeTaskRuns;
static unsigned long ulSmokeInterruptBytes;
static unsigned long ulSmokePolledBytes;
#endif

static unsigned short prvReadUInt16(const unsigned char * bytes)
{
    return (unsigned short)(
        ((unsigned short)bytes[0] << 8) |
        (unsigned short)bytes[1]);
}

static unsigned long prvReadUInt32(const unsigned char * bytes)
{
    return ((unsigned long)bytes[0] << 24) |
        ((unsigned long)bytes[1] << 16) |
        ((unsigned long)bytes[2] << 8) |
        (unsigned long)bytes[3];
}

static void prvWriteUInt16(unsigned char * bytes, unsigned short value)
{
    bytes[0] = (unsigned char)(value >> 8);
    bytes[1] = (unsigned char)value;
}

static void prvWriteUInt32(unsigned char * bytes, unsigned long value)
{
    bytes[0] = (unsigned char)(value >> 24);
    bytes[1] = (unsigned char)(value >> 16);
    bytes[2] = (unsigned char)(value >> 8);
    bytes[3] = (unsigned char)value;
}

static unsigned short prvCrc16(
    const unsigned char * bytes,
    unsigned short length)
{
    unsigned short crc = 0xFFFFU;
    unsigned short index;

    for (index = 0U; index < length; index++)
    {
        unsigned char bit;

        crc ^= (unsigned short)((unsigned short)bytes[index] << 8);
        for (bit = 0U; bit < 8U; bit++)
        {
            crc = (crc & 0x8000U) != 0U
                ? (unsigned short)((crc << 1) ^ 0x1021U)
                : (unsigned short)(crc << 1);
        }
    }

    return crc;
}

static unsigned short prvParseNumber(
    const char * text,
    unsigned long * value)
{
    char * end;
    unsigned long parsed;

    parsed = strtoul(text, &end, 0);
    if ((end == text) || (*end != '\0'))
    {
        return 0U;
    }

    *value = parsed;
    return 1U;
}

static void prvTrim(char * text)
{
    char * start = text;
    char * end;

    while ((*start != '\0') && isspace((unsigned char)*start))
    {
        start++;
    }
    if (start != text)
    {
        memmove(text, start, strlen(start) + 1U);
    }

    end = text + strlen(text);
    while ((end > text) && isspace((unsigned char)end[-1]))
    {
        end--;
        *end = '\0';
    }
}

static unsigned short prvLoadConfiguration(
    unsigned short * base,
    unsigned char * irq)
{
    FILE * file;
    char line[80];
    unsigned char seenBase = 0U;
    unsigned char seenIrq = 0U;
    unsigned long parsed;

    *base = 0x03F8U;
    *irq = 4U;
    file = fopen("DOSAGENT.CFG", "rt");
    if (file == NULL)
    {
        return (errno == ENOENT) ? 1U : 0U;
    }

    while (fgets(line, sizeof(line), file) != NULL)
    {
        char * separator;

        if ((strchr(line, '\n') == NULL) && !feof(file))
        {
            fclose(file);
            return 0U;
        }
        prvTrim(line);
        if ((line[0] == '\0') || (line[0] == '#') || (line[0] == ';'))
        {
            continue;
        }

        separator = strchr(line, '=');
        if (separator == NULL)
        {
            fclose(file);
            return 0U;
        }
        *separator = '\0';
        separator++;
        prvTrim(line);
        prvTrim(separator);
        if (!prvParseNumber(separator, &parsed))
        {
            fclose(file);
            return 0U;
        }

        if (strcmp(line, "BASE") == 0)
        {
            if ((seenBase != 0U) || (parsed < 0x0100UL) || (parsed > 0x0FFFUL))
            {
                fclose(file);
                return 0U;
            }
            *base = (unsigned short)parsed;
            seenBase = 1U;
        }
        else if (strcmp(line, "IRQ") == 0)
        {
            if ((seenIrq != 0U) || ((parsed != 3UL) && (parsed != 4UL)))
            {
                fclose(file);
                return 0U;
            }
            *irq = (unsigned char)parsed;
            seenIrq = 1U;
        }
        else
        {
            fclose(file);
            return 0U;
        }
    }

    if (ferror(file) != 0)
    {
        fclose(file);
        return 0U;
    }
    if (fclose(file) != 0)
    {
        return 0U;
    }

    return 1U;
}

static unsigned char prvIrqVector(void)
{
    return (unsigned char)(0x08U + ucUartIrq);
}

static void __interrupt __far prvUartInterrupt(void)
{
    unsigned short nextHead;

    while ((inp(usUartBase + 5U) & 0x01U) != 0U)
    {
        unsigned char value = (unsigned char)inp(usUartBase);

#if defined(FREERTOSX86_SMOKE)
        ulSmokeInterruptBytes++;
#endif
        nextHead = (unsigned short)((usReceiveHead + 1U) & 0x00FFU);
        if (nextHead != usReceiveTail)
        {
            ucReceiveBuffer[usReceiveHead] = value;
            usReceiveHead = nextHead;
        }
    }

    outp(0x20U, 0x20U);
}

static unsigned short prvInitializeUart(void)
{
    unsigned char masterMask;
    unsigned char interruptMask;
    unsigned char lineControl;

    pxOldUartInterrupt = _dos_getvect(prvIrqVector());
    ucOldUartInterruptMask = (unsigned char)inp(0x21U);
    interruptMask = (unsigned char)(1U << ucUartIrq);
    portENTER_CRITICAL();
    ucOldUartInterruptEnable = (unsigned char)inp(usUartBase + 1U);
    outp(usUartBase + 1U, 0U);
    lineControl = (unsigned char)inp(usUartBase + 3U);
    ucOldUartLineControl = lineControl;
    outp(usUartBase + 3U, (unsigned char)(lineControl | 0x80U));
    ucOldUartDivisorLow = (unsigned char)inp(usUartBase);
    ucOldUartDivisorHigh = (unsigned char)inp(usUartBase + 1U);
    ucOldUartModemControl = (unsigned char)inp(usUartBase + 4U);

    outp(usUartBase + 3U, 0x80U);
    outp(usUartBase, 12U);
    outp(usUartBase + 1U, 0U);
    outp(usUartBase + 3U, 0x03U);
    outp(usUartBase + 2U, 0x07U);
    outp(usUartBase + 4U, 0x0BU);
    _dos_setvect(prvIrqVector(), prvUartInterrupt);
    masterMask = (unsigned char)(ucOldUartInterruptMask & (unsigned char)~interruptMask);
    outp(0x21U, masterMask);
    outp(usUartBase + 1U, 0x01U);
    portEXIT_CRITICAL();

    ucUartInitialized = 1U;
    return 1U;
}

static void prvRestoreUart(void)
{
    unsigned char currentMask;
    unsigned char interruptMask;
    unsigned char lineControl;

    if (ucUartInitialized == 0U)
    {
        return;
    }

    portENTER_CRITICAL();
    outp(usUartBase + 1U, 0U);
    _dos_setvect(prvIrqVector(), pxOldUartInterrupt);
    interruptMask = (unsigned char)(1U << ucUartIrq);
    currentMask = (unsigned char)inp(0x21U);
    currentMask = (unsigned char)(
        (currentMask & (unsigned char)~interruptMask) |
        (ucOldUartInterruptMask & interruptMask));
    outp(0x21U, currentMask);

    outp(usUartBase + 3U, 0x80U);
    outp(usUartBase, ucOldUartDivisorLow);
    outp(usUartBase + 1U, ucOldUartDivisorHigh);
    lineControl = ucOldUartLineControl;
    outp(usUartBase + 3U, lineControl);
    outp(usUartBase + 4U, ucOldUartModemControl);
    outp(usUartBase + 1U, ucOldUartInterruptEnable);
    ucUartInitialized = 0U;
    portEXIT_CRITICAL();
}

static unsigned short prvReadUartByte(unsigned char * value)
{
    unsigned short hasByte = 0U;

    portENTER_CRITICAL();
    if (usReceiveTail != usReceiveHead)
    {
        *value = ucReceiveBuffer[usReceiveTail];
        usReceiveTail = (unsigned short)((usReceiveTail + 1U) & 0x00FFU);
        hasByte = 1U;
    }
    else if ((inp(usUartBase + 5U) & 0x01U) != 0U)
    {
        *value = (unsigned char)inp(usUartBase);
        hasByte = 1U;
#if defined(FREERTOSX86_SMOKE)
        ulSmokePolledBytes++;
#endif
    }
    portEXIT_CRITICAL();

    return hasByte;
}

static unsigned short prvWriteUartByte(unsigned char value)
{
    unsigned long attempt;

    for (attempt = 0UL; attempt < 0xFFFFUL; attempt++)
    {
        if ((inp(usUartBase + 5U) & 0x20U) != 0U)
        {
            outp(usUartBase, value);
            return 1U;
        }
    }

    return 0U;
}

static unsigned short prvSendFrame(
    unsigned char messageType,
    const unsigned char * sessionId,
    const unsigned char * payload,
    unsigned short payloadLength)
{
    unsigned char frame[DOS_AGENT_HEADER_SIZE + DOS_AGENT_MAX_TX_PAYLOAD_SIZE + DOS_AGENT_CRC_SIZE];
    unsigned short frameLength;
    unsigned short crc;
    unsigned short index;

    if ((payloadLength > DOS_AGENT_MAX_TX_PAYLOAD_SIZE) ||
        (payloadLength > DOS_AGENT_MAX_PAYLOAD_SIZE))
    {
        return 0U;
    }

    frame[0] = 'M';
    frame[1] = 'S';
    frame[2] = 'A';
    frame[3] = 'G';
    frame[4] = DOS_AGENT_PROTOCOL_VERSION;
    frame[5] = messageType;
    frame[6] = 0U;
    frame[7] = 0U;
    memcpy(frame + 8U, sessionId, 16U);
    prvWriteUInt16(frame + 24U, payloadLength);
    if (payloadLength != 0U)
    {
        memcpy(frame + DOS_AGENT_HEADER_SIZE, payload, payloadLength);
    }

    frameLength = (unsigned short)(DOS_AGENT_HEADER_SIZE + payloadLength);
    crc = prvCrc16(frame, frameLength);
    prvWriteUInt16(frame + frameLength, crc);
    frameLength = (unsigned short)(frameLength + DOS_AGENT_CRC_SIZE);
    for (index = 0U; index < frameLength; index++)
    {
        if (!prvWriteUartByte(frame[index]))
        {
            return 0U;
        }
    }

    return 1U;
}

static unsigned long prvGetFreeBytes(void)
{
    union REGS registers;
    unsigned long freeSectors;
    unsigned long bytesPerSector;

    registers.x.ax = 0x3600U;
    registers.x.dx = 0U;
    vTaskSuspendAll();
    int86(0x21, &registers, &registers);
    (void)xTaskResumeAll();
    if (registers.x.ax == 0xFFFFU)
    {
        return 0UL;
    }

    freeSectors = (unsigned long)registers.h.al *
        (unsigned long)registers.x.bx;
    bytesPerSector = (unsigned long)registers.x.cx;
    if ((bytesPerSector == 0UL) ||
        (freeSectors > (0xFFFFFFFFUL / bytesPerSector)))
    {
        return 0xFFFFFFFFUL;
    }

    return freeSectors * bytesPerSector;
}

static unsigned short prvSendHelloAck(const unsigned char * sessionId)
{
    unsigned char payload[20];

    payload[0] = DOS_AGENT_PROTOCOL_VERSION;
    payload[1] = 1U;
    payload[2] = 0U;
    payload[3] = 0U;
    payload[4] = 5U;
    payload[5] = 0U;
    prvWriteUInt16(payload + 6U, 0x8088U);
    prvWriteUInt32(payload + 8U, 1UL);
    prvWriteUInt32(payload + 12U, prvGetFreeBytes());
    prvWriteUInt32(payload + 16U, 1UL);

    if (!prvSendFrame(0x02U, sessionId, payload, sizeof(payload)))
    {
        return 0U;
    }
    memcpy(ucSessionId, sessionId, sizeof(ucSessionId));
    ucSessionActive = 1U;
    ucPendingDebugSent = 0U;
    return 1U;
}

static unsigned short prvDosFileNameCharacter(unsigned char value)
{
    return ((value >= 'A') && (value <= 'Z')) ||
        ((value >= 'a') && (value <= 'z')) ||
        ((value >= '0') && (value <= '9')) ||
        (value == '_') ||
        (value == '-') ||
        (value == '$');
}

static unsigned short prvValidateDosFileName(
    const unsigned char * name,
    unsigned char length,
    unsigned char * normalizedName)
{
    unsigned char index;
    unsigned char dotSeen = 0U;
    unsigned char baseLength = 0U;
    unsigned char extensionLength = 0U;

    if ((length == 0U) || (length > 12U))
    {
        return 0U;
    }

    for (index = 0U; index < length; index++)
    {
        unsigned char value = name[index];
        if (value == '.')
        {
            if ((dotSeen != 0U) || (baseLength == 0U))
            {
                return 0U;
            }
            dotSeen = 1U;
            normalizedName[index] = '.';
        }
        else
        {
            if (!prvDosFileNameCharacter(value))
            {
                return 0U;
            }
            if (dotSeen != 0U)
            {
                extensionLength++;
            }
            else
            {
                baseLength++;
            }
            normalizedName[index] = (unsigned char)toupper(value);
        }
    }

    if ((baseLength > 8U) ||
        ((dotSeen != 0U) &&
         ((extensionLength == 0U) || (extensionLength > 3U))))
    {
        return 0U;
    }

    normalizedName[length] = '\0';
    if ((strcmp((const char *)normalizedName, DOS_AGENT_TRANSFER_TEMP_NAME) == 0) ||
        (strcmp((const char *)normalizedName, DOS_AGENT_TRANSFER_BACKUP_NAME) == 0))
    {
        return 0U;
    }

    return 1U;
}

static short prvDOSFileExists(const char * name)
{
    FILE * file;
    short result;

    vTaskSuspendAll();
    errno = 0;
    file = fopen(name, "rb");
    if (file == NULL)
    {
        result = (errno == ENOENT) ? 0 : -1;
    }
    else
    {
        result = (fclose(file) == 0) ? 1 : -1;
    }
    (void)xTaskResumeAll();

    return result;
}

static unsigned short prvRemoveDOSFile(const char * name)
{
    int result;

    vTaskSuspendAll();
    result = remove(name);
    (void)xTaskResumeAll();
    return (result == 0);
}

static unsigned short prvRenameDOSFile(
    const char * oldName,
    const char * newName)
{
    int result;

    vTaskSuspendAll();
    result = rename(oldName, newName);
    (void)xTaskResumeAll();
    return (result == 0);
}

static unsigned short prvCloseTransferFile(void)
{
    unsigned short result = 1U;

    if (pxTransferFile == NULL)
    {
        return 1U;
    }

    vTaskSuspendAll();
    if (fclose(pxTransferFile) != 0)
    {
        result = 0U;
    }
    pxTransferFile = NULL;
    (void)xTaskResumeAll();

    return result;
}

static unsigned short prvWriteTransferData(
    const unsigned char * data,
    unsigned short length)
{
    unsigned short result;

    vTaskSuspendAll();
    result = (fwrite(data, 1U, length, pxTransferFile) == length);
    (void)xTaskResumeAll();

    return result;
}

static unsigned short prvAppendXmodemCrc16(
    const unsigned char * bytes,
    unsigned short length)
{
    unsigned short crc = 0U;
    unsigned short index;

    for (index = 0U; index < length; index++)
    {
        unsigned char bit;

        crc ^= (unsigned short)((unsigned short)bytes[index] << 8);
        for (bit = 0U; bit < 8U; bit++)
        {
            crc = (crc & 0x8000U) != 0U
                ? (unsigned short)((crc << 1) ^ 0x1021U)
                : (unsigned short)(crc << 1);
        }
    }

    return crc;
}

static unsigned long prvAppendTransferCrc32(
    unsigned long crc,
    const unsigned char * bytes,
    unsigned short length)
{
    unsigned short index;

    for (index = 0U; index < length; index++)
    {
        unsigned char bit;

        crc ^= (unsigned long)bytes[index];
        for (bit = 0U; bit < 8U; bit++)
        {
            crc = (crc & 1UL) != 0UL
                ? (crc >> 1) ^ 0xEDB88320UL
                : crc >> 1;
        }
    }

    return crc;
}

static void prvResetTransfer(unsigned short removeTemporaryFile)
{
    (void)prvCloseTransferFile();
    if (removeTemporaryFile != 0U)
    {
        (void)prvRemoveDOSFile(DOS_AGENT_TRANSFER_TEMP_NAME);
    }

    ucTransferActive = 0U;
    ucXmodemStarted = 0U;
    ucXmodemPacketActive = 0U;
    ucTransferHasBlock = 0U;
    usXmodemPacketLength = 0U;
    ulTransferBytesRemaining = 0UL;
}

static void prvAbortTransfer(unsigned short notifyPeer)
{
    if (notifyPeer != 0U)
    {
        (void)prvWriteUartByte(0x18U);
        (void)prvWriteUartByte(0x18U);
    }
    prvResetTransfer(1U);
}

static unsigned short prvSendReady(
    const unsigned char * sessionId,
    unsigned char result)
{
    return prvSendFrame(DOS_AGENT_READY, sessionId, &result, 1U);
}

static void prvHandlePutBegin(
    const unsigned char * sessionId,
    const unsigned char * payload,
    unsigned short payloadLength)
{
    unsigned char destinationLength;
    unsigned long fileLength;
    unsigned long freeBytes;
    short backupExists;
    short temporaryExists;
    unsigned char readyResult = 0U;

    if ((payloadLength < 27U) ||
        ((unsigned short)(26U + payload[25]) != payloadLength) ||
        (payload[25] == 0U))
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_INVALID);
        return;
    }

    if (payload[24] != 0U)
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_UNSUPPORTED);
        return;
    }

    destinationLength = payload[25];
    if (!prvValidateDosFileName(
            payload + 26U,
            destinationLength,
            ucTransferDestination))
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_INVALID);
        return;
    }

    fileLength = prvReadUInt32(payload + 16U);
    if (fileLength == 0UL)
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_INVALID);
        return;
    }

    freeBytes = prvGetFreeBytes();
    if ((freeBytes != 0UL) && (fileLength > freeBytes))
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_NO_SPACE);
        return;
    }

    backupExists = prvDOSFileExists(DOS_AGENT_TRANSFER_BACKUP_NAME);
    if (backupExists != 0)
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_IO_FAILED);
        return;
    }

    temporaryExists = prvDOSFileExists(DOS_AGENT_TRANSFER_TEMP_NAME);
    if (temporaryExists < 0)
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_IO_FAILED);
        return;
    }
    if ((temporaryExists > 0) &&
        !prvRemoveDOSFile(DOS_AGENT_TRANSFER_TEMP_NAME))
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_IO_FAILED);
        return;
    }

    vTaskSuspendAll();
    pxTransferFile = fopen(DOS_AGENT_TRANSFER_TEMP_NAME, "wb");
    (void)xTaskResumeAll();
    if (pxTransferFile == NULL)
    {
        (void)prvSendReady(sessionId, DOS_AGENT_TRANSFER_OPEN_FAILED);
        return;
    }

    memcpy(ucTransferBuildId, payload, sizeof(ucTransferBuildId));
    ulTransferBytesRemaining = fileLength;
    ulTransferExpectedCrc32 = prvReadUInt32(payload + 20U);
    ulTransferRunningCrc32 = 0xFFFFFFFFUL;
    ucTransferNextBlock = 1U;
    ucTransferLastBlock = 0U;
    ucTransferHasBlock = 0U;
    usXmodemPacketLength = 0U;
    ucXmodemStarted = 0U;
    ucXmodemPacketActive = 0U;
    ucTransferActive = 1U;
    xTransferStartedTick = xTaskGetTickCount();
    xTransferLastByteTick = xTransferStartedTick;
    xTransferLastRequestTick = (TickType_t)(
        xTransferStartedTick - (TickType_t)DOS_AGENT_XMODEM_REQUEST_TICKS);

    if (!prvSendReady(sessionId, readyResult))
    {
        prvAbortTransfer(0U);
    }
}

static unsigned short prvCommitTransferFile(void)
{
    short destinationExists;
    short backupExists;

    destinationExists = prvDOSFileExists((const char *)ucTransferDestination);
    backupExists = prvDOSFileExists(DOS_AGENT_TRANSFER_BACKUP_NAME);
    if ((destinationExists < 0) || (backupExists != 0))
    {
        return 0U;
    }

    if (destinationExists == 0)
    {
        return prvRenameDOSFile(
            DOS_AGENT_TRANSFER_TEMP_NAME,
            (const char *)ucTransferDestination);
    }

    if (!prvRenameDOSFile(
            (const char *)ucTransferDestination,
            DOS_AGENT_TRANSFER_BACKUP_NAME))
    {
        return 0U;
    }

    if (!prvRenameDOSFile(
            DOS_AGENT_TRANSFER_TEMP_NAME,
            (const char *)ucTransferDestination))
    {
        (void)prvRenameDOSFile(
            DOS_AGENT_TRANSFER_BACKUP_NAME,
            (const char *)ucTransferDestination);
        return 0U;
    }

    (void)prvRemoveDOSFile(DOS_AGENT_TRANSFER_BACKUP_NAME);
    return 1U;
}

static void prvSendPutComplete(
    unsigned char result,
    unsigned long verifiedCrc32)
{
    unsigned char payload[21];

    memcpy(payload, ucTransferBuildId, sizeof(ucTransferBuildId));
    payload[16] = result;
    prvWriteUInt32(payload + 17U, verifiedCrc32);
    (void)prvSendFrame(
        DOS_AGENT_PUT_COMPLETE,
        ucSessionId,
        payload,
        sizeof(payload));
}

static void prvFinishTransfer(void)
{
    unsigned long verifiedCrc32 = ~ulTransferRunningCrc32;
    unsigned char result = 0U;

    if (ulTransferBytesRemaining != 0UL)
    {
        (void)prvWriteUartByte(0x15U);
        return;
    }

    if (!prvCloseTransferFile())
    {
        result = DOS_AGENT_TRANSFER_IO_FAILED;
    }
    else if (verifiedCrc32 != ulTransferExpectedCrc32)
    {
        result = DOS_AGENT_TRANSFER_BAD_CRC;
    }
    else if (!prvCommitTransferFile())
    {
        result = DOS_AGENT_TRANSFER_IO_FAILED;
    }

    (void)prvWriteUartByte(0x06U);
    if (result != 0U)
    {
        (void)prvRemoveDOSFile(DOS_AGENT_TRANSFER_TEMP_NAME);
    }
    prvSendPutComplete(result, verifiedCrc32);
    prvResetTransfer(0U);
}

static void prvProcessXmodemByte(unsigned char value)
{
    if (value == 0x18U)
    {
        prvAbortTransfer(0U);
        return;
    }

    if (ucXmodemPacketActive == 0U)
    {
        if (value == 0x01U)
        {
            ucXmodemStarted = 1U;
            ucXmodemPacketActive = 1U;
            usXmodemPacketLength = 0U;
            return;
        }

        if (value == 0x04U)
        {
            prvFinishTransfer();
        }
        return;
    }

    ucXmodemPacket[usXmodemPacketLength++] = value;
    if (usXmodemPacketLength == DOS_AGENT_XMODEM_PACKET_SIZE)
    {
        unsigned char blockNumber = ucXmodemPacket[0];
        unsigned char blockNumberInverse = ucXmodemPacket[1];
        unsigned short receivedCrc = prvReadUInt16(
            ucXmodemPacket + 2U + DOS_AGENT_XMODEM_BLOCK_SIZE);
        unsigned short actualCrc = prvAppendXmodemCrc16(
            ucXmodemPacket + 2U,
            DOS_AGENT_XMODEM_BLOCK_SIZE);

        ucXmodemPacketActive = 0U;
        usXmodemPacketLength = 0U;
        if ((unsigned char)~blockNumber != blockNumberInverse ||
            (receivedCrc != actualCrc))
        {
            (void)prvWriteUartByte(0x15U);
            return;
        }

        if ((ucTransferHasBlock != 0U) &&
            (blockNumber == ucTransferLastBlock))
        {
            (void)prvWriteUartByte(0x06U);
            return;
        }

        if ((blockNumber != ucTransferNextBlock) ||
            (ulTransferBytesRemaining == 0UL))
        {
            (void)prvWriteUartByte(0x15U);
            return;
        }

        {
            unsigned short dataLength = (ulTransferBytesRemaining <
                    (unsigned long)DOS_AGENT_XMODEM_BLOCK_SIZE)
                ? (unsigned short)ulTransferBytesRemaining
                : DOS_AGENT_XMODEM_BLOCK_SIZE;

            if (!prvWriteTransferData(ucXmodemPacket + 2U, dataLength))
            {
                prvAbortTransfer(1U);
                return;
            }

            ulTransferRunningCrc32 = prvAppendTransferCrc32(
                ulTransferRunningCrc32,
                ucXmodemPacket + 2U,
                dataLength);
            ulTransferBytesRemaining -= (unsigned long)dataLength;
            ucTransferLastBlock = blockNumber;
            ucTransferHasBlock = 1U;
            ucTransferNextBlock = (unsigned char)(blockNumber + 1U);
            (void)prvWriteUartByte(0x06U);
        }
    }
}

static void prvServiceTransfer(void)
{
    unsigned char value;
    TickType_t now;

    while ((ucTransferActive != 0U) &&
           (prvReadUartByte(&value) != 0U))
    {
        now = xTaskGetTickCount();
        xTransferLastByteTick = now;
#if defined(FREERTOSX86_SMOKE)
        ulSmokeReceivedBytes++;
#endif
        prvProcessXmodemByte(value);
    }

    if (ucTransferActive == 0U)
    {
        return;
    }

    now = xTaskGetTickCount();
    if ((ucXmodemStarted == 0U) &&
        ((TickType_t)(now - xTransferLastRequestTick) >=
            (TickType_t)DOS_AGENT_XMODEM_REQUEST_TICKS))
    {
        if (!prvWriteUartByte((unsigned char)'C'))
        {
            prvAbortTransfer(0U);
            return;
        }
        xTransferLastRequestTick = now;
    }

    if ((ucXmodemPacketActive != 0U) &&
        ((TickType_t)(now - xTransferLastByteTick) >=
            (TickType_t)DOS_AGENT_XMODEM_TIMEOUT_TICKS))
    {
        ucXmodemPacketActive = 0U;
        usXmodemPacketLength = 0U;
        (void)prvWriteUartByte(0x15U);
    }
    else if (((TickType_t)(now - xTransferLastByteTick) >=
            (TickType_t)DOS_AGENT_TRANSFER_TIMEOUT_TICKS))
    {
        prvAbortTransfer(1U);
    }
}

static void prvHandleFrame(
    const unsigned char * frame,
    unsigned short payloadLength)
{
    unsigned char messageType = frame[5];
    const unsigned char * sessionId = frame + 8U;
    const unsigned char * payload = frame + DOS_AGENT_HEADER_SIZE;

    if (messageType == 0x01U)
    {
        if ((payloadLength == 4U) &&
            (payload[0] <= DOS_AGENT_PROTOCOL_VERSION) &&
            (payload[1] >= DOS_AGENT_PROTOCOL_VERSION))
        {
            (void)prvSendHelloAck(sessionId);
        }
        return;
    }

    if ((ucSessionActive == 0U) ||
        (memcmp(sessionId, ucSessionId, sizeof(ucSessionId)) != 0))
    {
        return;
    }

    if (messageType == 0x7EU)
    {
        ucSessionActive = 0U;
        ucPendingDebugSent = 0U;
    }
    else if ((messageType == 0x31U) && (payloadLength == 5U))
    {
        if ((prvReadUInt32(payload) == xPendingDebug.sequence) &&
            (ucPendingDebugActive != 0U))
        {
            if (payload[4] == 0U)
            {
                ucPendingDebugActive = 0U;
                ucPendingDebugSent = 0U;
            }
            else
            {
                ucPendingDebugSent = 0U;
            }
        }
    }
    else if (messageType == DOS_AGENT_PUT_BEGIN)
    {
        prvHandlePutBegin(sessionId, payload, payloadLength);
    }
}

static void prvDropFramePrefix(unsigned short count)
{
    if (count >= usFrameLength)
    {
        usFrameLength = 0U;
        return;
    }

    usFrameLength = (unsigned short)(usFrameLength - count);
    memmove(ucFrameBuffer, ucFrameBuffer + count, usFrameLength);
}

static short prvFindMagic(void)
{
    unsigned short index;

    for (index = 0U; index <= (unsigned short)(usFrameLength - 4U); index++)
    {
        if ((ucFrameBuffer[index] == 'M') &&
            (ucFrameBuffer[index + 1U] == 'S') &&
            (ucFrameBuffer[index + 2U] == 'A') &&
            (ucFrameBuffer[index + 3U] == 'G'))
        {
            return (short)index;
        }
    }

    return -1;
}

static void prvParseAvailableFrames(void)
{
    while (usFrameLength >= 4U)
    {
        short magicOffset = prvFindMagic();
        unsigned short payloadLength;
        unsigned short frameLength;
        unsigned short expectedCrc;

        if (magicOffset < 0)
        {
            if (usFrameLength > 3U)
            {
                prvDropFramePrefix((unsigned short)(usFrameLength - 3U));
            }
            return;
        }
        if (magicOffset > 0)
        {
            prvDropFramePrefix((unsigned short)magicOffset);
        }
        if (usFrameLength < DOS_AGENT_HEADER_SIZE)
        {
            return;
        }

        payloadLength = prvReadUInt16(ucFrameBuffer + 24U);
        if ((ucFrameBuffer[4] != DOS_AGENT_PROTOCOL_VERSION) ||
            (ucFrameBuffer[6] != 0U) ||
            (ucFrameBuffer[7] != 0U) ||
            (payloadLength > DOS_AGENT_MAX_PAYLOAD_SIZE))
        {
            prvDropFramePrefix(1U);
            continue;
        }
        frameLength = (unsigned short)(
            DOS_AGENT_HEADER_SIZE + payloadLength + DOS_AGENT_CRC_SIZE);
        if (usFrameLength < frameLength)
        {
            return;
        }

        expectedCrc = prvReadUInt16(ucFrameBuffer + frameLength - DOS_AGENT_CRC_SIZE);
        if (expectedCrc != prvCrc16(
                ucFrameBuffer,
                (unsigned short)(frameLength - DOS_AGENT_CRC_SIZE)))
        {
#if defined(FREERTOSX86_SMOKE)
            ulSmokeInvalidCrcFrames++;
#endif
            prvDropFramePrefix(1U);
            continue;
        }

#if defined(FREERTOSX86_SMOKE)
        ulSmokeValidFrames++;
#endif
        prvHandleFrame(ucFrameBuffer, payloadLength);
        prvDropFramePrefix(frameLength);
    }
}

static void prvProcessByte(unsigned char value)
{
#if defined(FREERTOSX86_SMOKE)
    ulSmokeReceivedBytes++;
#endif
    if (usFrameLength >= DOS_AGENT_FRAME_BUFFER_SIZE)
    {
        prvDropFramePrefix(1U);
    }
    ucFrameBuffer[usFrameLength++] = value;
    prvParseAvailableFrames();
}

static void prvLoadPendingDebugMessage(void)
{
    unsigned char nextTail;

    if (ucPendingDebugActive != 0U)
    {
        return;
    }

    portENTER_CRITICAL();
    if (ucDebugQueueHead != ucDebugQueueTail)
    {
        xPendingDebug = xDebugQueue[ucDebugQueueTail];
        nextTail = (unsigned char)((ucDebugQueueTail + 1U) & DOS_AGENT_DEBUG_QUEUE_MASK);
        ucDebugQueueTail = nextTail;
        xPendingDebug.sequence = ulNextDebugSequence++;
        ucPendingDebugActive = 1U;
        ucPendingDebugSent = 0U;
    }
    portEXIT_CRITICAL();
}

static void prvSendPendingDebugMessage(void)
{
    unsigned char payload[DOS_AGENT_MAX_TX_PAYLOAD_SIZE];
    TickType_t now;
    unsigned short payloadLength;

    if ((ucSessionActive == 0U) || (ucPendingDebugActive == 0U))
    {
        return;
    }

    now = xTaskGetTickCount();
    if ((ucPendingDebugSent != 0U) &&
        ((TickType_t)(now - xLastDebugSendTick) < DOS_AGENT_DEBUG_RETRY_TICKS))
    {
        return;
    }

    prvWriteUInt32(payload, xPendingDebug.sequence);
    payload[4] = xPendingDebug.severity;
    prvWriteUInt16(payload + 5U, xPendingDebug.length);
    memcpy(payload + 7U, xPendingDebug.text, xPendingDebug.length);
    payloadLength = (unsigned short)(7U + xPendingDebug.length);
    if (prvSendFrame(0x30U, ucSessionId, payload, payloadLength))
    {
        ucPendingDebugSent = 1U;
        xLastDebugSendTick = now;
    }
}

unsigned short dos_agent_initialize(void)
{
    if (!prvLoadConfiguration(&usUartBase, &ucUartIrq))
    {
        return DOS_AGENT_ERROR;
    }

    usReceiveHead = 0U;
    usReceiveTail = 0U;
    usFrameLength = 0U;
    ucSessionActive = 0U;
    ucDebugQueueHead = 0U;
    ucDebugQueueTail = 0U;
    ucPendingDebugActive = 0U;
    ucPendingDebugSent = 0U;
    ucTransferActive = 0U;
    ucXmodemStarted = 0U;
    ucXmodemPacketActive = 0U;
    ucTransferHasBlock = 0U;
    usXmodemPacketLength = 0U;
    pxTransferFile = NULL;
#if defined(FREERTOSX86_SMOKE)
    ulSmokeReceivedBytes = 0UL;
    ulSmokeValidFrames = 0UL;
    ulSmokeInvalidCrcFrames = 0UL;
    ulSmokeTaskRuns = 0UL;
    ulSmokeInterruptBytes = 0UL;
    ulSmokePolledBytes = 0UL;
#endif
    if (!prvInitializeUart())
    {
        return DOS_AGENT_ERROR;
    }
    ucAgentInitialized = 1U;
    return DOS_AGENT_OK;
}

#if defined(FREERTOSX86_SMOKE)
void dos_agent_get_smoke_diagnostics(
    unsigned long * receivedBytes,
    unsigned long * validFrames,
    unsigned long * invalidCrcFrames,
    unsigned long * taskRuns,
    unsigned long * interruptBytes,
    unsigned long * polledBytes)
{
    *receivedBytes = ulSmokeReceivedBytes;
    *validFrames = ulSmokeValidFrames;
    *invalidCrcFrames = ulSmokeInvalidCrcFrames;
    *taskRuns = ulSmokeTaskRuns;
    *interruptBytes = ulSmokeInterruptBytes;
    *polledBytes = ulSmokePolledBytes;
}
#endif

void dos_agent_task(void * parameters)
{
    unsigned char value;

    (void)parameters;
    for (;;)
    {
#if defined(FREERTOSX86_SMOKE)
        ulSmokeTaskRuns++;
#endif
        while ((ucTransferActive == 0U) &&
               (prvReadUartByte(&value) != 0U))
        {
            prvProcessByte(value);
        }
        if (ucTransferActive != 0U)
        {
            prvServiceTransfer();
        }
        else
        {
            prvLoadPendingDebugMessage();
            prvSendPendingDebugMessage();
        }
        vTaskDelay((TickType_t)1U);
    }
}

void dos_agent_shutdown(void)
{
    if (pxTransferFile != NULL)
    {
        (void)fclose(pxTransferFile);
        pxTransferFile = NULL;
        (void)remove(DOS_AGENT_TRANSFER_TEMP_NAME);
    }
    if (ucAgentInitialized != 0U)
    {
        ucSessionActive = 0U;
        ucAgentInitialized = 0U;
        prvRestoreUart();
    }
}

unsigned short dos_agent_log(
    unsigned char severity,
    const char * message)
{
    unsigned char nextHead;
    unsigned short index;
    unsigned short length = 0U;
    DosAgentDebugMessage * destination;

    if ((ucAgentInitialized == 0U) || (message == NULL))
    {
        return DOS_AGENT_ERROR;
    }

    portENTER_CRITICAL();
    nextHead = (unsigned char)((ucDebugQueueHead + 1U) & DOS_AGENT_DEBUG_QUEUE_MASK);
    if (nextHead == ucDebugQueueTail)
    {
        portEXIT_CRITICAL();
        return DOS_AGENT_QUEUE_FULL;
    }

    destination = &xDebugQueue[ucDebugQueueHead];
    destination->severity = severity;
    for (index = 0U; (index < DOS_AGENT_DEBUG_MESSAGE_SIZE) && (message[index] != '\0'); index++)
    {
        unsigned char character = (unsigned char)message[index];
        if ((character != '\t') && ((character < 0x20U) || (character > 0x7EU)))
        {
            character = '?';
        }
        destination->text[index] = (char)character;
        length++;
    }
    destination->length = length;
    ucDebugQueueHead = nextHead;
    portEXIT_CRITICAL();
    return DOS_AGENT_OK;
}
