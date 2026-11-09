# DOS FreeRTOS Workspace

## Project goal

The goal is to run FreeRTOS under MS-DOS with two distinct targets:

- Iteration 1: an 8088-compatible DOSBox-X guest with Hercules graphics, 640 KiB conventional memory, and 4 MiB EMS exposed through the PicoMEM-compatible configuration/driver.
- Iteration 2: an 80386 DOS target using 32-bit protected mode through a DOS extender/DPMI, with 640 KiB conventional memory, 384 KiB XMS capacity, VGA graphics, and 4 MiB EMS exposed through the PicoMEM-compatible configuration/driver.

Test both complete target profiles in DOSBox-X before testing on physical hardware. Start by checking whether DOSBox-X's `ems=emsboard` mode works with the PicoMEM EMS driver/configuration and reports 4 MiB; generic EMS availability alone is not sufficient. The directories in this workspace are references; the DOS FreeRTOS application and its unified build/test setup have not yet been established. Put new project code beside the reference directories, not inside them, unless a task explicitly calls for changing a reference.

Keep the 8088 target free of instructions and assumptions that require newer CPUs. Keep CPU-specific code and build settings explicit for each target. The 80386 protected-mode target needs a separate port/toolchain path; do not treat the existing 16-bit DOS ports as a drop-in 32-bit port. Treat conventional memory, XMS, and EMS as separate resources and verify each from the guest. XMS is memory above 1 MiB; the requested 384 KiB is an XMS capacity, not the physical address range between 640 KiB and 1 MiB.

## Architecture and references

- `FreeRTOS-LTS/` is the AWS LTS collection. The kernel is in `FreeRTOS-LTS/FreeRTOS/FreeRTOS-Kernel/`; its compiler-specific ports live under `portable/`. Start the 8088 target from the existing 16-bit DOS PC ports in `portable/BCC/16BitDOS/PC/` and `portable/oWatcom/16BitDOS/PC/`. Their `port.c` files handle DOS timer/interrupt-vector integration, while the matching port macro headers define compiler- and CPU-specific scheduler primitives. The 80386 protected-mode target requires a separate port and DOS extender/DPMI integration. See the [FreeRTOS LTS README](FreeRTOS-LTS/README.md).
- `ISA-PicoMEM/` separates RP2040/RP2350 firmware in `src/` from DOS-side drivers and utilities in `src/drivers/` and `drivers/`. Use its DOS code as a reference for real-mode drivers and hardware interaction; do not treat its Pico firmware as code for the PC target. See the [PicoMEM README](ISA-PicoMEM/README.md) and its [firmware build workflow](ISA-PicoMEM/.github/workflows/build.yml).
- `dosbox-x/` is a DOSBox-X Windows distribution with executables and configuration/assets, not a source checkout. Use separate project-owned configs for the 8088/Hercules and 80386/VGA profiles; test the PicoMEM EMS driver/configuration in DOSBox-X EMS-board mode before hardware testing. Do not overwrite the supplied distribution config. See [DOSBox-X README](dosbox-x/README.txt) and the bundled configuration reference.

## Build and test

There is no workspace-wide build, test, or lint command yet. Use a reference project's own workflow when changing or validating that reference.

- **FreeRTOS coreJSON unit-test reference** (run from `FreeRTOS-LTS/FreeRTOS/coreJSON/` in a checkout with Git metadata):

  ```sh
  git submodule update --checkout --init --recursive test/unit-test/Unity
  cmake -S test -B build
  cmake --build build
  ctest --test-dir build
  ctest --test-dir build -R '^core_json_utest$'
  ```

  The last command runs the single registered coreJSON unit-test target. The test setup requires CMake 3.13+, a C90-capable compiler, Ruby, and the Unity submodule; see `FreeRTOS-LTS/FreeRTOS/coreJSON/README.md`.

- **PicoMEM firmware reference**: initialize its recursive submodules and external Pico SDK/toolchain as described in `ISA-PicoMEM/.github/workflows/build.yml`, then configure/build from the workspace root:

  ```sh
  cmake -S ISA-PicoMEM/src -B ISA-PicoMEM/build -DCMAKE_BUILD_TYPE=Release
  cmake --build ISA-PicoMEM/build --config Release --parallel
  ```

  This builds Pico firmware, not the DOS FreeRTOS targets. The workflow is the source of truth for SDK, compiler, and Python dependency versions; no corresponding test command is configured there.

- **DOS driver assembly reference**: `ISA-PicoMEM/src/drivers/PMMOUSE/Makefile` builds `pmmouse.exe` by default. From that directory, run `make` with JWasm and `com2exe` available. Its default assembler flag targets 8088/8086; its commented `-2` alternative targets 286 and newer, so treat it only as an example and do not use it to define the new 80386 target.

## Porting conventions

- Keep the FreeRTOS kernel core separate from each port's scheduler/interrupt implementation and compiler-specific definitions. Prefer adapting the existing DOS ports over duplicating scheduler logic.
- DOS tick handling must coexist with the existing DOS timer service; preserve and restore interrupt vectors and timer state when the scheduler exits. Validate this behavior in DOSBox-X and, where possible, on compatible hardware.
- Keep the 80386 protected-mode interrupt, timer, and DOS-service transitions behind its own DPMI/extender-specific port layer; do not reuse real-mode interrupt-vector code without validating the extender contract.
- Keep DOS-host code, PicoMEM's microcontroller firmware, and emulator tooling as distinct layers. A firmware build does not validate DOS driver or scheduler behavior.
- Keep Hercules and VGA output behind target-specific display code so the kernel and scheduler tests do not depend on either adapter.
- Treat EMS as bank-switched memory, not a contiguous extension of conventional RAM. Keep the kernel and active task state in conventional memory until an EMS-backed allocation/context design is explicitly implemented and tested.
- Do not silently substitute DOSBox-X's generic EMS for the required PicoMEM-compatible 4 MiB EMS profile; verify the selected driver, page frame, and reported capacity in both emulator targets.
