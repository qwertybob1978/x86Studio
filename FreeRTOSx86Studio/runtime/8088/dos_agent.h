#ifndef FREERTOSX86_DOS_AGENT_H
#define FREERTOSX86_DOS_AGENT_H

#define DOS_AGENT_OK          0U
#define DOS_AGENT_QUEUE_FULL  1U
#define DOS_AGENT_ERROR       2U

unsigned short dos_agent_initialize(void);
void dos_agent_task(void * parameters);
void dos_agent_shutdown(void);
unsigned short dos_agent_log(unsigned char severity, const char * message);

#if defined(FREERTOSX86_SMOKE)
void dos_agent_get_smoke_diagnostics(
    unsigned long * receivedBytes,
    unsigned long * validFrames,
    unsigned long * invalidCrcFrames,
    unsigned long * taskRuns,
    unsigned long * interruptBytes,
    unsigned long * polledBytes);
#endif

#endif
