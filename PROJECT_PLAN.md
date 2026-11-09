# FreeRTOS on MS-DOS: Project Plan

## Goal

Run FreeRTOS as an MS-DOS application with two targets:

| Iteration | CPU target | Display | Base memory | Extended memory |
|---|---|---|---|---|
| 1 | 8088-compatible | Hercules graphics | 640 KiB conventional | 4 MiB EMS |
| 2 | 80386 protected mode via DOS extender/DPMI | VGA graphics | 640 KiB conventional | 384 KiB XMS + 4 MiB EMS |

Both target profiles must first be tested in DOSBox-X with the PicoMEM-compatible configuration and driver providing 4 MiB EMS. Test physical PicoMEM hardware after both emulator profiles pass. The PicoMEM reference describes its EMS emulation as a Lo-tech-compatible board with up to 4 MiB; DOSBox-X also offers EMS-board emulation, but compatibility with the actual PicoMEM driver must be demonstrated rather than assumed.

The requested 384 KiB XMS is a capacity above the 1 MiB address boundary, not the physical address range from 640 KiB to 1 MiB. Keep DOSBox-X-visible conventional, XMS, and EMS capacities separate in configuration and test results.

## Current implementation status

- The 8088-compatible DOSBox-X baseline passes with XMS and EMS disabled: the scheduler runs for 36 ticks and DOS reports 640 KiB conventional memory.
- The isolated EMS-board stage also passes with XMS disabled: DOSBox-X reports 256 total 16 KiB EMS pages (4 MiB), and the scheduler still completes. DOSBox-X must use its 386 CPU model for this stage because its 8086 memory aliasing prevents EMS-board backing; the executable itself remains built for 8086.
- The EMS stage allocates one EMS page, maps it into the page frame, and has a running FreeRTOS task write/read it; it then releases the handle and checks the page count is restored. Kernel objects and task stacks still use conventional memory; this is not a general FreeRTOS heap.
- XMS support is explicitly deferred to future 80386 work.
- The EMS test uses DOSBox-X's generic EMS board, not the PicoMEM driver.
- Hercules graphics output, the PicoMEM EMS driver, and the 80386 target remain unvalidated.

## Phase 1 — Establish the baseline

1. Select and pin the FreeRTOS LTS kernel revision and a DOS compiler/toolchain. Evaluate the existing Borland and Open Watcom DOS ports before writing a new port:
   - `FreeRTOS-LTS/FreeRTOS/FreeRTOS-Kernel/portable/BCC/16BitDOS/PC/`
   - `FreeRTOS-LTS/FreeRTOS/FreeRTOS-Kernel/portable/oWatcom/16BitDOS/PC/`
2. Build the smallest DOS executable that starts the scheduler, runs two static tasks, exercises tick delays/yields, and returns cleanly to DOS.
3. Record executable format, compiler flags, memory model, stack/heap sizes, and the behavior of the existing timer/vector setup. Establish separate build profiles for the 8088 and 80386 targets; the protected-mode target needs its own compiler, extender/DPMI runtime, and port implementation.
4. Define a small display interface with separate Hercules and VGA implementations. Keep scheduler tests independent of display hardware.

**Exit criteria:** the selected kernel/port/toolchain builds reproducibly, the scheduler switches between tasks, and shutdown restores DOS timer/interrupt state.

## Phase 2 — Iteration 1: 8088-compatible DOSBox-X and Hercules

1. Add a project-owned DOSBox-X profile based on the bundled reference configuration: use Hercules graphics and the closest supported 8086 CPU mode for instruction-set compatibility. This does not model exact 8088 bus timing.
2. `dos-poc/8088/run-dosbox.bat` tests only 640 KiB of conventional memory with XMS and EMS disabled. `dos-poc/8088/run-dosbox.bat EMS` then enables EMS-board mode with XMS still disabled, verifies 4 MiB through the EMS API, and tests allocation, mapping, task access, and release of one page. The EMS profile uses 5 MiB plus 64 KiB of DOSBox-X backing memory to expose all 256 EMS pages. Then test the PicoMEM-compatible EMS driver/configuration; do not assume DOSBox-X's general `memsize` setting is the EMS size.
3. Build the low-end executable with compiler settings that prohibit 80186-and-newer instructions. Add a binary/disassembly compatibility check where the selected toolchain supports one.
4. Run the scheduler sample in DOSBox-X, display a task heartbeat/status in Hercules graphics mode, and verify delays, yields, timer tick behavior, memory use, and return to the DOS prompt.
5. Test with the PicoMEM-compatible EMS driver active in DOSBox-X, not just with generic EMS enabled. If DOSBox-X's EMS-board mode does not match the driver interface, resolve or explicitly document that emulator gap before claiming the target passes.

