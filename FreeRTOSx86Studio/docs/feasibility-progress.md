# Feasibility Spike Progress

**Updated:** 2026-10-31
**Plan status:** In Progress  
**Spike status:** Mac SE System 7.5.5 boot, 68k HelloMac build, and visible sample launch verified. This verifies the sample on the tested setup only; broader compatibility remains unverified.

## Delivered

- Windows entrypoints for prerequisite probing and configured build → emulator-process orchestration.
- Deno 2 task runner and automated unit/orchestration tests; no third-party packages are required.
- Machine-readable toolchain and per-run evidence output under `feasibility/evidence/`.
- Candidate compatibility matrix with explicit unverified states and required evidence.
- Emulator binaries are stored in the Git-ignored `.local-tools/` folder; user-provided ROMs and disk images remain in the Git-ignored `images/` folder.
- The WPF IDE foundation is tracked separately in `docs/implementation-progress.md`; the legacy build-and-launch compatibility gate remains closed.

## Latest run

The current host probe found:

- Deno 2.9.7 and Git 2.53.0 on Windows; WSL 2.6.3.0 with Ubuntu and Kali distributions.
- Windows CMake and Ubuntu CMake/make are present.
- The selected WPF desktop IDE does not require Rust or Tauri; the earlier environment probe found no Rust toolchain in Windows, Ubuntu, or Kali.
- Retro68 GCC 16.1.0 68k and PowerPC compilers and the `Rez` resource compiler are installed under `~/retro68-build/toolchain/bin`. The `RIncludes` paths are generated include-resource directories, not executable tools.
- Basilisk II, SheepShaver, MAME, and a Mini vMac 36.04 custom SE variation are available in `.local-tools/`. MAME's Mac SE machine cannot start without the separate 1 KB ADB modem firmware ROM `342s0440-b.bin`.
- The user supplied `images/OS_755_2GB.dsk`, reported copied from their own Mac. Preserve this original; use and delete a disposable working copy for each test. The file is excluded from Git by `*.dsk`.
- The user supplied a Mac SE ROM at `images/256KB ROMs/1987-03 - B2E362A8 - Mac SE.ROM`. Its SHA-1 matches the MAME `macse` ROM definition.

### HelloMac guest application launch — 2026-10-30

- Retro68 rebuilt the tracked 68k sample into an HFS application disk; SHA-256: `8cad665941c8f77528380ce8304b479d948595a6fa589302790c44743d32aea8`.
- The Windows workflow extracted disposable boot media and started Mini vMac with the application disk as the second volume.
- Finder opened `HelloMac` and displayed the “Hello, Mac!” dialog. The evidence capture is from the emulator window on the secondary monitor, not the full desktop.
- Captured evidence: `evidence/mac-se-hello-mac.png`. The boot helper exited successfully and removed its disposable media; the original disk and ROM remain unchanged.

### Mac SE guest boot — 2026-10-30

- Generated Mini vMac 36.04 Windows x64 variation for `-m SE`; the ROM must be named `MacSE.ROM` alongside the executable.
- Confirmed the source disk contains one Apple_HFS partition, extracted it into a disposable temporary `disk1.dsk`, launched Mini vMac, and observed the System 7.5.5 Finder desktop at 512×342 monochrome. The visible Demo label is the free-variation overlay.
- Saved the screen evidence to `evidence/mac-se-system-7-5-5-desktop.png`. The original disk and ROM were preserved and hash-checked after the run. The temporary disk and ROM copies were removed.
- The fresh Retro68 68k build and Windows-driven emulator start are verified. The user observed the HelloMac dialog in the guest. No LC III/PowerPC application launch has been verified.

The toolchain report was refreshed after the Retro68 setup completed. Ubuntu now reports both Retro68 cross-compilers and `Rez` available under `~/retro68-build/toolchain/bin`. The report still marks overall desktop/build readiness and compatibility claims false; those summary flags do not replace the per-profile build-and-launch evidence or open the exit gate.

## Validation

