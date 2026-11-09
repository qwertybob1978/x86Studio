#include <string.h>
#include "designer_form.h"

static short gHasKey = 0;
static short gChestOpen = 0;

static void SetText(AdventureForm form, const char *controlId, const char *value)
{
    unsigned char text[256];
    size_t length = strlen(value);

    if (length > 255)
        length = 255;
    text[0] = (unsigned char)length;
    memcpy(text + 1, value, length);
    MacStudioSetControlText(form, controlId, text);
}

static void ResetGame(AdventureForm form)
{
    gHasKey = 0;
    gChestOpen = 0;
    SetText(form, "SceneText", "A brass key lies beside a locked treasure chest.");
    SetText(form, "InventoryText", "Inventory: empty");
    SetText(form, "StatusText", "Take the key, then open the chest.");
}

void AdventureLookClick(AdventureForm form, short item)
{
    (void)item;
    if (gChestOpen)
        SetText(form, "SceneText", "The chest is open; the treasure is yours.");
    else if (gHasKey)
        SetText(form, "SceneText", "You hold the key. The chest is still locked.");
    else
        SetText(form, "SceneText", "A brass key lies beside a locked treasure chest.");
    SetText(form, "StatusText", "Look describes the cave without changing your inventory.");
}

void AdventureTakeKeyClick(AdventureForm form, short item)
{
    (void)item;
    if (gChestOpen)
    {
        SetText(form, "StatusText", "The adventure is complete. Start over to play again.");
        return;
    }
    if (gHasKey)
    {
        SetText(form, "StatusText", "You already have the brass key.");
        return;
    }

    gHasKey = 1;
    SetText(form, "InventoryText", "Inventory: brass key");
    SetText(form, "StatusText", "You picked up the brass key.");
}

void AdventureOpenChestClick(AdventureForm form, short item)
{
    (void)item;
    if (gChestOpen)
    {
        SetText(form, "StatusText", "The chest is already open.");
        return;
    }
    if (!gHasKey)
    {
        SetText(form, "StatusText", "Locked. Take the brass key first.");
        return;
    }

    gChestOpen = 1;
    SetText(form, "SceneText", "The chest opens. You found the treasure!");
    SetText(form, "InventoryText", "Inventory: treasure");
    SetText(form, "StatusText", "You win! Start Over begins a new game.");
}

void AdventureResetClick(AdventureForm form, short item)
{
    (void)item;
    ResetGame(form);
}

void MacStudioUserControlClicked(
    AdventureForm form,
    const char *controlId,
    short item)
{
    (void)form;
    (void)controlId;
    (void)item;
}
