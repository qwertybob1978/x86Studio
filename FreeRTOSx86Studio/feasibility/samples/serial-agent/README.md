# MacStudio Serial Agent prototype

This Retro68 68k application is an early System 7 background-process
prototype with a **Baud Rate** menu. It opens either the Modem or Printer
serial port at the configured classic Serial Manager rate (300–57,600 baud;
9600 by default), 8-N-1, with no flow control, polls for bounded `MSAG` version
1 frames, and
responds to a compatible four-byte `HELLO` with `HELLO_ACK`. The absent
`MacStudio/AgentPort` file selects Modem; the file can contain `MODEM` or
`PRINTER` to select a port. `MacStudio/AgentBaudRate` sets the startup rate
using one supported numeric value; the file may be absent for the 9600 default.
While running, the Baud Rate menu changes the rate immediately and saves the
selection to that file. The event loop calls `WaitNextEvent` every tick so
MultiFinder can schedule foreground applications.
For disposable emulator runs, `scripts/windows/boot-mac-se.ps1` can create this
file before startup with `-AgentBaudRate <rate>` when autostarting
`MacStudioAgent`.
It accepts one bounded local debug record from an AppleEvent and relays it to
the host as `DEBUG_RECORD` after a serial session is established; the host
acknowledges it with `DEBUG_ACK`. PUT receives XMODEM-CRC blocks into a
temporary file in the HFS `MacStudio` directory, validates the MacBinary II
package and checksum, then installs both forks and Finder type/creator. A
replacement keeps the previous application as `.MSAGPrevious`. RUN launches
the most recently installed build, and STOP requests that application to quit.
Generated projects include `MacStudioDebugLog(severity, message)`, which
sends a bounded printable-ASCII record to the Agent using AppleEvents.

The debug queue holds one record and sends it once; it has no retry or durable
storage. RUN and the probe's debug-record relay/acknowledgement were verified
over the serial link at 57,600 baud on Basilisk II/System 7.5.5. STOP timed out
while the probe displayed a modal dialog and remains unverified with a
quit-responsive target. The generated logging API still needs an end-to-end
serial test. An unrecoverable Serial Manager error ends the prototype process.
Its `SIZE` resource enables background and foreground operation under
MultiFinder.
`AgentIpcProbe` sends one bounded local debug record to the Agent with
AppleEvents. `ZAgentLaunchProbe` asks the Agent to launch the `AgentLaunchTarget`
application using a local AppleEvent; the target must be installed at the root
of the same HFS volume as the probe.

Build it in WSL Ubuntu with the repository's Retro68 helper:

```sh
bash scripts/wsl/build-hello-mac.sh \
  feasibility/samples/serial-agent \
  .local-tools/retro68-serial-agent-build \
  MacStudioAgent
bash scripts/wsl/build-hello-mac.sh \
  feasibility/samples/serial-agent \
  .local-tools/retro68-serial-agent-build \
  AgentIpcProbe
bash scripts/wsl/build-hello-mac.sh \
  feasibility/samples/serial-agent \
  .local-tools/retro68-serial-agent-build \
  ZAgentLaunchProbe
bash scripts/wsl/build-hello-mac.sh \
  feasibility/samples/serial-agent \
  .local-tools/retro68-serial-agent-build \
  AgentLaunchTarget
```

Each command produces a `.dsk` application image in the build directory.

MacBinary II was validated with the Retro68 `AgentLaunchTarget` application:
the `hcopy -m` export had a valid header CRC, a 3,781-byte resource fork, and
`APPL` Finder type; importing and exporting it again preserved both forks and
Finder metadata.

The HFS bridge can export a built application disk as a MacBinary II package:

```bash
bash scripts/wsl/mac-test-bridge.sh export-macbinary \
  /path/to/DesignerForm.dsk DesignerForm \
  /path/to/DesignerForm.bin
```

Use WSL paths and an existing destination directory. The bridge exports to a
temporary file and replaces the destination only after a non-empty package is
produced. This action was verified on `DesignerForm.dsk`; the output header
CRC matched, its Finder type was `APPL`, and its zero-byte data fork and
58,015-byte resource fork lengths matched the package size.

