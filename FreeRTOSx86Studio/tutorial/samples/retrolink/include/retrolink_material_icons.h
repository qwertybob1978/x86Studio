#ifndef RETROLINK_MATERIAL_ICONS_H
#define RETROLINK_MATERIAL_ICONS_H

#include "wire.h"

#define RL_ICON_WIDTH 18
#define RL_ICON_HEIGHT 18

typedef enum
{
    RL_ICON_CONNECT,
    RL_ICON_DISCONNECT,
    RL_ICON_TAKE_CONTROL,
    RL_ICON_UPLOAD,
    RL_ICON_DOWNLOAD,
    RL_ICON_OPEN,
    RL_ICON_UP,
    RL_ICON_MARK,
    RL_ICON_REFRESH,
    RL_ICON_COUNT
} RlIconId;

typedef struct
{
    const rl_u8 *data;
    rl_u16 length;
} RlIconRle;

static const rl_u8 gIconConnect[] =
{
    40, 0, 7, 1, 11, 0, 8, 1, 10, 0, 2, 1, 4, 0, 2, 1,
    10, 0, 2, 1, 4, 0, 2, 1, 10, 0, 8, 1, 10, 0, 7, 1,
    10, 0, 9, 1, 9, 0, 9, 1, 28, 0, 2, 1, 6, 0, 6, 1,
    4, 0, 2, 1, 5, 0, 7, 1, 4, 0, 2, 1, 5, 0, 2, 1,
    4, 0, 1, 1, 4, 0, 6, 1, 1, 0, 2, 1, 4, 0, 1, 1,
    4, 0, 6, 1, 1, 0, 7, 1, 5, 0, 5, 1, 1, 0, 7, 1,
    10, 0, 8, 1,
};

static const rl_u8 gIconDisconnect[] =
{
    40, 0, 7, 1, 11, 0, 8, 1, 10, 0, 2, 1, 4, 0, 2, 1,
    10, 0, 2, 1, 4, 0, 2, 1, 10, 0, 8, 1, 10, 0, 7, 1,
    10, 0, 9, 1, 9, 0, 9, 1, 29, 0, 1, 1, 2, 0, 1, 1,
    3, 0, 6, 1, 4, 0, 6, 1, 1, 0, 7, 1, 5, 0, 4, 1,
    2, 0, 2, 1, 4, 0, 1, 1, 5, 0, 4, 1, 2, 0, 2, 1,
    4, 0, 1, 1, 4, 0, 6, 1, 1, 0, 7, 1, 5, 0, 1, 1,
    2, 0, 1, 1, 3, 0, 6, 1, 10, 0, 8, 1,
};

static const rl_u8 gIconTakeControl[] =
{
    43, 0, 3, 1, 3, 0, 3, 1, 8, 0, 5, 1, 1, 0, 5, 1,
    7, 0, 5, 1, 1, 0, 5, 1, 7, 0, 4, 1, 2, 0, 4, 1,
    10, 0, 1, 1, 5, 0, 1, 1, 27, 0, 5, 1, 2, 0, 4, 1,
    5, 0, 8, 1, 2, 0, 4, 1, 4, 0, 9, 1, 1, 0, 4, 1,
    4, 0, 9, 1, 1, 0, 4, 1, 4, 0, 9, 1, 1, 0, 4, 1,
    26, 0, 1, 1, 4, 0, 1, 1, 11, 0, 8, 1, 10, 0, 9, 1,
    10, 0, 1, 1, 4, 0, 2, 1, 3, 0,
};

static const rl_u8 gIconUpload[] =
{
    80, 0, 3, 1, 14, 0, 5, 1, 12, 0, 7, 1, 10, 0, 9, 1,
    8, 0, 10, 1, 11, 0, 5, 1, 13, 0, 5, 1, 13, 0, 5, 1,
    13, 0, 5, 1, 46, 0, 11, 1, 39, 0,
};

static const rl_u8 gIconDownload[] =
{
    61, 0, 5, 1, 13, 0, 5, 1, 13, 0, 5, 1, 13, 0, 5, 1,
    13, 0, 5, 1, 10, 0, 11, 1, 8, 0, 9, 1, 10, 0, 7, 1,
    12, 0, 5, 1, 14, 0, 2, 1, 48, 0, 11, 1, 39, 0,
};

static const rl_u8 gIconOpen[] =
{
    75, 0, 7, 1, 11, 0, 13, 1, 5, 0, 14, 1, 4, 0, 14, 1,
    4, 0, 2, 1, 1, 0, 12, 1, 3, 0, 2, 1, 1, 0, 12, 1,
    3, 0, 15, 1, 3, 0, 15, 1, 3, 0, 15, 1, 3, 0, 15, 1,
    3, 0, 15, 1, 3, 0, 14, 1, 5, 0, 12, 1, 20, 0,
};

static const rl_u8 gIconUp[] =
{
    82, 0, 1, 1, 16, 0, 3, 1, 14, 0, 5, 1, 12, 0, 7, 1,
    10, 0, 5, 1, 1, 0, 3, 1, 8, 0, 3, 1, 1, 0, 2, 1,
    2, 0, 3, 1, 6, 0, 3, 1, 2, 0, 2, 1, 3, 0, 2, 1,
    7, 0, 1, 1, 3, 0, 2, 1, 4, 0, 1, 1, 11, 0, 2, 1,
    16, 0, 2, 1, 16, 0, 2, 1, 16, 0, 2, 1, 17, 0, 1, 1,
    25, 0,
};

static const rl_u8 gIconMark[] =
{
    51, 0, 1, 1, 16, 0, 3, 1, 14, 0, 5, 1, 12, 0, 5, 1,
    12, 0, 5, 1, 12, 0, 5, 1, 6, 0, 1, 1, 5, 0, 5, 1,
    6, 0, 3, 1, 3, 0, 5, 1, 6, 0, 5, 1, 1, 0, 5, 1,
    8, 0, 9, 1, 10, 0, 7, 1, 12, 0, 5, 1, 14, 0, 3, 1,
    16, 0, 1, 1, 47, 0,
};

static const rl_u8 gIconRefresh[] =
{
    80, 0, 5, 1, 3, 0, 1, 1, 7, 0, 11, 1, 7, 0, 3, 1,
    3, 0, 5, 1, 6, 0, 2, 1, 6, 0, 4, 1, 5, 0, 3, 1,
    5, 0, 5, 1, 5, 0, 2, 1, 5, 0, 6, 1, 5, 0, 2, 1,
    16, 0, 3, 1, 8, 0, 1, 1, 7, 0, 2, 1, 7, 0, 2, 1,
    7, 0, 3, 1, 5, 0, 3, 1, 8, 0, 9, 1, 10, 0, 7, 1,
    14, 0, 2, 1, 24, 0,
};

static const RlIconRle gRetrolinkIcons[RL_ICON_COUNT] =
{
    { gIconConnect, sizeof(gIconConnect) },
    { gIconDisconnect, sizeof(gIconDisconnect) },
    { gIconTakeControl, sizeof(gIconTakeControl) },
    { gIconUpload, sizeof(gIconUpload) },
    { gIconDownload, sizeof(gIconDownload) },
    { gIconOpen, sizeof(gIconOpen) },
    { gIconUp, sizeof(gIconUp) },
    { gIconMark, sizeof(gIconMark) },
    { gIconRefresh, sizeof(gIconRefresh) },
};

#endif
