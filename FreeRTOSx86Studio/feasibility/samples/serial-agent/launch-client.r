#include "Processes.r"

resource 'DLOG' (128) {
    { 60, 60, 150, 360 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    128,
    "Agent Launch Probe",
    centerMainScreen
};

resource 'DITL' (128) {
    {
        { 56, 120, 76, 180 },
        Button { enabled, "OK" };

        { 20, 20, 40, 280 },
        StaticText { enabled, "Agent could not launch target." };
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