The IDE's Core package inspector validates the MacBinary header CRC and exact
fork lengths while streaming the file. It computes CRC-32/ISO-HDLC over the
complete package and uses the first 16 bytes of its SHA-256 digest as the
Build ID. Inspection of the exported `DesignerForm` package succeeded with a
58,240-byte package and CRC-32 `E7D76DC9`.

The host XMODEM-CRC sender streams the package in 128-byte blocks, uses the
XMODEM CRC-16 variant, retries unacknowledged blocks, pads the final block, and
sends CAN on cancellation. `MacStudioAgentSession.PutAsync` coordinates
PUT_BEGIN/READY, the raw XMODEM stream, and PUT_COMPLETE identity/checksum
validation. Fake-Agent tests cover coalesced READY/CRC-request bytes,
successful transfers, rejection, timeouts, exclusive serial ownership, and
completion mismatch. Live testing transferred a 3,968-byte MacBinary
`AgentLaunchTarget` twice over COM30/COM31 to Basilisk II/System 7.5.5; both
installations completed, including replacement of the same destination. This
does not establish real-Mac or System 7.0 behavior. Disk-full and corrupted
transfer recovery remain unverified.

In the IDE, open **Window → Serial Agent Connection**, select the host COM
endpoint, and connect. Enter a safe HFS destination name (ASCII letters,
digits, and underscores), then choose **Send MacBinary Package...**. After
installation, use **Run Installed App** and **Stop App** to control that
Build ID. The UI accepts a pre-exported package; it does not yet package the
active IDE build. Receiving a file never launches it automatically.

When developing an application with the generated form code, call
`MacStudioDebugLog(severity, message)` to send a printable-ASCII message of at
most 512 bytes to the IDE. The Agent must be running and connected; the call
returns an `OSErr` if the Agent is unavailable, the message is invalid, or the
single-record queue is occupied. No retry or durable queue is provided.

The build, first installation, emulator bootstrap, deployment workflow, and
test limitations are in the [Serial Agent deployment guide](../../../docs/serial-agent-deployment.md).
The target-side connector, cable wiring, `AgentPort` setup, emulator port
mapping, and troubleshooting steps are in the
[Serial Agent connection manual](../../../docs/serial-agent-manual.md). The manual's
MOXA UPort 1130I pin mapping is a wiring reference only; physical hardware
compatibility has not yet been verified.

Live testing passed on Basilisk II/System 7.5.5 with host COM30 and Basilisk
Serial A on COM31 at 9600 baud, 8-N-1. The Agent returned a CRC-valid
`HELLO_ACK` with the exact session ID, protocol/Agent versions, System 7.5,
machine type 4, and the 9600-baud flag. The probe then queued
`AppleEvent IPC probe`; the host received it as a valid `DEBUG_RECORD` and
returned `DEBUG_ACK`. A bad-CRC `HELLO` received no response in the earlier
framing test. System 7.0 coverage and real Macintosh behavior remain
unverified.

The launch probe also succeeded on that setup: it launched the target from the
boot volume root, displayed the target's dialog, and the Agent returned a
valid `HELLO_ACK` while that target was foregrounded. This verifies
launch-and-return plus background serial scheduling only under Basilisk
II/System 7.5.5.

At 57,600 baud, the IDE and Agent negotiated a connection over COM30/COM31,
transferred and installed the 5,888-byte `AgentIpcProbe` package (CRC-32
`0E0D0620`), and launched it with **Run Installed App**. The IDE received the
probe's `AppleEvent IPC probe` record and acknowledged it. **Stop App** timed
out while the guest probe was showing its modal confirmation dialog, so STOP
is not considered verified. The generated `MacStudioDebugLog` API, physical
Macintosh behavior, and System 7.0 remain unverified. Screenshots of the IDE
and guest are included in the [Serial Agent connection
manual](../../../docs/serial-agent-manual.md).
