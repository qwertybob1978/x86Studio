#include <Dialogs.h>
#include <Fonts.h>
#include <Menus.h>
#include <Quickdraw.h>
#include <TextEdit.h>
#include <Windows.h>

int main(void)
{
    InitGraf(&qd.thePort);
    InitFonts();
    InitWindows();
    InitMenus();
    TEInit();
    InitDialogs(NULL);
    InitCursor();

    DialogPtr dialog = GetNewDialog(128, NULL, (WindowPtr)-1);
    if(dialog == NULL)
        return 1;

    short item;
    ModalDialog(NULL, &item);
    return item == 1 ? 0 : 1;
}
