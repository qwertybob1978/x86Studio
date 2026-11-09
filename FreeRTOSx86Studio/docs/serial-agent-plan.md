# Serial Deployment Agent and Debug Session Plan

**Status:** Gate 0 and the Phase 1 Agent lifetime, launch-and-return, and
AppleEvent debug IPC prototypes are verified on Basilisk II/System 7.5.5.
Phase 2 host framing/session, baud selection, and the COM connection window
are verified through the IDE at 57,600 baud. Phase 3 host PUT coordination and
the Agent's streaming XMODEM-CRC receiver/HFS installer passed transfers over
COM30/COM31, including a 5,888-byte package at 57,600 baud. RUN and
DEBUG_RECORD/DEBUG_ACK also succeeded at that rate. STOP timed out while the
probe application displayed a modal dialog and needs retesting with a
quit-responsive target. Error/recovery cases, active-project packaging, real
hardware, and System 7.0 remain unverified.

## Goal

Allow the Windows IDE to connect to a classic Macintosh over a selected host
COM port, identify a running MacStudio Agent, transfer the active project's
built application (the **BUT**, Binary Under Test) using XMODEM, install it on
the Mac's hard disk, launch it on request, and relay structured debug output
back to the IDE.

The same host workflow should support a real Macintosh and an emulator only
when that emulator exposes a usable serial device. A successful Basilisk II
test is useful transport evidence, but is not proof of real-hardware behavior.

## Current baseline and prerequisite

- The IDE has a separate **Window → Serial Terminal** window which enumerates
  Windows COM ports, reads and writes ASCII, and configures Basilisk II's
  `--seriala`/`--serialb` endpoints. The terminal implementation is in
  `src/ClassicMacIde.App/SerialTerminalWindow.xaml.cs`.
- The generated guest runtime exposes `MacStudioSerialOpen`,
  `MacStudioSerialRead`, and `MacStudioSerialWrite`. It also contains
  XMODEM/XMODEM-CRC/XMODEM-1K logic in
  `src/ClassicMacIde.Core/MacFormCodeGenerator.Protocol.cs`; this is guest-side
  generated C, not a Windows host sender.
- The Color Classic serial demo is
  `tutorial/samples/serial-terminal/SerialTerminal.cmideproj`.
- Basilisk II can be configured to open a Windows COM port. The host currently
  has a verified VSPE pair: COM30 for the IDE and COM31 for Basilisk II Serial
  A (Macintosh Modem). Mini vMac currently has no serial passthrough.

**Gate 0 evidence — 2026-10-31:** At 9600 baud, 8-N-1, the Color Classic
guest opened its Modem serial driver and displayed “Modem ready.” The IDE sent
`HOST-TO-GUEST-COM30`, which appeared in the guest receive area; the guest sent
`guest-to-host`, which appeared in the IDE terminal. This verifies the
Basilisk II/VSPE path only, not physical Macintosh hardware.

**Gate 0 must pass before implementation of deployment or debugging:** connect
the IDE terminal to a paired endpoint, run a small guest echo/responder through
Basilisk II, and demonstrate repeatable bidirectional data at the configured
baud rate. Record the endpoint configuration and test results. Do not treat
successful emulator startup or a COM-port listing as proof of serial
communication. Do not install a system driver without user authorization.

## Architecture

```text
Windows IDE
  Connection dialog
       |
  Serial session / host protocol / XMODEM sender
       |
  selected Windows COM port
       |
  virtual null-modem pair, emulator adapter, or physical serial interface
       |
MacStudio Agent (resident background app)
  handshake + XMODEM receiver + HFS installer + launcher + debug relay
       |
  application debug API / local inter-process channel
       |
BUT (the active project's built application)
```

Keep the transport, session protocol, file transfer, and application debug
channel as separate layers. The IDE should not assume that a connected COM
port implies a compatible Agent, and the Agent must not treat arbitrary serial
input as an executable or a shell command.

## Mac Agent feasibility decision

“TSR” is a useful description of the desired behavior, but classic Mac OS is
not DOS: the implementation should first be investigated as a small,
non-visual background application launched by System 7's Startup Items and
running under MultiFinder. It should use the Serial Manager and cooperative
event loop, yield regularly, and remain responsive while the BUT is in front.

Before committing to this design, prototype the following on System 7.0 and
7.5.5:

1. Can the Agent stay alive and service serial traffic while another application
   is foregrounded?
2. Can it launch a second application and continue to run in the background?
3. Does the behavior require MultiFinder or a particular System version?
4. Can it recover cleanly from sleep/wake, an app switch, a disconnected cable,
   or a cancelled transfer?

