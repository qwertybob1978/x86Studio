#include "Processes.r"

resource 'vers' (1) {
    0x00, 0x01, 0x80, 0x00,
    0,
    "0.1",
    "MacStudio Agent 0.1"
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
    96 * 1024,
    96 * 1024
};

resource 'DLOG' (128) {
    { 60, 40, 150, 460 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    128,
    "MacStudio Agent 0.1",
    centerMainScreen
};

resource 'DITL' (128) {
    {
        { 56, 170, 76, 250 },
        Button { enabled, "OK" };

        { 20, 20, 40, 400 },
        StaticText { enabled, "A package transfer is active. Try again when it finishes." };
    }
};

resource 'DLOG' (129) {
    { 60, 40, 150, 460 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    129,
    "MacStudio Agent 0.1",
    centerMainScreen
};

resource 'DITL' (129) {
    {
        { 56, 170, 76, 250 },
        Button { enabled, "OK" };

        { 20, 20, 40, 400 },
        StaticText { enabled, "Could not open that rate. The previous rate was restored." };
    }
};

resource 'DLOG' (130) {
    { 60, 40, 150, 460 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    130,
    "MacStudio Agent 0.1",
    centerMainScreen
};

resource 'DITL' (130) {
    {
        { 56, 170, 76, 250 },
        Button { enabled, "OK" };

        { 20, 20, 40, 400 },
        StaticText { enabled, "The serial port could not be reopened. The Agent is stopping." };
    }
};

resource 'DLOG' (131) {
    { 60, 40, 150, 460 },
    dBoxProc,
    visible,
    noGoAway,
    0,
    131,
    "MacStudio Agent 0.1",
    centerMainScreen
};

resource 'DITL' (131) {
    {
        { 56, 170, 76, 250 },
        Button { enabled, "OK" };

        { 20, 20, 40, 400 },
        StaticText { enabled, "Rate changed, but the setting could not be saved." };
    }
};
