# FreeRTOSx86Studio Porting Plan

## Goal

Create a separate, copyable Windows WPF IDE based on `ClassicMacStudio`. The
new IDE edits a GUI visually, then generates a C application whose GUI loop is
a FreeRTOS task running in the MS-DOS port. The first runnable target is an
8088-class DOS application using Hercules graphics. The editor also has
8088/CGA design profiles, and the architecture must admit a separate
80386/VGA target without coupling the editor to a specific graphics adapter.

## Copy and preservation

- Copy the complete `ClassicMacStudio` project tree into `FreeRTOSx86Studio`,
  including its app, core, tests, documentation, tutorials, feasibility
  material, and license notices.
- Do not copy the nested `.git` metadata or generated `bin`, `obj`, and build
  output directories. Do not modify the source `ClassicMacStudio` project.
- Rename the visible product and project metadata to `FreeRTOSx86Studio`;
  preserve reusable designer/model behavior and applicable third-party notices.

## Current status (updated 2026-11-09)

### Implemented

- The WPF IDE has a designer, project model, LVGL widget catalog, C code
  generation, configurable grid/snap, object resize handles, and a
  Build & Run workflow for the 8088/Hercules sample.
- The target catalog contains five profiles: 8088/Hercules, CGA 320 × 200
  Palette 0, CGA 320 × 200 Palette 1, CGA 640 × 200, and 80386/VGA. Only
  Hercules is currently enabled for building and running; CGA and VGA are
  designer/code-generation profiles, not runtime support.
- A regression matrix exercises every catalog widget against all five target
  profiles (205 widget/profile generation cases). This verifies generated
  source shape, not that every widget runs on every target.
- The designer maps native-pixel geometry onto a 960 × 720, 4:3 preview.
  Hercules and CGA color properties offer 8 × 8 ordered-dither mixtures;
  palette choices and widget mockups are preview-only. See the
  [display preview notes](docs/dos-display-preview.md).
- The 8088/Hercules LVGL 8.3 path has a partial-buffer renderer and assembly
  flush. The generated FreeRTOS GUI task has been built and smoke-tested in
  DOSBox-X with 640 KiB conventional memory. The latest IDE build passed with
  zero warnings/errors and the Core suite passed 391 tests.
- The ribbon already separates LVGL tools into functional tabs. A row-resizing
  `GridSplitter` already adjusts the Build Output pane's height.

### Not yet implemented or verified

- Runtime CGA rendering and CGA dithering are not implemented. Hercules
  dithering is also not implemented in the DOS runtime; the HGC runtime
  remains monochrome. The 80386/VGA protected-mode runtime is not implemented.
- The complete PicoMEM memory profiles have not been verified in DOSBox-X:
  the 8088 profile requires 640 KiB conventional memory plus 4 MiB EMS, and
  the 80386 profile requires 640 KiB conventional memory, 384 KiB XMS capacity,
  and 4 MiB EMS. EMS must be supplied by the PicoMEM-compatible driver/config
  in DOSBox-X `ems=emsboard` mode and must report the required 4 MiB; generic
  EMS availability is not sufficient. XMS and EMS are separate resources.
- The IDE still exposes generated `gui_task.c` as a read-only editor tab.
  It does not yet present the program as a standalone application while
  hiding its internal FreeRTOS task implementation.
- Widget palette entries are currently text-oriented rather than icon cards
  with compact captions. The functional tabs exist, but the complete
  icon-and-caption presentation for every catalog widget remains to be done.
- The property pane currently offers a manually editable `OnClick` handler
  field. Clicking an event to create/open a Borland C++Builder 6-style handler
  snippet in the code editor is not implemented.
- Build Output height can be adjusted with its row splitter. Resizable layout
  for the other tool panes and validation of practical pane limits/layout
  behavior remain open.
- Hardware compatibility, serial-mouse behavior, and complete memory-profile
  validation remain unverified. A widget-generation test is not a substitute
  for DOSBox-X build/run testing of each supported runtime target.

### Remaining IDE interaction work

1. Render every LVGL widget as a Material icon with a small title below it.
   Keep the ribbon split by functional category (for example Controls, Data,
   Containers, Media, and Drawing) and ensure each catalog entry appears in
   the appropriate tab.
2. Replace the manual event-name workflow with an event inspector: selecting
   or activating an event should create its handler skeleton once, open the
   editable application code at that handler, and bind the event. Preserve
   existing user code and avoid duplicate or stale handler definitions.
