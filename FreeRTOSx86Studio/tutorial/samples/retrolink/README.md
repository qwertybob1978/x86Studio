# RetroLink Hercules tutorial sample

This is a FreeRTOSx86Studio project that ports the RetroLink DOS protocol and
file manager to the 8088/Hercules target. The `.x86gui` file supplies an
editable 4:3 LVGL design. At runtime, the application draws its live LOCAL and
REMOTE file panes, compact ImageButton toolbar, serial dropdowns, and transfer
actions using the same LVGL runtime. Toolbar glyphs are alpha-mask RLE data
captured from the matching Windows Material toolbar icons.

The DOS-side protocol source is adapted from the GPL-3.0 RetroLink project.
The original repository was not modified; this sample includes its license
text and keeps the DOS protocol helpers in `src\` and `include\`.

## Open, build, and run

1. Open `RetroLinkTutorial.x86gui` in FreeRTOSx86Studio.
2. Keep the sample's `link-port.txt` set to `COM5` (the emulator's DOS COM2
   link). Change it if your virtual serial pair uses another host port.
3. Use **Build & Run**. DOSBox-X starts an 8088/Hercules guest, initializes
   the runtime Agent on DOS COM1, and listens for RetroLink on DOS COM2 at
   9600 baud. The sample's local pane starts in `DATA\`.
4. Start the original Windows RetroLink application. Select **COM6** and
   **9600** baud, choose this sample's `test-drop\` directory as its LOCAL
   root, then connect.
5. The DOS toolbar defaults to COM2 at 9600 baud. The COM dropdown lists
   BIOS-configured COM2/COM4 ports; COM1/COM3 are reserved because they share
   IRQ4 with the IDE Agent. The baud dropdown offers rates from 300 through
   115200. Changing either setting reopens the DOS listener if it was open;
   set the Windows application to the same baud and reconnect. **LINK**
   toggles the DOS listener; the Windows application initiates the handshake.
6. In DOS, select `Windows Share` in the REMOTE pane and press **OPEN**.
   The DOS peer lists the selected Windows LOCAL root. Select a remote 8.3
   file and press **GET** to request it from Windows and save it under DOS
   `C:\DATA`. Existing destination files are not overwritten.
7. To test DOS-to-Windows transfer, select a file in the DOS LOCAL pane and
   press **SEND**. **MARK** toggles files for a multi-file send. Alternatively,
   select a DOS file under `C:\DATA` in the Windows REMOTE pane and use
   **Copy REMOTE to LOCAL**. Transfers use XMODEM-CRC and verify length and
   CRC.

The configured virtual pairs are DOSBox COM1 ↔ Windows COM2 (Agent), with the
IDE Agent attached to Windows COM4, and DOSBox COM2 ↔ Windows COM5 with the
Windows RetroLink application on COM6. These are separate links. Both ends of
the RetroLink link must use 9600 baud.

`user_sources.txt` lists the additional DOS modules included by the shared
8088 build script. The application lifecycle hooks in the generated GUI task
call `user_app_init()` once and `user_app_poll()` after each LVGL pass, so the
protocol poller runs cooperatively without editing generated task code.
The COM2 receiver buffers incoming bytes from IRQ3 while Hercules rendering
is busy; the UART vector and register state are restored when the guest exits.

## Limits

The DOS share follows the original DOS peer's 8.3 filename limits and supports
files up to 512 KiB. GET refuses to overwrite an existing DOS file and removes
partial files after failed transfers. The GUI target is the 8088/Hercules
real-mode target; this sample does not claim support for the separate 80386
protected-mode target.
