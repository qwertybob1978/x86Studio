# Serial Terminal sample

This is the finished project used by
[Classic Mac for Dummies 2](../../guides/classic-mac-for-dummies-2-serial-terminal.md).
Open `SerialTerminal.cmideproj` in ClassicMacStudio. The project targets
the Color Classic profile and includes the form controls, a `SerialPort1`
component, and the bound Send, Clear, receive, and error handlers.

The component opens the Modem port at 9600 baud, 8-N-1, with no flow control.
For interactive testing, select **Run → Basilisk II**, configure the IDE's
**Window → Serial Terminal** with opposite endpoints of a virtual null-modem
pair, apply the Basilisk guest endpoint, connect the IDE endpoint, and choose
**Build & Run**. Mini vMac does not pass serial devices through.

This example has no automated guest Build & Test because that workflow is
limited to Mini vMac and the Mac SE profile. Its serial integration checks are
in the tutorial. The Retro68 68k build succeeds, and the sample's bidirectional
serial traffic was verified in Basilisk II on COM30/COM31 at 9600 baud, 8-N-1,
including a repeat after restarting the emulator. The current tutorial
screenshots show an unconnected setup because that pair was in use by the
Serial Agent at 57,600 baud; they do not depict the earlier live test. See
[developerManual.md](../../../docs/developerManual.md#ide-serial-terminal-and-demo-project)
for the transport setup. Physical Macintosh/MOXA communication remains
unverified.
