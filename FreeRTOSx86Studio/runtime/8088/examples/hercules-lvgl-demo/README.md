# Hercules LVGL Demo

This saved FreeRTOSx86Studio project targets the 8088-compatible DOS /
Hercules profile at 720 × 348 and 1 bpp. Its form demonstrates a label,
button event binding, checked checkbox, outlined rectangle and circle, a
2-pixel diagonal line, and LVGL LED, menu, and meter widgets. The generated
LVGL v8.3 task, event files, widget configuration, and source manifest let the
project build without first opening the designer.

Open `HerculesLvglDemo.x86gui` in FreeRTOSx86Studio to edit the screen
visually. Save the project after editing so the IDE refreshes the generated
task and event files. To build and run the checked-in sample from PowerShell:

```powershell
.\runtime\8088\build-and-run.ps1 `
  -ProjectDirectory .\runtime\8088\examples\hercules-lvgl-demo
```

Run the bounded DOSBox-X smoke test with:

```powershell
.\runtime\8088\build-and-run.ps1 `
  -ProjectDirectory .\runtime\8088\examples\hercules-lvgl-demo `
  -SmokeTest
```

The generator regression matrix can be run from the `FreeRTOSx86Studio`
directory:

```powershell
dotnet test .\tests\ClassicMacIde.Core.Tests\ClassicMacIde.Core.Tests.csproj `
  --filter FullyQualifiedName~FreeRTOSx86GuiCodeGeneratorTests
```

It exercises every catalog widget against every DOS display profile and checks
the generated LVGL constructor, headers, source manifest, feature macros, and
color depth. This verifies generation only; CGA and VGA remain design/codegen
profiles and are not runtime-buildable until their display backends and target
ports are implemented.

The emulator profile uses an 8086 CPU model, 640 KiB conventional memory,
Hercules graphics, and disables XMS and EMS. The graphics renderer and mouse
driver are isolated behind the runtime API; this sample does not claim EMS
framebuffer support or serial-mouse hardware verification.

The Hercules renderer uses both 32 KiB display pages as a synchronized
double buffer. LVGL updates the hidden page, flips it after the last dirty
area, and copies only the combined dirty bounds back to the other page. Its
8-row LVGL draw buffer stays in conventional memory, avoiding a full-screen
RAM framebuffer; byte-aligned spans use a packed assembly blit path.
