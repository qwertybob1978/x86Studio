#include "Processes.r"

resource 'DLOG' (128) {
    { 60, 60, 150, 360 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    128,
    "Agent IPC Probe",
    centerMainScreen
};

resource 'DITL' (128) {
    {
        { 56, 120, 76, 180 },
        Button { enabled, "OK" };

        { 20, 20, 40, 280 },
        StaticText { enabled, "Debug record queued to Agent." };
    }
};

resource 'DLOG' (129) {
    { 60, 60, 150, 360 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    129,
    "Agent IPC Probe",
    centerMainScreen
};

resource 'DITL' (129) {
    {
        { 56, 120, 76, 180 },
        Button { enabled, "OK" };

        { 20, 20, 40, 280 },
        StaticText { enabled, "Could not queue a record to Agent." };
    }
};

resource 'SIZE' (-1) {
    reserved,
    acceptSuspendResumeEvents,
    reserved,
    canBackground,
    doesActivateOnFGSwitch,
    backgroundAndForeground,
    dontGetFrontClicks,
    ignoreChildDiedEvents,
    is32BitCompatible,
    isHighLevelEventAware,
    onlyLocalHLEvents,
    notStationeryAware,
    dontUseTextEditServices,
    reserved,
    reserved,
    reserved,
    64 * 1024,
    64 * 1024
};
