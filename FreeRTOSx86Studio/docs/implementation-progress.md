# Implementation Progress

**Updated:** 2026-10-31
**Status:** In progress  
**Current increment:** Color-capable classic Mac designer profiles and QuickDraw properties

## Decisions

- Use C#/.NET WPF for the Windows desktop IDE. Deno remains the task runner for existing feasibility scripts and tests; it is not the desktop UI host.
- Keep compatibility claims gated on observed builds and application launches. The Mac SE's System 7.5.5 boot, Hello Mac sample, and generated designer form have been verified; the generated form's button closes its app.
- Use a separate core library for target profiles and designer data so its rules can be unit tested without starting WPF.
- Keep built-in widgets within System 7's standard Dialog, Control, List, and Menu Manager APIs; use a per-widget generator strategy so later widgets do not require a central generator rewrite.

## Completed

- Created a .NET solution with a WPF application, a UI-independent core library, and an xUnit test project.
- Added initial Mac SE, LC III, and Power Macintosh 6100 target display profiles. Only the Mac SE guest OS boot is recorded as verified; application compatibility remains unverified.
- Added a profile-aware screen preview with monochrome/color mode and resolution indicators, plus basic button placement and read-only geometry inspection.
- Added core validation and unit tests for display profiles, monochrome depth, and designer control bounds.
- Added a Windows PowerShell test runner and Deno tasks to run Deno checks/tests and .NET unit tests together.
- Added a WPF **Build & Run Hello Mac** action for the tracked sample; it invokes the Windows/WSL workflow and displays build output. It does not yet build the unsaved designer preview.
- Added a scripted Retro68 build, disposable System 7 boot volume, and second HFS application disk launch path. Original user-provided system media are read-only inputs.
- Restricted the run action to the Mac SE profile until other emulator/build profiles are configured.
- The tracked Hello Mac sample rebuilt successfully with Retro68 68k, producing a valid HFS application disk (SHA-256 `4fc803c949bce682806a929f207fb48bfbf1da4edf43fea23bf8a867a6d3a754`).
- Ran the integrated Windows script; it extracted a disposable System 7 volume, mounted the generated application disk as the second Mini vMac drive, and started Mini vMac.
- The user confirmed that `HelloMac` opened in Finder and displayed the “Hello, Mac!” dialog on the Mac SE/System 7.5.5 guest.
- Captured the guest dialog at [feasibility/evidence/mac-se-hello-mac.png](../feasibility/evidence/mac-se-hello-mac.png).
- Added a unit-tested C/Rez/CMake generator that translates the designer's form title and button controls into a buildable 68k application.
- Changed **Build & Run Designer Form** to generate source from the current designer model, build it with Retro68, and launch the resulting HFS disk in Mini vMac.
- Successfully built the generated `DesignerForm.dsk`; its SHA-256 is `9c23e468e7ab836fee11ed70f56282fd8a46481f4dbbbf16a325bd74b806167e`.
- Updated boot-volume extraction to retain zero-filled regions as a sparse file, reducing writes when preparing the disposable System 7 boot volume.
- Generalized emulator prompts and IDE status text so they identify the generated application and form rather than the earlier Hello Mac dialog.
- Ran `DesignerForm` on the Mac SE/System 7.5.5 guest. The user confirmed the form appeared and its button closed the application.
- Added a versioned HFS mailbox protocol for staging test requests and returning guest telemetry.
- Added automatic app launch by copying the generated application (including its resource fork) into the configured System 7 `Startup Items` folder on the disposable boot volume.
- Added a **Build & Test** action that stages `QUERY_FORM`, button-click, and `QUIT` commands on that volume, then validates returned metadata and handled events.
- Changed **Build & Run** to leave the auto-started form open in the guest; no manual Finder launch is required.
- Added a **Stop Emulator** action that closes the IDE-owned Mini vMac process gracefully to flush guest disks, then force-stops it after a timeout.
- The guest application flushes its telemetry volume before exit; the boot helper extracts `MacStudio.out` from the disposable System 7 volume before cleaning it up.
- Verified Build & Run with the form visible in the guest without manual input, and verified Build & Test automatically launched, reported form/control metadata, handled a staged button click, returned `DONE`, and passed the IDE assertions. The bridge is batch/file-based, not live serial IPC.
- Mini vMac's exclusive disk-image lock prevents live HFS mailbox reads. Build & Test now watches for a test-only monochrome completion marker in the emulator window, stops the owned process, then extracts the structured mailbox response; Build & Run remains open and unchanged.
- Verified the completion handshake end to end: the host detected the marker, stopped Mini vMac, and extracted `FORM`, `CONTROL`, handled `EVENT`, and `DONE` records without manual interaction.
- Added a widget registry and built-in catalog for static text, editable text, push buttons, checkboxes, radio buttons, popup menus, list boxes, and scrollbars, with geometry/property validation and per-widget generator hooks.
- Replaced the single-button toolbox with a data-driven widget palette, classic monochrome previews, click-to-place placement, drag-move, and editable geometry/text/widget properties.
- Extended generated DITL/Rez and guest event-loop generation for built-in widgets, radio-group selection, list construction, popup menu resources, and Control Manager scrollbars.
- Versioned the guest bridge as `MACSTUDIO/2` and included widget type IDs in control telemetry so Build & Test can verify widget kind and placement. Generated guest tests also exercise edit-text round-tripping and initial checkbox, radio, popup, list, and scrollbar states.
- Created [developerManual.md](developerManual.md) with the widget catalog, architecture/API guidance, extension steps, and Docmost-friendly Mermaid diagrams. The Mini vMac FAQ confirms that the emulator currently has no networking and its serial ports are unattached; the documented workflow therefore uses the HFS mailbox.
- Added adjustable 2–64 pixel grid spacing, optional snap-to-grid, group selection/movement, edge alignment, and a dedicated resize mode with minimum-size and form-bound checks.
- Restyled the designer preview with a striped classic title bar, classic button outlines, and a Chicago-font request with documented Windows fallback behavior.
- Added versioned `.cmideproj` JSON save/load for project name, target profile, form title, stable control IDs, geometry, and widget properties.
- Added a lazy project file tree and an AvalonEdit code tab with C/C++, Rez, and common file syntax highlighting; generated UI sources are read-only.
- Added project-scoped `src`/`.build` output and an editable `user_events.c` callback file that is preserved across designer builds and invoked for interactive clicks and Build & Test commands.
- Generated stable-ID C accessors for Pascal text and widget values. Text APIs support static/editable text and standard buttons; value APIs support checkbox/radio state, popup/list selection, and scrollbar position, with documented `noErr`/`paramErr` contracts.
- Initialized the List Manager selection search cell before `LGetSelect`, and changed widget setters to invalidate only their dialog item while preserving the active GrafPort. Guest round-trips passed for text and values, including invalid-value rejection.
- The boot helper now extracts telemetry after a missing test marker, enabling flushed normal-mode traces. A clean eight-widget no-request launch reached `READY`, created the dialog and all widgets, reported all metadata, and completed its first `WaitNextEvent`/`IsDialogEvent`/`DialogSelect` cycle without error. Earlier System errors were not reproduced; visual screen inspection remains pending.
- Updated the boot helper to extract guest telemetry before reporting a missing completion marker, preserving partial diagnostics when the guest has written a response.