3. Hide `gui_task.c` and other RTOS orchestration details from the user's
   project/editor surface. Let the user edit the program and event handlers as
   an application; generate and build the FreeRTOS task wrapper internally.
4. Make the tool panes resizable with usable minimum sizes. Keep the existing
   Build Output splitter, provide equivalent resizing where needed, and test
   that resizing does not disrupt the designer/code workspace. Persist pane
   sizes only if the existing layout mechanism supports it cleanly.
5. Add UI regression checks for icon/catalog coverage, event-handler
   generation and persistence, hidden task implementation, and pane resizing.

## Version 1 architecture

1. **Target model:** describe CPU, display backend, resolution, color depth,
   memory facilities, and build/run capability in target profiles. The current
   catalog has `8088 + Hercules (720 x 348, 1 bpp)`, three 8088/CGA variants,
   and `80386 + VGA`. Only Hercules is build-supported. The project resource
   budgets are 640 KiB conventional memory and 4 MiB PicoMEM-compatible EMS
   for 8088, and 640 KiB conventional memory, 384 KiB XMS capacity, and 4 MiB
   EMS for 80386. The designer and project format consume profile-neutral
   capabilities, not graphics-card-specific APIs.
2. **WYSIWYG editor:** reuse the copied designer's form model, canvas,
   selection, drag/resize, properties, event bindings, project persistence,
   and C editor. Use a 720 x 348 target canvas and a DOS-oriented widget and
   graphics palette.
3. **Generated application:** generate a user-facing application model and
   event hooks, then wrap its GUI loop in an internal
   `vFreeRTOSx86GuiTask(void *)` task. Do not expose `gui_task.c` or require
   users to think of their application as an RTOS task. The RTOS kernel, task
   control blocks, and task stacks remain in conventional memory for the first
   version.
4. **Graphics boundary:** generated C calls a stable `gui_*`/`hgc_*` C API.
   An 8088 assembly module owns Hercules mode/port access and implements the
   pixel, line, rectangle, and circle primitives. Declare those assembly
   entry points `__cdecl` so Open Watcom passes arguments on the stack as the
   assembly parameter frames expect. The future VGA profile will provide a
   separate backend behind the same drawing contract.
5. **Hercules mode and buffering:** enter 720 x 348 graphics mode and draw
   through the HGC page frame at B000h. Prefer the card's two 32 KiB graphics
   pages for hardware page flipping when page 1 is supported. The verified
   LVGL path uses a small partial buffer and assembly flush; it does not assume
   page flipping or EMS. EMS is optional: use it only as a mapped off-screen
   image/cache when the available HGC page memory is insufficient; do not
   expose EMS as a flat pointer or FreeRTOS heap.
6. **Mouse:** use the Microsoft-compatible DOS mouse API (INT 33h), so an
   installed driver can adapt a serial mouse to the GUI without embedding a
   specific UART protocol in widgets. Keep mouse access behind the same GUI
   input boundary so a future target can replace it.
7. **Build/run:** add a reproducible Open Watcom build path for the generated
   task, 8088 assembly backend, and existing FreeRTOS DOS port; launch and
   collect a result from the DOSBox-X Hercules profile.

## LVGL and display roadmap

- The WYSIWYG preview already maps native target pixels to a 4:3 display face.
  Its 8 × 8 ordered-dither color mixtures are design-time only; do not describe
  them as DOS runtime rendering.
- The DOS runtime uses the official `lvgl/lvgl` upstream `release/v8.3` source
  at commit `fbb73d4b3ffe086a7218bd1a8e51f9e7a260c4b9`; it does not depend on
  the linked fork. A Watcom 16-bit probe of 344 selected v9.6.0 core, draw,
  font, input, and widget sources failed on 30 files before runtime integration;
  v9 also replaces both display and input driver APIs and uses 32-bit object
  flags that need a 16-bit compiler audit. Keep v8.3 for the working 8088
  target; revisit v9 only with a focused compiler compatibility port and full
  DOSBox-X verification.
- The v8.3 renderer stores one byte per monochrome pixel in a 720 x 8 partial
  buffer. The HGC flush path uses 8086 assembly to pack bits into the card's
  90-byte interleaved scanlines, preserving untouched edge bits; C clips each
  LVGL area and dispatches its rows. Keep the full-screen surface out of
  conventional memory.
- Keep LVGL-facing screens and generated widgets independent of adapter
  operations. The first target provides LVGL display/input drivers over the
  Hercules API and a monochrome WYSIWYG preview. Other adapters must preserve
  the screen project model and widget-generation boundary.
