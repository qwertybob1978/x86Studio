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
#define AGENT_HFS_ROOT_DIRECTORY_ID 2L
#define AGENT_DEBUG_EVENT_CLASS 'MSAG'
#define AGENT_LAUNCH_EVENT_ID 'LNCH'

static const unsigned char gAgentName[] = {
    AGENT_NAME_LENGTH,
    'M', 'a', 'c', 'S', 't', 'u', 'd', 'i', 'o', 'A', 'g', 'e', 'n', 't'
};

static const unsigned char gTargetName[] = {
    17,
    'A', 'g', 'e', 'n', 't', 'L', 'a', 'u', 'n', 'c', 'h', 'T', 'a', 'r',
    'g', 'e', 't'
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

static OSErr AgentFindTarget(FSSpec *targetSpec)
{
    ProcessSerialNumber currentProcess;
    ProcessInfoRec processInfo;
    FSSpec probeSpec;
    OSErr error;

    if (targetSpec == NULL)
        return paramErr;

    error = GetCurrentProcess(&currentProcess);
    if (error != noErr)
        return error;

    memset(&processInfo, 0, sizeof(processInfo));
    memset(&probeSpec, 0, sizeof(probeSpec));
    processInfo.processInfoLength = sizeof(processInfo);
    processInfo.processAppSpec = &probeSpec;
    error = GetProcessInformation(&currentProcess, &processInfo);
    if (error != noErr)
        return error;
    if (probeSpec.vRefNum == 0)
        return fnfErr;

    return FSMakeFSSpec(
        probeSpec.vRefNum,
        AGENT_HFS_ROOT_DIRECTORY_ID,
        (ConstStringPtr)gTargetName,
        targetSpec);
}

static OSErr AgentRequestLaunch(const FSSpec *targetSpec)
{
    ProcessSerialNumber agentProcess;
    AEAddressDesc target;
    AppleEvent event;
    AppleEvent reply;
    OSErr error;
    Boolean targetCreated = false;
    Boolean eventCreated = false;
    Boolean replyCreated = false;

    if (targetSpec == NULL)
        return paramErr;

    error = AgentFindProcess(&agentProcess);
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
        AGENT_DEBUG_EVENT_CLASS,
        AGENT_LAUNCH_EVENT_ID,
        &target,
        kAutoGenerateReturnID,
        kAnyTransactionID,
        &event);
    if (error != noErr)
        goto cleanup;
    eventCreated = true;

    error = AEPutParamPtr(
        &event,
        keyDirectObject,
        typeFSS,
        targetSpec,
        sizeof(*targetSpec));
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
        AEDisposeDesc((AEDesc *)&reply);
    if (eventCreated)
        AEDisposeDesc((AEDesc *)&event);
    if (targetCreated)
        AEDisposeDesc((AEDesc *)&target);
    return error;
}

int main(void)
{
    FSSpec targetSpec;
    OSErr error;
    short item;
    DialogPtr dialog;

    error = AgentFindTarget(&targetSpec);
    if (error == noErr)
        error = AgentRequestLaunch(&targetSpec);
    if (error == noErr)
        return 0;

    InitGraf(&qd.thePort);
    InitFonts();
    InitWindows();
    InitMenus();
    TEInit();
    InitDialogs(NULL);
    InitCursor();

    dialog = GetNewDialog(128, NULL, (WindowPtr)-1);
    if (dialog == NULL)
        return 1;
    ModalDialog(NULL, &item);
    DisposeDialog(dialog);
    return 1;
}