## Current issue

- The user reports a repeated System error 88 and shared a screenshot of the application unexpectedly quitting. Classic Mac error 88 is `dsCDEFnFnd` (“Could not load CDEF”), pointing to a missing/unavailable Control Definition Function rather than a generic memory error.
- The generated scrollbar uses `scrollBarProc` (CDEF procedure ID 16) in `NewControl`. Clean guest startup has returned from `NewControl` and completed the first dialog-event cycle, so the exact failing call remains unconfirmed; normal mouse interaction was not exercised in that trace.
- Investigate actual interaction paths (`DialogSelect`, list `LClick`, scrollbar `TrackControl`, and popup selection) using a freshly built application disk before changing production behavior. Do not treat the synthetic mailbox click tests as proof that native mouse tracking is safe.
- Current investigation: the existing 53-test Core suite passes. The emulator boot/build scripts now accept an optional BMP guest-screen capture path for completion or timeout snapshots; PowerShell syntax passes, but guest capture is not yet verified.
- The ignored guest harness can now generate either the full showcase or a single widget. Its first `dotnet run` attempt stalled before updating generated files and was stopped; retry with diagnostics before using it for emulator conclusions.
- Fresh eight-widget emulator build completed and displayed the completion marker, with no startup failure observed. The first screenshot attempt failed at the last-pixel boundary before telemetry extraction; the sampling coordinates have been clamped and require a rerun.
- The rerun passed: the guest returned all eight control records, seven synthetic `HANDLED` clicks, and `DONE`; the captured 512 x 342 screen showed no type-88 alert. Visual inspection still needs per-widget isolation because the showcase screenshot has unclear/clipped placements, and synthetic bridge clicks do not exercise native mouse tracking.

## Validation