| Check | Result |
|---|---|
| `deno task check` | Passed |
| `deno task test` | Passed: 9 tests, 0 failed (includes configured build/launch runner with fake local commands) |
| Windows PowerShell entrypoints | Parsed successfully |
| Windows wrapper smoke test | Passed with fake local build/emulator commands; guest verification correctly remains false |
| Windows toolchain probe | Completed; see evidence JSON |
| Classic Mac guest boot | Mac SE System 7.5.5 Finder boot passed in Mini vMac; see `evidence/mac-se-system-7-5-5-desktop.png` |
| Classic Mac application build and launch | HelloMac rebuilt; Mini vMac booted the guest with the app disk attached; user confirmed the “Hello, Mac!” dialog |
| MAME Mac SE run | Blocked: `342s0440-b.bin` ADB modem firmware ROM is not present |
| LC III / PowerPC guest runs | Not run |
| WPF desktop build | Passed; 0 warnings and 0 errors |

## Exit gate and next evidence

**Sample loop passed for the Mac SE/System 7.5.5 setup.** This evidence does not establish general application compatibility or support for other models, processor architectures, or system versions. Next, investigate LC III and PowerPC toolchain/emulator profiles and record each only after its own build and visible guest launch.

### Color-target profile and drawing validation — 2026-10-31

- Added candidate Macintosh Color Classic (512 × 384, 8-bit) and PowerBook 165c (640 × 400, 8-bit) designer profiles.
- Added validated `#RRGGBB` QuickDraw graphics properties, with a second background color for dithered graphics. Color properties are shown only for color targets; monochrome drawing remains unchanged.
- Core tests passed (92); the WPF Release build passed with 0 warnings/errors; Retro68 accepted the Color QuickDraw API declarations in a syntax-only check.
- The local Color Classic and PowerBook main ROM images match MAME 0.289's machine definitions. `-verifyroms` still fails because Color Classic CUDA firmware and PowerBook video/PMU firmware are absent. Mini vMac's variation service does not list either model.
- Basilisk II booted System 7.5.5 with both ROMs using generic model ID 5 and target-sized 8-bit windows. Generated Color QuickDraw smoke applications rendered red, dithered blue/cyan, and green shapes in both sessions.
- Exact Color Classic and PowerBook hardware emulation remains unverified; the successful Basilisk II runs do not establish native hardware timing, display mapping, or general app compatibility. Color Build & Run is routed through Basilisk II; Build & Test remains limited to Mini vMac/Mac SE.

### Basilisk II and host serial investigation — 2026-10-31

- The installed Basilisk II Windows build exposes `--seriala` and `--serialb`; upstream Windows backend code opens the configured Windows COM device directly. It does not provide a host-side virtual serial pair or TCP serial bridge.
- The official Basilisk II README documents an explicit ROM path, attachable Mac disk images, Windows `win/<width>/<height>/<bits>` window mode, and `dx`/`fb` fullscreen modes. Its default Mac model ID is 5 (Mac IIci); other IDs are documented as unsupported and potentially unstable, so this is not an exact Color Classic or PowerBook machine profile.
- `--rominfo` is a diagnostic flag that prints during ROM patching and then continues emulation; it is not a one-shot command. The earlier bounded checks did not capture its redirected output, and their continued process lifetime was expected. They do not establish ROM compatibility either way.
- With the official Windows window mode, a disposable copy of the local System 7.5.5 disk, and `modelid=5`, both the Color Classic ROM (512 × 384 window) and PowerBook 165c ROM (640 × 400 window) booted to Finder in Basilisk II. This confirms usable ROMs for a generic Basilisk II session, not exact target hardware emulation.
- The present Windows PnP Ports listing contains Moxa Real COM Port devices COM7–COM22, but no paired virtual-COM endpoints were identified. No driver was installed and no port was opened.
- The Finder desktop can appear monochrome even when color QuickDraw is available; generated smoke apps subsequently rendered visible color under both the Color Classic and PowerBook ROMs. Exact hardware compatibility remains unverified because Basilisk II used generic model ID 5. No serial device was opened; serial simulator work still requires an explicitly verified loopback/virtual-COM pair.

### Emulator menu and color app workflow — 2026-10-31

