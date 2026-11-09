#include "Dialogs.h"
#include "Fonts.h"
#include "Menus.h"
#include "Quickdraw.h"
#include "TextEdit.h"
#include "Windows.h"

int main(void)
{
    short item;
    DialogPtr dialog;

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
    return 0;
}