| Check | Result |
|---|---|
| Deno check | Passed through `deno task test:all` |
| Deno feasibility unit tests | Passed: 9 passed, 0 failed |
| WPF app build | Passed: 0 warnings, 0 errors |
| .NET core unit tests | Passed: 9 passed, 0 failed |
| Combined test script (`deno task test:all`) | Passed |
| Combined tests after build/run action | Passed: 9 Deno, 9 .NET; WPF Release build 0 warnings/errors |
| PowerShell syntax checks | Passed for boot and build/run scripts |
| Combined test script after designer-form implementation | Passed: 9 Deno, 16 .NET; WPF Release build 0 warnings/errors |
| Sparse-file capability probe | Passed: current Windows account can mark NTFS files sparse |
| Retro68 reproducible build | Passed with `scripts/wsl/build-hello-mac.sh`; HFS disk checksum recorded above |
| Integrated Windows/WSL/emulator workflow | Build succeeded and Mini vMac started with disposable system and application volumes |
| Mac SE HelloMac guest launch | Passed: user observed the “Hello, Mac!” dialog |
| Generated DesignerForm build | Passed: 68k application and HFS disk produced |
| Generated DesignerForm guest launch | Passed: user confirmed the form appeared and the button closed the application |
| Versioned HFS mailbox staging and extraction | Passed: request and guest response round-tripped through the disposable System 7 volume |
| IDE Build & Run autostart | Passed: generated form was visible in the guest without manually opening Finder |
| IDE Build & Test guest loop | Passed: auto-started app returned form/control telemetry and one handled button click |
| IDE Stop Emulator | Passed: owned Mini vMac process closed and guest telemetry was extracted |
| Latest combined test script (`deno task test:all`) | Passed: 9 Deno, 28 .NET; WPF Release build 0 warnings/errors; PowerShell and bridge-script syntax checks |
| Automatic test completion handshake | Passed: marker detected, emulator stopped, and structured response extracted from the guest mailbox |
| Full validation after completion-marker change | Passed: 9 Deno, 28 .NET, WPF Release build with 0 warnings/errors, PowerShell and bridge-script syntax checks |
| Core tests after widget catalog and generator changes | Passed: 39 tests |
| WPF Release build after widget designer changes | Passed: 0 warnings, 0 errors; isolated output used because the running IDE locks the normal output |
| Retro68 68k build of the generated nine-control/eight-type showcase | Passed; C and Rez resources compile into `WidgetShowcase.dsk` |
| Designer geometry tests | Passed: 11 focused tests covering snapping and all four alignment edges |
| Core tests after designer precision tools | Passed: 47 tests |
| WPF Release build after designer precision tools | Passed: 0 warnings, 0 errors; isolated output used because existing editor processes hold normal outputs |
| Mini vMac widget showcase | Passed: all eight widget types reported on Mac SE/System 7.5.5; all seven interactive widgets reported `HANDLED`; test-owned emulator stopped and telemetry extracted |
| Project serializer tests | Passed: 6 tests cover JSON round-trip, file save/load, schema and target validation, bounds, and duplicate IDs |
| Core tests after project/event-code changes | Passed: 53 tests |
| Deno check and tests | Passed: type check and 9 tests |
| WPF Release build after project explorer and code editor | Passed: 0 warnings, 0 errors; isolated output used because running editor processes hold standard outputs |
| Retro68 showcase with editable event source | Passed: generated C, event source, and Rez compiled; Mac SE guest returned all widget metadata and seven handled events |
| Core tests after generated property accessors | Passed: 53 tests |
| WPF Release build after generated property accessors | Passed: 0 warnings, 0 errors; isolated output used |
| Mac SE three-control accessor round-trip | Passed: button text and checkbox/radio values were read and updated by stable ID; guest returned `DONE` |
| Mac SE eight-widget interaction loop | Passed: clean generated source returned all eight controls, all seven interactive widgets reported `HANDLED`, popup/list/scrollbar checks succeeded, and the guest returned `DONE` |
| Mac SE normal-mode generated form launch | Passed: a generated form started without a test request and remained visible on System 7.5.5 |
| Remaining accessor round-trips | Passed: text and all widget values round-tripped in the guest; invalid values were rejected |
| Current Core unit tests | Passed: 53 tests |
| Current eight-widget guest regression | Passed: 8 controls reported, 7 interactive clicks handled, no guest errors, and `DONE`; test-owned emulator stopped |
| Normal-mode startup diagnostics | Passed: a clean eight-widget no-request launch reported all controls and completed the first dialog-event cycle; no System error was reproduced |
| Interactive timeout telemetry extraction | Passed: the helper stopped Mini vMac and recovered flushed startup and first-event markers before reporting the expected missing test marker |

