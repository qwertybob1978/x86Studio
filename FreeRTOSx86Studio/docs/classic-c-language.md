# C for Classic Macintosh with Retro68

## Toolchain and target

The IDE emits C source, Rez resources, and a CMake project. The current
Build & Run path selects `m68k-apple-macos-gcc` from Retro68 and produces a
68k Macintosh application. Rez compiles the resource source. The toolchain is
built by `scripts/wsl/build-retro68.sh` under the WSL user's home directory.

The project does not pin an explicit `-std=` language flag. The active GCC
release's default C dialect therefore applies; the exact compiler version can
vary with the Retro68 checkout. Generated code is conventional C and uses
Retro68's Macintosh Toolbox headers. Do not assume C++ support, a modern
macOS SDK, POSIX APIs, or a particular C standard-library feature set on the
System 7 guest. For portability, prefer straightforward C supported by the
selected Retro68 compiler and classic Toolbox interfaces, and verify any
additional library call by compiling for 68k.

## Application entry points

The generated `main.c` calls `MacStudioRunForm()`. The generated form source
creates and runs the dialog. Keep user-owned form startup and widget-event
logic in `user_events.c`; generated files can be regenerated. The IDE preserves
the user source and event bindings.

```c
#include "designer_form.h"

int main(void)
{
    return MacStudioRunForm();
}

void MacStudioFormDidLoad(MainForm form)
{
    (void)form;
}

void MacStudioUserControlClicked(
    MainForm form, const char *controlId, short item)
{
    (void)form;
    (void)controlId;
    (void)item;
}
```

The generated header declares only callbacks that the current project needs.
Use those actual declarations rather than copying an unused sample callback.

## Components and C conventions

- Include `designer_form.h` to access generated declarations and stable-ID
  macros.
- Use generic accessors for dynamic IDs; named accessors are generated for
  controls in the current project.
- IDs and C identifiers are distinct: generated C symbols encode punctuation
  from designer IDs. Keep user-defined C form names within the generated
  identifier constraints.
- Event handlers receive the generated form/dialog, stable control ID, and
  dialog item number. Prefer stable IDs over item numbers because reordering
  widgets can change item numbers.
- Toolbox routines use classic Macintosh types such as `DialogPtr`, `OSErr`,
  `Rect`, `Boolean`, and Pascal strings. Check return codes such as `noErr`
  and `paramErr`.
- Project-generated APIs are declared in `designer_form.h`; consult the
  [developer manual](developerManual.md) for control, file, menu, serial,
  and transfer component signatures.

## Macintosh-specific constraints

The verified profile is a 512 × 342 monochrome Mac SE running System 7.5.5.
Resource text currently supports printable ASCII and is limited to 255 bytes.
Do not assume color QuickDraw, Appearance Manager, Carbon, modern framework
APIs, large-memory models, or later operating-system services on that target.

The generated resources use Dialog, Control, List, Menu, and QuickDraw
Toolbox services. The target's memory, CPU, screen, resource, and file-size
limits are far below those of a modern Windows build host. Prefer bounded
buffers and explicit size checks; receive APIs report `memFullErr` rather than
writing past the caller's capacity.

## Build and debug

Build from the IDE or run the sample build script. Compiler and linker
diagnostics are shown in the IDE's build output. Retro68 compilation confirms
the 68k ABI and Toolbox declarations at compile/link time; it does not prove
the guest will render correctly. Run the application in Mini vMac or on
hardware to test its actual UI and callbacks.

Mini vMac in the current setup cannot forward serial data. Serial and file
transfer code should be tested with physical serial devices or an emulator
configured with a serial backend. The generated protocol runtime has host
loopback and cross-compilation validation, but real-modem interoperability
still requires hardware testing.
