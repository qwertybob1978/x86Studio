#include "Dialogs.r"

resource 'DLOG' (128) {
    { 50, 80, 150, 360 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    128,
    "",
    centerMainScreen
};

resource 'DITL' (128) {
    {
        { 80, 90, 100, 160 },
        Button { enabled, "OK" };

        { 20, 20, 40, 260 },
        StaticText { enabled, "Hello, Mac!" };
    }
};

#include "Processes.r"

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
    notHighLevelEventAware,
    onlyLocalHLEvents,
    notStationeryAware,
    dontUseTextEditServices,
    reserved,
    reserved,
    reserved,
    100 * 1024,
    100 * 1024
};