## Next

- System error 88 remains unreproduced. Continue investigating native mouse paths; the isolated runs exercised the mailbox interactions.
- Investigate System error 88 through native mouse paths; current automated runs use mailbox interactions and did not reproduce it.
- Improve screenshot isolation/clearing if the residual screen artifacts in the guest captures are reproducible in a clean single-process run.
- Obtain legally usable complete MAME firmware sets and verify both color guests, PowerBook LCD mapping, and a generated color application before enabling Build & Run for those profiles.

## Completion record

**Completed 2026-10-30:** The tracked Hello Mac sample built and its “Hello, Mac!” dialog was observed on the Mac SE/System 7.5.5 guest. The current designer-generated `DesignerForm` also builds as a 68k HFS disk. Its guest appearance and behavior were later verified on the emulator, as recorded below.

**Completed 2026-10-30:** The designer-generated `DesignerForm` built with Retro68 and launched automatically from the disposable System 7.5.5 Startup Items folder. Build & Run left the form visible without manual Finder interaction. Build & Test automatically launched the app, exercised a button through the mailbox, returned telemetry, and passed the IDE assertions. `deno task test:all` passed (9 Deno tests, 28 .NET tests, PowerShell and bridge-script syntax checks, and a WPF Release build with 0 warnings/errors). The mailbox remains batch/file-based; live serial IPC is not implemented.

**Completed 2026-10-30:** Designer grid, snap, multi-select, alignment, resize, and classic preview styling were implemented. A Retro68-built showcase ran through the Mac SE/System 7.5.5 guest loop; telemetry confirmed all eight widget types, seven handled interactive clicks, and clean emulator shutdown. All 47 Core tests and the isolated WPF Release build passed. The emulator test verified guest behavior and placement metadata; visual font/raster fidelity still needs manual comparison.

**Completed 2026-10-30:** Added project JSON persistence, a lazy file tree, AvalonEdit syntax highlighting, and a preserved editable `user_events.c` file. Project/event callback integration passed 53 Core tests, 9 Deno tests, the isolated WPF Release build, and a second Mini vMac showcase run with all eight widgets and seven handled clicks.

**Completed 2026-10-30:** Rebuilt the eight-widget showcase from clean generated sources and verified its full synthetic interaction loop on the Mac SE/System 7.5.5 guest. All eight controls were reported, seven interactive clicks were handled, popup/list/scrollbar interaction checks passed, and the guest returned `DONE`. A separate normal-mode launch without a test request showed the generated form without an error dialog.

**Completed 2026-10-31:** Fixed list selection reads by initializing the List Manager search cell and replaced full-dialog setter redraws with targeted dialog-item invalidation. Guest accessor round-trips passed, the Core suite passed (53 tests), and the clean eight-widget guest regression again reported eight controls, seven handled clicks, no errors, and `DONE`. The boot helper now attempts telemetry extraction after a test-marker timeout. A clean normal-mode eight-widget trace reached metadata and completed its first dialog-event cycle without reproducing the previously observed System error; direct visual inspection remains pending.

**Completed 2026-10-31:** Ran isolated Mac SE/System 7.5.5 Build & Test sessions for editable text, button, checkbox, radio button, popup menu, list box, and scrollbar. Each guest response contained exactly one control, reported `HANDLED` for its applicable click, and reached `DONE` without an error. Captured and reviewed each guest screen; the tested control was visible, but residual screen artifacts remain, so the captures are not a clean visual-fidelity pass. System error 88 was not reproduced.

**Completed 2026-10-31:** Added a separate Graphics palette with QuickDraw line, rectangle, circle/oval, and arc primitives. Rectangle, circle/oval, and arc support outline, solid, and dithered styles; arc angles are schema-validated. Added generated stable-ID macros and named bounds/text/value accessors, and routed generated click dispatch through those component IDs. The full Core suite passed (56 tests), the WPF Release build passed with 0 warnings/errors, the Retro68 graphics showcase built and returned 10 controls plus `DONE`, and the standard guest regression returned eight controls, seven handled clicks, and `DONE`. Screenshot review showed all graphics variants; some residual screen text/artifacts remain. System error 88 was not reproduced.

**Completed 2026-10-31:** Added a persisted, editable C form type name with backward-compatible loading of earlier schema-1 projects. Generated C now exposes `MacStudioRunForm()`; an editable `src/main.c` owns the entry point and `MacStudioFormDidLoad` hook, and is preserved on subsequent builds. The project creates editable `main.c` and `user_events.c` when missing.