- CGA profiles already exist for 320 × 200 Palette 0, 320 × 200 Palette 1,
  and 640 × 200 monochrome, and participate in the widget-generation matrix.
  Implement runtime rendering separately. For CGA color, use palette-aware
  ordered dithering with a precomputed RGB332-to-palette-pair/coverage lookup;
  for 640 × 200, use the monochrome path. Keep the Bayer pattern anchored to
  absolute screen coordinates so partial refreshes remain stable. Avoid
  per-pixel palette searches, floating point, and error diffusion on the 8088.
  Do not mark a CGA profile build-supported until it has a target backend and
  DOSBox-X verification. Additional text modes remain future work.
- Treat VGA and VESA as 80386-only backends. Keep their mode selection,
  framebuffer access, and any extender/DPMI requirements out of the 8088
  runtime. The current VGA profile is code-generation-only. Build a distinct
  protected-mode/DPMI port and verify its complete memory and graphics profile
  in DOSBox-X before hardware testing.

## Implementation stages and acceptance

1. Copy the full project and establish a buildable, distinctly named
   `FreeRTOSx86Studio` solution without changing `ClassicMacStudio`.
2. Add the target/backend model, HGC widget palette, 720 x 348 designer
   profile, and versioned DOS project files. Confirm form save/load preserves
   target and geometry.
3. Implement and unit-test deterministic GUI task generation for supported
   controls and graphics. Generated C must call only the backend's public C
   API and expose user event hooks.
4. Implement the 8088 HGC assembly driver and C abstraction. Verify its
   coordinate bounds, MSB-first pixel masks (`0x80 >> (x & 7)`), page
   selection, and primitive output against the HGC memory layout; build with
   8086/8088-compatible instructions. A DOSBox-X screenshot confirms readable
   glyphs and circular output after correcting the pixel bit order.
5. Integrate the pinned LVGL monochrome renderer, partial HGC flush driver,
   INT 33h pointer input, WYSIWYG widgets, and generated FreeRTOS GUI task.
   Verify rendered output and clean DOS return in DOSBox-X; keep the v9.6
   upgrade gated on its separate 8088 compatibility and runtime checks.
7. Verify the complete DOSBox-X memory profiles: 640 KiB conventional memory
   and 4 MiB PicoMEM-compatible EMS on both targets, plus 384 KiB XMS capacity
   for 80386. First confirm that `ems=emsboard` works with the PicoMEM driver
   and reports 4 MiB; generic EMS is not sufficient. EMS use by the RTOS may
   remain optional. If used for mapped image/cache storage, test explicit
   allocate/map/copy/unmap operations rather than assuming a linear
   framebuffer. Verify hardware page flipping separately.
8. Implement and verify the existing CGA 320 × 200 Palette 0, Palette 1, and
   640 × 200 profiles, including palette-aware runtime dithering and
   DOSBox-X regression. Add further CGA text modes only as explicit profiles.
   Then implement VGA/VESA as 80386-only profiles with a separate
   protected-mode/DPMI backend; preserve the designer schema and generated GUI
   API.
9. Complete the application-oriented editor experience described above:
   icon-and-caption widget ribbon, event-to-handler generation, hidden RTOS
   task sources, and resizable tool panes. Keep these changes independent of
   target-specific rendering and verify them with UI regression tests.

## External hardware/API references

- Reid Brown's Hercules programming notes document ports 03B8h/03BFh, the
  720 x 348 mode, 90-byte rows, and the four 2000h scanline banks:
  <https://gist.github.com/bocke/21d4acd8993a7d8812f7a31c3c558300>
- FreeDOS mouse's INT 33h reference lists initialization and position/button
  functions and notes the Hercules graphics-mode interaction:
  <https://github.com/FDOS/mouse/blob/master/int33.lst>

These references guide the implementation; DOSBox-X and hardware tests remain
the acceptance evidence.

## Known constraints

- HGC graphics are monochrome and expose pixels, not built-in rectangle,
  circle, or line operations; those primitives belong in the 8088 backend.
- EMS pages are window-mapped. Normal FreeRTOS pointers, stacks, and heaps must
  not refer to an EMS page that can be unmapped or remapped.
- A DOS INT 33h driver is a runtime dependency for a serial mouse. Mouse
  availability must be detected and failure must not prevent drawing.
- A DOSBox-X EMS-board test is not evidence that the PicoMEM driver works.
