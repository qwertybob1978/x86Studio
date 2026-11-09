# Running the feasibility spike

The spike harness is independent of the deferred IDE. Emulator binaries and test media stay local and are excluded from Git.

## Local checks

From the workspace root:

```text
deno task check
deno task test
```

On Windows, run the prerequisite probe from PowerShell:

```powershell
.\scripts\windows\verify-toolchain.ps1
```

It records a machine-readable snapshot at `feasibility/evidence/latest-toolchain-report.json`. Missing prerequisites are reported, not installed.

## Building Retro68 in WSL Ubuntu

The Retro68 toolchain is installed under the WSL user's home directory, not into the Git worktree. The first-time Ubuntu prerequisite installation, when needed, is:

```powershell
wsl.exe -d Ubuntu -u root -- apt-get update
wsl.exe -d Ubuntu -u root -- apt-get install -y --no-install-recommends `
  cmake libgmp-dev libmpfr-dev libmpc-dev libboost-all-dev bison flex texinfo ruby git
```

Run the reproducible project entrypoint from the repository root:

```powershell
wsl.exe -d Ubuntu -- bash scripts/wsl/build-retro68.sh
```

It clones the [Retro68 upstream source](https://github.com/autc04/Retro68), initializes its submodules, builds classic 68k and PowerPC toolchains without the optional Carbon target, and verifies both compilers and the `Rez` resource compiler under `~/retro68-build/toolchain/bin`. Retro68's `RIncludes` paths are generated include-resource directories, not executable tools. The build does not install or download a Macintosh ROM or system image.

## 68k Hello, Mac development loop

The WPF IDE's **Build & Run** command generates the current designer form and
routes it through the same build/boot workflow. To build the tracked Hello Mac
sample directly from Windows PowerShell:

```powershell
.\scripts\windows\build-and-run-mac-se.ps1
```

The default workflow builds the tracked `tutorial/samples/hello-mac` source
with Retro68 in WSL Ubuntu and writes an HFS application disk at
`.local-tools/retro68-hello-mac-build/HelloMac.dsk`. It extracts the source
System 7 disk's HFS partition to disposable boot media, installs the
application in Startup Items, and starts Mini vMac. The source disk and ROM
are not modified.

To run a generated Color QuickDraw form with Basilisk II, select **Run →
Basilisk II** in the IDE, choose a Color Classic or PowerBook 165c target, and
use **Build & Run**. The matching local ROM is selected by default; the
integrated run uses generic Basilisk II model ID 5 and does not claim exact
hardware emulation. To run the tracked Hello Mac sample directly under the
PowerBook profile, use:

```powershell
.\scripts\windows\build-and-run-mac-se.ps1 `
  -Emulator BasiliskII `
  -TargetProfileId mac-powerbook-165c-68k `
  -GuestScreenWidth 640 `
  -GuestScreenHeight 400
```

The Color Classic target uses `-TargetProfileId mac-color-classic-68k` and
512 × 384 dimensions. Build & Test remains limited to Mini vMac/Mac SE.

The matching launch-only command is available when an application disk has already been built:

```powershell
.\scripts\windows\boot-mac-se.ps1 `
  -DiskImagePath .\images\OS_755_2GB.dsk `
  -RomImagePath ".\images\256KB ROMs\1987-03 - B2E362A8 - Mac SE.ROM" `
  -AppDiskImagePath .\.local-tools\retro68-hello-mac-build\HelloMac.dsk
```

## Configured candidate run

1. Copy `profile-config.example.json` to a user-managed local JSON file and edit the selected profile. Keep the local file out of source control.
2. Set `build.command` and `build.args` to the exact approved local build command. For WSL, call `wsl.exe` with an explicit distribution and script path; do not embed a shell command string when an argument array will do.
3. Set `artifactPath` to the exact output path produced by the build. The same path must be visible to the Windows emulator.
4. Set `emulator.command` and `emulator.args` for the installed emulator. Include the literal `{artifact}` as exactly one argument. Point emulator preferences to system software/ROMs the user is legally entitled to use; do not place images or ROMs in this project.
5. Review the local commands, then run:

```powershell
.\scripts\windows\build-and-launch-hello-mac.ps1 `
  -Profile mac-se-68k `
  -ConfigPath .\feasibility\profile-config.local.json
```

The script stops if the build fails or the expected artifact is absent. It starts the configured emulator and records its process ID. It does **not** wait for, inspect, or claim a successful guest boot/application launch. Observe the emulator manually, retain appropriate evidence, and update the compatibility matrix only after that verification. Use a distinct `-EvidencePath` for each run if you need to preserve several records.

## Mac SE guest boot check

The verified System 7.5.5 disk was supplied by the user. The project keeps the source disk and ROM in `images/`, excluded from Git by the `*.dsk` and `*.ROM` rules. Do not replace or edit those originals during a test.

Mini vMac requires a Mac SE variation and the ROM filename `MacSE.ROM`. A whole Apple-partitioned disk is not a raw HFS volume, so run the helper to extract its single HFS partition into a disposable temporary folder:

```powershell
.\scripts\windows\boot-mac-se.ps1 `
  -DiskImagePath .\images\OS_755_2GB.dsk `
  -RomImagePath ".\images\256KB ROMs\1987-03 - B2E362A8 - Mac SE.ROM"
```

Inspect the emulated desktop, then close Mini vMac. The helper waits for the
emulator to close and removes temporary media. It validates the Apple
partition map and HFS signature; the Mini vMac/Mac SE path also checks the
tested SE ROM SHA-1. The observed System 7.5.5 desktop is captured at
`evidence/mac-se-system-7-5-5-desktop.png`.

The local Mini vMac 36.04 custom Windows x64 SE variation was generated using the [Gryphel variations service](https://www.gryphel.com/c/minivmac/var_serv.html); its demo overlay does not affect the boot check. Basilisk II and SheepShaver Windows archives are from the Windows download links on the [E-Maculation emulator pages](https://www.emaculation.com/doku.php/downloads). MAME 0.289 is from the [official MAME release](https://www.mamedev.org/release.html); using the Mac SE machine requires an additional 1 KB ADB modem ROM not present in the supplied media, so MAME has not booted this profile.
