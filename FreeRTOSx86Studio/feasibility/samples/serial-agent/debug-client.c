#include <string.h>

#include "AppleEvents.h"
#include "Dialogs.h"
#include "Fonts.h"
#include "Menus.h"
#include "Processes.h"
#include "Quickdraw.h"
#include "TextEdit.h"
#include "Windows.h"

#define AGENT_NAME_LENGTH 14
#define AGENT_APPLE_EVENT_PREFIX_SIZE 3
#define AGENT_MAX_DEBUG_RECORD_SIZE 512
#define AGENT_DEBUG_EVENT_CLASS 'MSAG'
#define AGENT_DEBUG_EVENT_ID 'DBUG'

static short gErrorStage;

static const unsigned char gAgentName[] = {
    AGENT_NAME_LENGTH,
    'M', 'a', 'c', 'S', 't', 'u', 'd', 'i', 'o', 'A', 'g', 'e', 'n', 't'
};

static OSErr AgentFindProcess(ProcessSerialNumber *agentProcess)
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

        if (processName[0] == AGENT_NAME_LENGTH &&
            memcmp(processName, gAgentName, sizeof(gAgentName)) == 0)
        {
            *agentProcess = currentProcess;
            return noErr;
        }
    }
}

static OSErr AgentSendDebugMessage(const unsigned char *message, unsigned short length)
{
    unsigned char request[AGENT_APPLE_EVENT_PREFIX_SIZE + AGENT_MAX_DEBUG_RECORD_SIZE];
    ProcessSerialNumber agentProcess;
    AEAddressDesc target;
    AppleEvent event;
    AppleEvent reply;
    OSErr error;
    Boolean targetCreated = false;
    Boolean eventCreated = false;
    Boolean replyCreated = false;

    if ((message == NULL && length != 0) || length > AGENT_MAX_DEBUG_RECORD_SIZE)
        return paramErr;

    gErrorStage = 1;
    error = AgentFindProcess(&agentProcess);
    if (error != noErr)
        return error;

    memset(&target, 0, sizeof(target));
    memset(&event, 0, sizeof(event));
    memset(&reply, 0, sizeof(reply));
    gErrorStage = 2;
    error = AECreateDesc(
        typeProcessSerialNumber,
        &agentProcess,
        sizeof(agentProcess),
        &target);
    if (error != noErr)
        goto cleanup;
    targetCreated = true;

    gErrorStage = 3;
    error = AECreateAppleEvent(
        AGENT_DEBUG_EVENT_CLASS,
        AGENT_DEBUG_EVENT_ID,
        &target,
        kAutoGenerateReturnID,
        kAnyTransactionID,
        &event);
    if (error != noErr)
        goto cleanup;
    eventCreated = true;

    gErrorStage = 4;
    request[0] = 1;
    request[1] = (unsigned char)(length >> 8);
    request[2] = (unsigned char)length;
    if (length != 0)
        memcpy(request + AGENT_APPLE_EVENT_PREFIX_SIZE, message, length);
    error = AEPutParamPtr(
        &event,
        keyDirectObject,
        typeChar,
        request,
        AGENT_APPLE_EVENT_PREFIX_SIZE + length);
    if (error != noErr)
        goto cleanup;

    gErrorStage = 5;
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
    else
        gErrorStage = 6;

cleanup:
    if (replyCreated)
        AEDisposeDesc((AEDesc *)&reply);
    if (eventCreated)
        AEDisposeDesc((AEDesc *)&event);
    if (targetCreated)
        AEDisposeDesc((AEDesc *)&target);
    return error;
}

static void AgentSetFailureText(DialogPtr dialog, OSErr error)
{
    static const unsigned char prefix[] = "Agent IPC stage ";
    static const unsigned char separator[] = ", OSErr ";
    unsigned char stageText[16];
    unsigned char errorText[16];
    unsigned char message[64];
    short itemType;
    Handle itemHandle;
    Rect itemBounds;
    unsigned short length = 0;

    NumToString(gErrorStage, (StringPtr)stageText);
    NumToString(error, (StringPtr)errorText);
    memcpy(message + 1, prefix, sizeof(prefix) - 1);
    length = (unsigned short)(sizeof(prefix) - 1);
    memcpy(message + 1 + length, stageText + 1, stageText[0]);
    length = (unsigned short)(length + stageText[0]);
    memcpy(message + 1 + length, separator, sizeof(separator) - 1);
    length = (unsigned short)(length + sizeof(separator) - 1);
    memcpy(message + 1 + length, errorText + 1, errorText[0]);
    length = (unsigned short)(length + errorText[0]);
    message[0] = (unsigned char)length;

    GetDialogItem(dialog, 2, &itemType, &itemHandle, &itemBounds);
    SetDialogItemText(itemHandle, (StringPtr)message);
}

int main(void)
{
    static const unsigned char message[] = "AppleEvent IPC probe";
    OSErr error = AgentSendDebugMessage(message, sizeof(message) - 1);
    short item;
    DialogPtr dialog;

    InitGraf(&qd.thePort);
    InitFonts();
    InitWindows();
    InitMenus();
    TEInit();
    InitDialogs(NULL);
    InitCursor();

    dialog = GetNewDialog(error == noErr ? 128 : 129, NULL, (WindowPtr)-1);
    if (dialog == NULL)
        return 1;
    if (error != noErr)
        AgentSetFailureText(dialog, error);
    ModalDialog(NULL, &item);

    return error == noErr ? 0 : 1;
}