- Added mutually exclusive **Run → Mini vMac / Basilisk II** choices. Mini vMac remains the Mac SE build/test path; Basilisk II Build & Run is enabled for Color Classic and PowerBook 165c only. Build & Test remains limited to Mini vMac/Mac SE.
- Routed the generated app through Retro68 and the shared disposable-volume installer; Basilisk II receives the matching ROM, System 7 volume, app disk, target-sized 8-bit window, and generic model ID 5. Startup Items automatically launched the app in both integrated smoke runs.
- Confirmed red, dithered blue/cyan, and green QuickDraw output for both profiles. Captured [Color Classic](../feasibility/evidence/basilisk-color-classic-quickdraw.png) and [PowerBook 165c](../feasibility/evidence/basilisk-powerbook-165c-quickdraw.png) guest-window evidence. Original ROMs and the source System 7 disk were not modified.
- Ran **Build & Run from the WPF IDE** with Basilisk II selected and a Color Classic form containing a button. Retro68 built the generated `DesignerForm`, Basilisk II launched it from Startup Items, and the IDE tracked and stopped the emulator through **Stop Emulator**. The generated smoke-app disk from the direct workflow had SHA-256 `bfa368cd2c68f1e3c006b33c749a4ba1951a395b57d29e84daee2fb8d810efc4`.
- The color runs prove this generated app's rendering in Basilisk II's generic setup only; they do not prove exact Color Classic or PowerBook hardware compatibility.
- Core Release tests passed (95); the WPF Release build passed with 0 warnings/errors; both edited PowerShell scripts parsed successfully. The freshly built IDE displayed the Run menu with both choices.

### Form-level classic window properties — 2026-10-31

- Added persisted form properties for title-bar visibility, resizing, close and zoom boxes, movable title bars, launch visibility, centered or explicit position, and initial client size. Existing schema-version-1 project files load with the existing titled, visible, centered dialog defaults.
- The form inspector is available when the designer canvas is selected. The preview tracks title-bar and form dimensions; titleless forms omit the title-bar offset in generated widget bounds.
- Generated Rez resources select standard classic window procedures. Resizable forms now handle grow, zoom, title-bar drag, and close-box events, then clean up and exit normally when closed.
- Core Release tests passed (98). The WPF Release build succeeded with 0 warnings/errors using a separate output directory because the already-running IDE held its normal output assemblies open. Emulator interaction for the new window behaviors remains to be verified.

### IDE serial terminal and Color Classic demo — 2026-10-31

- Added a separate **Window → Serial Terminal** host window with editable COM-port selection, ASCII 8-N-1 send/receive, and Basilisk II Serial A/Serial B endpoint configuration. Build & Run now forwards configured endpoints as `--seriala`/`--serialb`.
- Added the loadable Color Classic project `tutorial/samples/serial-terminal/SerialTerminal.cmideproj`, with editable `main.c`, Send/Clear buttons, a serial component, and bound receive/error handlers in `user_events.c`.
- Validation passed: 104 Core tests, WPF Release build (0 warnings/errors), 9 Deno tests, PowerShell syntax and argument checks, and a Retro68 68k sample build. The generated `SerialTerminal.dsk` SHA-256 was `d1a3c7891d3ff331ec765722e52f0c5a497571f4891e0e942787149cec3570c2`.
- At this milestone the host exposed Moxa COM7–COM22 devices but no identified pair; no driver was installed and no port was opened. The later Gate 0 verification below supersedes that status.

### Serial transport gate verified — 2026-10-31

- The user-configured VSPE null-modem pair COM30 (IDE) and COM31 (Basilisk II Serial A / Mac Modem) was used at 9600 baud, 8-N-1.
- Fixed the generated serial runtime to open the Macintosh `.AIn`/`.AOut` or `.BIn`/`.BOut` drivers with `OpenDriver`, retain the returned refnums, and close them with `CloseDriver`.
- Retro68 built the Color Classic demo successfully. The guest reported “Modem ready”; `HOST-TO-GUEST-COM30` appeared in the guest receive area, and `guest-to-host` appeared in the IDE terminal.
- Gate 0 is complete for Basilisk II and VSPE only. Real Macintosh transport and Agent behavior remain unverified.
