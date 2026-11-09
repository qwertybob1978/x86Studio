#include <string.h>
#include <stdio.h>
#include "designer_form.h"

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

int main(void)
{
    return MacStudioRunForm();
}

void MacStudioFormDidLoad(TerminalForm form)
{
    short result = MacStudioSerialOpen("SerialPort1");

    if (result == noErr)
        SetTerminalText(form, "ConnectionStatus", "Modem ready - waiting for input.");
    else
    {
        char status[64];
        sprintf(status, "Open failed (OSErr %d).", result);
        SetTerminalText(form, "ConnectionStatus", status);
    }
}