**Completed 2026-10-31:** Added eight directional resize grips with matching cursors and independent draggable line endpoints. Line endpoint geometry persists as bounded percentage properties. Added a validated `MacStudioSetControlBounds` API plus named per-control setters; native controls, list boxes, scrollbars, and QuickDraw items now update their runtime geometry.

**Completed 2026-10-31:** Changed bridge metadata to report live bounds after the editable form-load hook. A Retro68/Mini vMac regression moved and read back a button, list, scrollbar, and line; the click callback changed the button text, telemetry reported the moved coordinates, and the guest returned `DONE`. Final validation passed all 60 Core tests, the WPF Release build (0 warnings/errors), and an IDE startup check. Screenshot review still shows residual desktop artifacts.

**Completed 2026-10-31:** Added a Delphi/C++Builder-style Events inspector. Interactive controls expose persisted `OnClick` bindings; Go/double-click creates or opens a preserved `user_events.c` handler, and generated dispatch calls the bound method while retaining the legacy generic callback for unbound controls. Handler signatures track form-type renames. The full Core suite passed (68 tests), the WPF Release build passed with 0 warnings/errors, and the IDE startup check passed.

**Completed 2026-10-31:** Added an extensible nonvisual component model, registry, and catalog for Standard File, menu items, serial ports, and a protocol-transfer component with XMODEM and ZMODEM choices. Component properties/events and protocol-to-serial references persist in schema-1 projects while older projects default to an empty component collection. The Core suite passed (76 tests); the WPF palette and generated Macintosh runtime are still in progress.

**Completed 2026-10-31:** Integrated the Components palette/tray, editable properties and event bindings, project save/load, native menu generation/dispatch, Standard File load/save APIs, and Serial Driver open/close/read/write plus polled data-available events. Serial buffers are now distinct per component; projects reject two components claiming the same physical port. Generated protocol APIs return `paramErr` and dispatch `OnError` until a transfer engine is implemented. Validation passed 81 Core tests, a Debug WPF build (0 warnings/errors), and a Retro68 build of a representative generated app containing file, menu, serial, and protocol components. Mini vMac's lack of serial passthrough prevents live serial-transfer verification.

**Completed 2026-10-31:** Replaced the two-row command toolbar with a Delphi-inspired tabbed ribbon for Home/build-run-test-stop, Components, and Design/grid/alignment. Ribbon and sidebar component selectors remain synchronized. Embedded Pictogrammers Material Design icon paths avoid a runtime theme dependency. The Debug WPF build passed with 0 warnings/errors.

**Completed 2026-10-31:** Replaced protocol-transfer stubs with generated synchronous XMODEM checksum/CRC/1K and single-file ZMODEM memory-buffer engines. Added block retry/cancel/timeout handling, negotiated ZMODEM CRC-32 with CRC-16 fallback, progress/completion/error events, serial auto-open, and ZMODEM filename validation. Added a printable-ASCII ZMODEM filename property. Host serial-loopback validated 300-byte XMODEM CRC and 1700-byte ZMODEM transfers; a generated project with both protocol components compiled and linked using Retro68 for 68k. Mini vMac has no serial passthrough; modem hardware interoperability remains unverified.

**Completed 2026-10-31:** Added root README environment setup instructions, a C/Retro68 language guide, protocol API/limitations documentation, and an in-IDE help viewer embedding the README and developer docs for offline use.

**Completed 2026-10-31:** Added horizontal scrolling to ribbon tabs and increased command button minimum widths to keep labels readable without group overlap. Added a Window menu for showing/hiding the Toolbox / Project Explorer, Properties / Events, and Build Output panes, with a restore-default-layout command. The Debug WPF build passed with 0 warnings and 0 errors; a narrow-window UI smoke check showed the ribbon labels remained aligned.

**Completed 2026-10-31:** Added candidate Macintosh Color Classic (512 × 384, 8-bit) and PowerBook 165c (640 × 400, 8-bit) profiles, with validated `#RRGGBB` QuickDraw graphics properties and target-aware WPF previews. Monochrome profiles retain their existing drawing behavior, and color properties are hidden outside color profiles. The Core suite passed (92 tests), the isolated WPF Release build passed with 0 warnings/errors, and Retro68 accepted the Color QuickDraw API syntax. MAME 0.289 recognizes both exact models and the local main ROMs match its definitions, but required CUDA firmware (Color Classic) and video/PMU firmware (PowerBook) are absent; no guest boot or color application launch is claimed, and Build & Run remains disabled for both profiles.