If a resident background application cannot meet these requirements across
the supported systems, document the minimum supported configuration before
considering a System Extension or other lower-level mechanism. Do not assume a
DOS-style TSR mechanism exists.

## Session protocol

Define a versioned, bounded protocol before adding UI or transfer automation.
Use framed control messages outside the XMODEM data stream. A control frame
should include a magic value, protocol version, message type, session ID,
payload length, and frame checksum. Enforce maximum lengths and timeouts.

Proposed exchange:

1. **Connect:** the host opens exactly one selected COM port with explicit
   baud/data/parity/stop/flow-control settings.
2. **HELLO / HELLO_ACK:** the IDE discovers the Agent and negotiates protocol
   version and capabilities. The Agent reports its version, system version,
   target identity if available, supported baud rates, free disk space, and
   supported transfer modes. Include a fresh session identifier so stale
   responses cannot be mistaken for the current connection.
3. **PUT_BEGIN / READY:** the IDE sends the build ID, safe destination name,
   package length, package checksum, and transfer mode. The Agent validates
   the request and creates a temporary destination file before accepting data.
4. **XMODEM transfer:** send XMODEM-CRC as the baseline; make 1K blocks an
   optional negotiated optimization. Handle ACK/NAK, duplicate blocks,
   retries, cancellation, timeouts, and disconnects. Do not interleave debug
   frames with XMODEM blocks.
5. **PUT_COMPLETE:** the Agent validates the exact received length and
   end-to-end checksum, installs the file, and returns a result tied to the
   session and build ID.
6. **RUN:** the IDE sends a distinct launch request only after installation
   succeeds. The Agent returns launch status and the installed application
   identity.
7. **DEBUG:** after launch, exchange bounded, sequenced debug records and
   acknowledgements. Provide explicit STOP and DISCONNECT operations.

Receiving a file must never automatically execute it merely because XMODEM
ended. Execution requires a valid `RUN` request for the completed build in the
current session. Avoid a general-purpose remote command facility.

## Application packaging and serial ownership

The IDE's current Retro68 build output is an HFS disk image. The deployment
pipeline must extract the actual application file from that image; it must not
send the whole disk image as the BUT.

Classic Mac applications may require a resource fork and Finder metadata in
addition to a data fork. **MacBinary II is selected** as the first package
format. A Retro68 `AgentLaunchTarget` exported with `hcopy -m` had a valid
MacBinary II header CRC, a 3,781-byte resource fork, and `APPL` Finder type.
Importing and exporting it again preserved both forks and Finder metadata.
This validates the package format using the HFS utilities, not the Agent's
future receiver or installer. The Agent must reject malformed packages,
validate fork lengths and checksums, and write both forks and metadata
correctly on HFS. A data-fork-only transfer is not an acceptable launch test.

Use a dedicated `MacStudio` directory on the target disk. Receive into a
temporary file, verify it, then rename it into place. Respect classic HFS
filename limits, check free space before transfer, and avoid overwriting an
unrelated file. Keep the previous known-good build or a recoverable backup
until the new BUT launches successfully.

The Agent should own the physical serial port for the whole debug session. The
BUT must not independently open that same port: doing so can reset or contend
with the Agent's Serial Manager connection. Add a small `MacStudioDebugWrite`
API to instrumented projects and choose a tested local IPC route from the BUT
to the Agent (prototype AppleEvents and a bounded file/mailbox fallback).
The Agent then multiplexes those debug records over its serial session. Keep
file-transfer mode exclusive until the XMODEM session has completed.

## IDE connection workflow

Extend the existing serial terminal workflow with a **Connect to Mac** dialog:

- Enumerate available Windows COM ports, allow refresh and manual entry, and
  show a clear error if a port is missing, busy, or cannot be opened.
- Select the host endpoint, baud rate, data bits, parity, stop bits, and flow
  control. Defaults should match the Agent and be visible rather than implicit.
- Select transport context: Basilisk II serial A/B, a supported Mini vMac
  backend, or a real Mac serial interface. Do not expose unsupported choices
  as working options.
- Show connection state, detected Agent/version/target, negotiated
  capabilities, active build ID, transfer progress, cancellation, and the
  last error. Provide explicit Disconnect and retry.
- Prevent two IDE windows or the terminal and deployment session from opening
  the same COM port concurrently.

Keep the serial session service independent of WPF so it can be tested with
fake streams and loopback transports. Reuse the current terminal for manual
diagnostics, but do not make manual text input part of the deployment protocol.

## Implementation phases and exit criteria

### Phase 0 — Verify the serial transport

