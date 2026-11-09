#include <Files.h>
#include <Processes.h>
#include <string.h>
#include "designer_form.h"

static void SetStatus(NotepadForm form, const char *message)
{
    unsigned char text[256];
    size_t length = strlen(message);

    if (length > 255)
        length = 255;
    text[0] = (unsigned char)length;
    memcpy(text + 1, message, length);
    MacStudioSetControlText(form, "StatusText", text);
}

static OSErr FindNoteFile(FSSpec *noteFile)
{
    ProcessSerialNumber currentProcess;
    ProcessInfoRec processInfo;
    FSSpec applicationFile;
    unsigned char processName[64];
    unsigned char fileName[] = { 8, 'N', 'o', 't', 'e', '.', 't', 'x', 't' };
    OSErr error;

    memset(&currentProcess, 0, sizeof(currentProcess));
    memset(&processInfo, 0, sizeof(processInfo));
    memset(&applicationFile, 0, sizeof(applicationFile));
    memset(processName, 0, sizeof(processName));
    processInfo.processInfoLength = sizeof(processInfo);
    processInfo.processName = (StringPtr)processName;
    processInfo.processAppSpec = &applicationFile;

    error = GetCurrentProcess(&currentProcess);
    if (error != noErr)
        return error;
    error = GetProcessInformation(&currentProcess, &processInfo);
    if (error != noErr)
        return error;

    return FSMakeFSSpec(
        applicationFile.vRefNum,
        applicationFile.parID,
        (ConstStringPtr)fileName,
        noteFile);
}

static OSErr SaveNote(const unsigned char *text)
{
    FSSpec noteFile;
    short fileRef = 0;
    long length = text[0];
    OSErr error;
    OSErr closeError;

    error = FindNoteFile(&noteFile);
    if (error == fnfErr)
        error = FSpCreate(&noteFile, 'MIDE', 'TEXT', smSystemScript);
    if (error != noErr && error != dupFNErr)
        return error;

    error = FSpOpenDF(&noteFile, fsWrPerm, &fileRef);
    if (error != noErr)
        return error;

    error = SetEOF(fileRef, 0);
    if (error == noErr && length > 0)
    {
        long written = length;
        error = FSWrite(fileRef, &written, (Ptr)(text + 1));
        if (error == noErr && written != length)
            error = dskFulErr;
    }

    closeError = FSClose(fileRef);
    if (error == noErr)
        error = closeError;
    return error;
}

static OSErr LoadNote(unsigned char *text, Boolean *wasTruncated)
{
    FSSpec noteFile;
    short fileRef = 0;
    long fileLength = 0;
    long readLength;
    OSErr error;
    OSErr closeError;

    *wasTruncated = false;
    error = FindNoteFile(&noteFile);
    if (error != noErr)
        return error;

    error = FSpOpenDF(&noteFile, fsRdPerm, &fileRef);
    if (error != noErr)
        return error;
    error = GetEOF(fileRef, &fileLength);
    if (error == noErr)
    {
        readLength = fileLength > 255 ? 255 : fileLength;
        *wasTruncated = fileLength > 255;
        if (readLength > 0)
        {
            long actualLength = readLength;
            error = FSRead(fileRef, &actualLength, (Ptr)(text + 1));
            if (error == noErr && actualLength != readLength)
                error = eofErr;
            readLength = actualLength;
        }
        text[0] = (unsigned char)readLength;
    }

    closeError = FSClose(fileRef);
    if (error == noErr)
        error = closeError;
    return error;
}

void NotepadSaveClick(NotepadForm form, short item)
{
    unsigned char text[256];
    OSErr error;

    (void)item;
    error = MacStudioGetControlText(form, "NoteText", text);
    if (error == noErr)
        error = SaveNote(text);
    SetStatus(
        form,
        error == noErr
            ? "Saved Note.txt beside the application."
            : "Save failed; check the disk and note length.");
}

void NotepadLoadClick(NotepadForm form, short item)
{
    unsigned char text[256];
    Boolean wasTruncated;
    OSErr error;

    (void)item;
    error = LoadNote(text, &wasTruncated);
    if (error == noErr)
        error = MacStudioSetControlText(form, "NoteText", text);
    if (error == fnfErr)
        SetStatus(form, "No Note.txt has been saved yet.");
    else if (error != noErr)
        SetStatus(form, "Load failed; check the disk.");
    else if (wasTruncated)
        SetStatus(form, "Loaded the first 255 bytes of Note.txt.");
    else
        SetStatus(form, "Loaded Note.txt.");
}

void NotepadClearClick(NotepadForm form, short item)
{
    static const unsigned char emptyText[] = { 0 };

    (void)item;
    MacStudioSetControlText(form, "NoteText", emptyText);
    SetStatus(form, "Text cleared; the saved note is unchanged.");
}

void MacStudioUserControlClicked(
    NotepadForm form,
    const char *controlId,
    short item)
{
    (void)form;
    (void)controlId;
    (void)item;
}