**Exit criteria:** the 8088-compatible binary runs in DOSBox-X with Hercules output, uses no instructions newer than the 8088/8086 ISA, fits within 640 KiB conventional memory, passes scheduler/timer checks, and the PicoMEM-compatible driver reports 4 MiB EMS.

## Phase 3 — Iteration 2: 80386 protected mode and VGA

1. Select the 32-bit compiler and DOS extender/DPMI runtime. Define how the port obtains timer interrupts, performs task switches, calls DOS services, and returns to DOS under that runtime.
2. Add a separate 80386 protected-mode port and build profile. Preserve a distinct 8088 artifact and its instruction-set checks.
3. Future work: configure and verify 640 KiB conventional memory and 384 KiB XMS capacity, separately from EMS. XMS must be reported through the XMS interface above 1 MiB; do not model the 640 KiB-to-1 MiB address range as XMS. Check the DOS/extender-visible memory map and ensure the extender and application fit.
4. Define how protected-mode code discovers and accesses 4 MiB EMS through the extender/DPMI interface. Do not treat EMS pages as a linear heap unless a mapping design explicitly provides that behavior.
5. Run the VGA display and scheduler checks in DOSBox-X with 386 CPU emulation, the selected DOS extender/DPMI runtime, and the PicoMEM-compatible 4 MiB EMS driver/configuration. Only after this profile passes, repeat applicable checks on a 386-class PC with PicoMEM.

**Exit criteria:** the 80386 protected-mode executable starts through the selected DOS extender/DPMI runtime in DOSBox-X with VGA output; 640 KiB conventional memory, 384 KiB XMS, and 4 MiB PicoMEM-compatible EMS are independently confirmed; and the 8088 build remains independently reproducible.

## Phase 4 — PicoMEM integration and regression

1. Confirm the PicoMEM model/firmware, EMS driver, page-frame configuration, and DOS version used by each physical target. The PicoMEM reference describes up to 4 MiB of EMS and includes DOS-side EMS/driver code.
2. After both DOSBox-X profiles pass, repeat the EMS driver, page-frame, capacity, and scheduler tests on physical PicoMEM hardware. Detect and report EMS availability; do not access PicoMEM-specific ports or memory windows merely because DOSBox-X EMS is enabled.
3. Repeat scheduler, DOS timer restoration, Hercules/VGA, conventional-memory, XMS, and EMS tests on the intended hardware. Record emulator and hardware results separately.

## Remaining technical risks

- Verify that the selected DOS extender/DPMI runtime can operate with 640 KiB conventional plus 384 KiB XMS and still provide the required application memory.
- Verify that DOSBox-X EMS-board emulation works with the PicoMEM EMS driver/configuration and exposes 4 MiB through the interface the protected-mode application uses. Confirm on physical PicoMEM hardware after emulator tests.
- Confirm which DOSBox-X configurations can run the selected 32-bit DOS extender/DPMI runtime before making emulator execution a required 80386 acceptance test.

## Reference implementation notes

- The FreeRTOS DOS ports install a scheduler tick on the DOS timer interrupt and use a software interrupt for yielding. Preserve DOS interrupt/timer state when starting and stopping the scheduler.
- `ISA-PicoMEM/src/drivers/PMEMM/` and `ISA-PicoMEM/src/isa_devices/` are useful DOS/ISA references; `ISA-PicoMEM/src/` also contains RP2040/RP2350 firmware, which is a separate target.
- The bundled DOSBox-X default configuration uses SVGA and more memory than the first iteration requires. Keep iteration-specific settings in project-owned config files.
