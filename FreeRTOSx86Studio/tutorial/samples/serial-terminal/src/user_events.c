#include <string.h>
#include "designer_form.h"

static unsigned char gReceivedText[256] = { 0 };

static void SetTerminalText(TerminalForm form, const char *controlId, const char *value)
{
    unsigned char pascalText[256];
    size_t length = strlen(value);

    if (length > 255)
        length = 255;
    pascalText[0] = (unsigned char)length;
    memcpy(pascalText + 1, value, length);
    MacStudioSetControlText(form, controlId, pascalText);
}

static void AppendReceivedByte(unsigned char value)
{
    unsigned char length = gReceivedText[0];

    if (length == 255)
    {
        memmove(gReceivedText + 1, gReceivedText + 2, 254);
        length = 254;
        gReceivedText[0] = length;
    }
    gReceivedText[++gReceivedText[0]] = value;
}

void TerminalSendClick(TerminalForm form, short item)
{
    unsigned char input[256];
    unsigned char output[256];
    short result;
    short index;
    long length;

    (void)item;
    result = MacStudioGetControlText(form, "SendText", input);
    if (result != noErr || input[0] == 0)
        return;

    length = input[0];
    for (index = 0; index < input[0]; index++)
        output[index] = input[index + 1];
    if (output[length - 1] != '\r' && length < 255)
        output[length++] = '\r';

    result = MacStudioSerialWrite("SerialPort1", output, length);
    if (result == noErr)
    {
        static const unsigned char emptyText[] = { 0 };
        MacStudioSetControlText(form, "SendText", emptyText);
        SetTerminalText(form, "ConnectionStatus", "Line sent.");
    }
    else
    {
        SetTerminalText(form, "ConnectionStatus", "Send failed - check serial connection.");
    }
}

void TerminalClearClick(TerminalForm form, short item)
{
    static const unsigned char emptyText[] = { 0 };

    (void)item;
    gReceivedText[0] = 0;
    MacStudioSetControlText(form, "ReceivedText", emptyText);
    SetTerminalText(form, "ConnectionStatus", "Receive display cleared.");
}

void SerialDataAvailable(TerminalForm form, short item)
{
    unsigned char data[128];
    long readCount = 0;
    long index;
    short result;

    (void)item;
    result = MacStudioSerialRead("SerialPort1", data, sizeof(data), &readCount);
    if (result != noErr || readCount <= 0)
        return;

    for (index = 0; index < readCount; index++)
    {
        unsigned char value = data[index];

        if (value == '\r' || value == '\n')
            AppendReceivedByte('\r');
        else if (value == '\b')
        {
            if (gReceivedText[0] > 0)
                gReceivedText[0]--;
        }
        else if (value >= 0x20 && value <= 0x7e)
            AppendReceivedByte(value);
    }

    MacStudioSetControlText(form, "ReceivedText", gReceivedText);
    SetTerminalText(form, "ConnectionStatus", "Receiving serial data.");
}

void SerialError(TerminalForm form, short item)
{
    (void)item;
    SetTerminalText(form, "ConnectionStatus", "Serial I/O error - check endpoint and cable.");
}

void MacStudioUserControlClicked(
    TerminalForm form,
    const char *controlId,
    short item)
{
    (void)form;
    (void)controlId;
    (void)item;
}