- Configure a virtual null-modem pair or suitable physical link. On Windows,
  [com0com](https://com0com.sourceforge.net/) is one option for creating the
  paired virtual serial ports; use one endpoint for the IDE and its partner
  for Basilisk II. If com0com is unsupported on the Windows version in use,
  [Eterlogic Virtual Serial Ports Emulator
  (VSPE)](https://eterlogic.com/Products.VSPE_Download.html) is another
  option; check its current Windows support and licensing.
- Verify two-way ASCII traffic using the existing terminal and a guest
  responder; repeat after emulator restart and at the intended baud rate.
- Record which Basilisk serial endpoint maps to which COM port.
- **Exit (complete):** repeatable bidirectional transfer was demonstrated at
  the configured baud rate. Continue with Phase 1; do not treat emulator
  success as proof of real-hardware behavior.

### Phase 1 — Prove the Agent model and specify formats

- [x] Build a minimal System 7 background Agent that identifies itself, handles
  HELLO/HELLO_ACK, yields correctly, and remains responsive while another app
  is foregrounded. Basilisk II/System 7.5.5 responded to a valid HELLO while
  Finder was foregrounded; a bad-CRC frame was ignored.
- [x] Prototype launch-and-return behavior. The Agent launched a target app
  from a local AppleEvent and returned to serial service while the target was
  foregrounded; a CRC-valid HELLO_ACK was received on Basilisk II/System
  7.5.5.
- [x] Prototype BUT-to-Agent debug IPC with AppleEvents and serial
  `DEBUG_RECORD`/`DEBUG_ACK`; verified on Basilisk II/System 7.5.5 using COM30
  and COM31 at 9600 and 57,600 baud, 8-N-1. This does not verify real hardware
  or System 7.0.
- [x] Select the package format and publish the versioned control-frame
  definition in `docs/serial-agent-protocol.md`.
- **Exit:** Agent lifetime, launch behavior, HFS fork preservation, and debug
  IPC work on at least one supported System 7 setup.

### Phase 2 — Host serial session and connection dialog

- [x] Add a bounded host frame encoder/decoder with CRC and resynchronization
  tests.
- [x] Add a testable stream-backed host session with HELLO negotiation,
  bounded retries, cancellation, session filtering, and disconnect.
- [x] Add an IDE COM-port connection window and connect it to the session
  service; retain the manual terminal as a separate diagnostic tool.
- [x] Show connection state, Agent/System versions, machine type, capabilities,
  available bytes, supported baud-rate flags, and a selectable 300–57,600
  baud rate. The connection was exercised at 57,600 baud, 8-N-1, with no flow
  control.
- Keep all protocol buffers and advertised lengths bounded.
- **Exit:** automated host tests cover framing, malformed acknowledgements,
  Agent errors, timeouts, cancellation, stale sessions, and disconnects. The
  stream-backed host session completed HELLO/HELLO_ACK and clean disconnect
  with the Basilisk II Agent on COM30/COM31; the connection UI was also
  verified at 57,600 baud. Physical-Mac verification remains pending.

### Phase 3 — BUT extraction, transfer, and installation

- [x] Add host UI for selecting a MacBinary II package, choosing a safe HFS
  destination name, transferring over the connected Agent session, and
  cancelling without releasing the COM port before cancellation completes.
- [x] Add an HFS bridge action that exports the built application as MacBinary
  II; verified the header CRC, `APPL` type, and fork lengths on
  `DesignerForm.dsk`.
- [x] Add streaming MacBinary validation and calculate package CRC-32 plus a
  16-byte SHA-256-derived build ID; verified against the actual exported
  `DesignerForm.dsk` application.
- [x] Implement a streaming host XMODEM-CRC sender with 128-byte blocks,
  retries, cancellation signaling, and exact package-length handling; fake
  receiver tests cover block boundaries and CRC.
- [x] Integrate the sender with PUT_BEGIN/READY and PUT_COMPLETE, including
  Build ID/CRC validation, coalesced READY/XMODEM-start handling, response
  timeouts, and exclusive serial-stream ownership; fake-Agent tests cover
  successful transfer, rejection, completion mismatch, and cancellation.
- [x] Implement streaming Agent reception into a temporary HFS file, validate
  MacBinary II length/header/checksum, install both forks and Finder metadata
  under `MacStudio`, and preserve a replaced build as `.MSAGPrevious`.
  Two consecutive 3,968-byte transfers and replacement succeeded on Basilisk
  II/System 7.5.5 over COM30/COM31; this does not establish physical-Mac or
  System 7.0 behavior. A 5,888-byte package also transferred and installed at
  57,600 baud.
- Verify and report disk-full, malformed-package, cancellation, and checksum
  errors without damaging the previous build.
- **Exit:** repeated transfers launch the expected application after emulator
  restarts; interrupted or corrupted transfers never replace the last
  known-good build.

### Phase 4 — Launch and debug sessions

- [x] Add explicit RUN/STOP controls and launch-status reporting; validate
  Build IDs and Agent result codes.
- [x] Add the generated `MacStudioDebugLog` AppleEvent API and a bounded,
  sequenced debug-record receiver with host acknowledgments.
- [x] Verify serial RUN, debug-record delivery/acknowledgement, and reconnect
  at 57,600 baud on Basilisk II/System 7.5.5.
- [ ] Verify STOP with a quit-responsive target; it timed out while the
  `AgentIpcProbe` modal confirmation dialog was open.
- [ ] Add retry/backpressure and durable log queuing; report session lifecycle
  events distinctly from Agent errors.
- [ ] Verify generated `MacStudioDebugLog`, STOP, and full lifecycle behavior
  on supported physical hardware and System 7.0.
- **Exit:** run, log, stop, and reconnect cycles work without the BUT taking
  ownership of the Agent's serial port.

### Phase 5 — Emulator and real-hardware support

- **Basilisk II:** Serial A/Modem passed handshake, package transfer, RUN, and
  debug-record delivery at 9600 and 57,600 baud over COM30/COM31. STOP timed
  out with the modal probe. Verify Serial B/Printer and error recovery before
  claiming those paths.
- The Agent and IDE now support all standard Serial Manager rates from 300
  through 57,600 baud. Verify host, Agent, emulator, and physical adapter
  compatibility at representative low, default, and maximum rates.
- The Agent can select either the Macintosh Modem or Printer serial driver.
  `MacStudio/AgentPort` selects `MODEM` or `PRINTER`; if the file is absent,
  Modem is selected. Verify both mappings through Basilisk II before claiming
  support for both paths.
- **Mini vMac:** remain unsupported until a serial passthrough backend is
  implemented and independently verified; current Mini vMac behavior has no
  attached serial device.
- **Real Macintosh:** validate with a physical System 7 machine, a known-good
  serial adapter/cable, and documented pinout/electrical compatibility.
  Do not assume a PC RS-232 port can be connected directly to a Macintosh
  serial connector.
- **Exit:** publish a per-target support matrix distinguishing emulator
  transport tests from real-machine tests.

### Phase 6 — Documentation and release readiness

- Document Agent installation/removal, Startup Items setup, supported System
  versions, COM wiring, baud settings, recovery, file locations, and known
  limitations. `docs/serial-agent-manual.md` now documents the two
  Mac serial ports, Agent port selection, host/emulator wiring, the MOXA
  UPort 1130I signal map, troubleshooting, and live IDE/guest screenshots;
  physical wiring and electrical compatibility are still unverified.
- Update the in-IDE help, README, sample project, and progress/compatibility
  records.
- Include a diagnostic mode that reports protocol/Agent versions and port
  settings without transferring or launching files.
- **Exit:** a user can set up the target and complete a documented deploy-run-
  debug cycle without manually editing emulator preferences or project source.

## Verification matrix

| Area | Required checks |
|---|---|
| Host protocol | Golden frame tests, version negotiation, invalid lengths/types, timeouts, stale session IDs |
| XMODEM | CRC failures, duplicate/retried blocks, cancellation, boundary sizes, truncated transfer, disconnect |
| Package/HFS | Both forks and Finder metadata preserved; long names, disk full, corrupt package, safe replacement |
| Agent lifetime | Startup launch, background scheduling, foreground app launch, sleep/wake, restart and recovery |
| Debugging | Ordered records, bounded output, app exit/error reporting, STOP, reconnect after cable loss |
| Emulator | Basilisk II with paired COM endpoints; Mini vMac only after passthrough exists |
| Hardware | Real System 7 Mac with validated adapter/cable and repeated deploy-run-debug cycles |

## Known risks and decisions

- **Emulator versus hardware:** Gate 0 verifies Basilisk II with COM30/COM31
  only; it does not establish physical Macintosh compatibility.
- **Background app semantics:** System 7 scheduling and launch behavior must be
  proven on the oldest supported system before choosing a resident design.
- **Resource forks:** raw XMODEM bytes alone do not describe an installable Mac
  application; the package/installer must preserve forks and Finder metadata.
- **Serial ownership:** the Agent and BUT cannot both reset/use the same port
  independently. The Agent must remain the sole serial owner during debug.
- **Throughput and recovery:** classic serial speeds make large applications
  slow to send. Report estimated time, allow cancellation, and keep transfer
  restart/recovery safe.
- **Hardware differences:** modem/printer port wiring, handshaking, adapters,
  and emulator mappings differ. Make settings explicit and test physical
  hardware separately from emulator support. The current cable diagram must
  be checked against the exact UPort 1130I manual and validated on real
  hardware before being treated as an approved cable design.
