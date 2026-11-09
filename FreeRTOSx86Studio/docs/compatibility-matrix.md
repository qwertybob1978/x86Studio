# Classic Macintosh Feasibility Matrix

**Last updated:** 2026-10-31  
**Overall result:** Mac SE sample and generic Basilisk II color smoke apps launched; exact Color Classic and PowerBook hardware compatibility remains unverified.

The LC III and Power Macintosh profiles are hypotheses copied from the approved plan. The Mac SE row records a reproducible 68k sample build and a user-observed HelloMac launch. Color Classic and PowerBook rows record generated QuickDraw smoke apps running under Basilisk II's generic Mac IIci model ID 5 with the respective ROMs; this is not exact machine emulation and does not establish general application compatibility.

## Candidate profiles

| Candidate | CPU | System software | Display | Compiler/resource tools | Emulator | Build | Guest boot / app launch | Result |
|---|---|---|---|---|---|---|---|---|
| Macintosh SE | 68k | System 7.5.5 (user-supplied image) | 512×342, 1-bit | Retro68 68k; HelloMac built with GCC 16.1.0 and Rez | Mini vMac 36.04, custom Windows x64 `-m SE` variation | Passed; HFS app disk SHA-256 `8cad665941c8f77528380ce8304b479d948595a6fa589302790c44743d32aea8` | Finder boot observed; “Hello, Mac!” dialog captured from the mounted app disk | HelloMac sample verified on this setup; broader application compatibility not yet tested |
| Macintosh Color Classic | 68k | System 7.5.5 (user-supplied image) | 512×384, 8-bit window | Retro68 68k; generated Color QuickDraw smoke app built | Basilisk II 2025-01-24, generic model ID 5, local Color Classic ROM | Passed; generated app disk | Red, dithered blue/cyan, and green QuickDraw shapes visible in the guest | Generic Basilisk II color app run verified; exact Color Classic hardware remains a candidate |
| PowerBook 165c | 68k | System 7.5.5 (user-supplied image) | 640×400, 8-bit window | Retro68 68k; generated Color QuickDraw smoke app built | Basilisk II 2025-01-24, generic model ID 5, local PowerBook ROM | Passed; generated app disk | Red, dithered blue/cyan, and green QuickDraw shapes visible in the guest | Generic Basilisk II color app run verified; exact PowerBook hardware and LCD mapping remain candidates |
| Macintosh LC III | 68k | System 7.1.1 candidate | 640×480, color candidate | Retro68: not installed | Basilisk II 24-01-2025 available locally; profile not configured | Not run | Not run | Candidate; unverified |
| Power Macintosh 6100 | PowerPC | System 7.5.5 candidate | 640×480, color candidate | Retro68: not installed | SheepShaver 24-01-2025 available locally; profile not configured | Not run | Not run | Candidate; unverified |

The LC III and Power Macintosh model/display values remain hypotheses. The SE boot used the user-supplied SE ROM (SHA-1 `58532b7d0d49659fd5228ac334a1b094f0241968`) and a temporary copy of the user's disk. The ROM matches the MAME Mac SE ROM definition. The source disk checksum was SHA-256 `4b7d468906b65c5814e96f0326d12ca3af7ece61c1edfa0d1a9a898681482bbc`; it was not modified.

Boot evidence: [Mac SE System 7.5.5 Finder desktop](../feasibility/evidence/mac-se-system-7-5-5-desktop.png). Mini vMac's visible Demo overlay is a free-variation notice; the Finder desktop and mounted `2GB 7.5.5` volume are visible.

Application evidence: [HelloMac running in the Mac SE/System 7.5.5 guest](../feasibility/evidence/mac-se-hello-mac.png). The screenshot shows the dialog after opening the application from the mounted second disk; it was captured from the emulator window on the secondary monitor.

Color QuickDraw evidence: the generated smoke app used the production form
generator and rendered red, dithered blue/cyan, and green graphics in Basilisk
II with the local Color Classic and PowerBook ROMs. Basilisk II's active model
was ID 5 (Mac IIci), not either exact target model. Screenshots:
[Color Classic](../feasibility/evidence/basilisk-color-classic-quickdraw.png) and
[PowerBook 165c](../feasibility/evidence/basilisk-powerbook-165c-quickdraw.png).
Default ROM paths:

- Color Classic: `images\1MB ROMs\1993-02 - ECD99DC0 - Color Classic.ROM`
- PowerBook 165c: `images\1MB ROMs\1992-10 - E33B2724 - Powerbook 160 & 165 & 165c & 180 & 180c.ROM`

## Host and tool observations

Collected on the current Windows host by `scripts/windows/verify-toolchain.ps1`; machine-readable details are in `evidence/latest-toolchain-report.json`.

| Area | Observation | State |
|---|---|---|
| Deno | 2.9.7 on Windows x64 | Available; `deno task test` runs |
| .NET SDK / WPF | .NET 10 SDK and Windows Desktop runtime installed | WPF app build passed |
| WSL | WSL 2.6.3.0; Ubuntu, Kali Linux, and Docker Desktop distributions listed | Available |
| Rustup / rustc / Cargo in Ubuntu and Kali | Not found | Blocked |
| Retro68 68k and PowerPC compilers | GCC 16.1.0 under `~/retro68-build/toolchain/bin` | Available; 68k sample build verified; PowerPC application not tested |
| Retro68 resource compiler | `Rez` under `~/retro68-build/toolchain/bin` | Available; Hello Mac resource compilation verified |
| Basilisk II | Windows x64 build dated 2025-01-24, stored in ignored `.local-tools/` | Color QuickDraw apps launched with generic model ID 5; LC III and exact color hardware profiles not verified |
| SheepShaver | Windows x64 build dated 2025-01-24, stored in ignored `.local-tools/` | Available; PowerPC profile not run |
| Mini vMac | 36.04 custom Windows x64 SE variation, stored in ignored `.local-tools/` | Available; System 7.5.5 Finder boot observed |
| MAME | Official MAME 0.289 Windows x64 release; executable SHA-256 `a1aa7912168c9d1b05e611906bc21b8b9be3935822aead36d12a1da363150b7d` | Lists both color models; `-verifyroms` reports required model firmware absent. Mac SE also lacks ADB modem ROM `342s0440-b.bin`. |
| System image / ROM | User supplied a 1.84 GiB Apple-partitioned disk and Mac SE ROM; originals remain in ignored `images/` | Used only as temporary test copies; source hashes recorded above |
| WPF desktop application build | .NET 10 WPF Release build | Passed; 0 warnings and 0 errors |

The Docker Desktop WSL distribution has no `bash` executable and is recorded as **unknown**, not as evidence that a tool is absent from other distributions.

## Evidence required per profile

For each retained profile, record:

1. Exact compiler, linker, resource compiler, versions, and reproducible Windows-triggered build command.
2. The resulting application/package or emulator-readable disk artifact and its checksum.
3. Emulator version, host OS, selected machine model, configuration, and legally sourced system software used.
4. A confirmed guest boot and visible application launch, with date and human-observed or otherwise trustworthy evidence.
5. Display dimensions/mode, any warnings or exclusions, and steps to reproduce.

The configured build runner records build completion and emulator process start separately; it deliberately does not claim that a guest booted. The Mac SE boot and Hello Mac application launch were observed and recorded above. Color Classic and PowerBook smoke-app runs verify color rendering in generic Basilisk II model ID 5 only; exact-model behavior, LC III, and Power Macintosh runs remain unverified. Local Color Classic and PowerBook main ROM images match MAME's definitions, but the additional firmware needed by MAME's exact models is absent.

## Safe handling and exclusions

- Emulator binaries are downloaded into the Git-ignored `.local-tools/` folder. User-supplied ROMs and disk images stay in the Git-ignored `images/` folder; tests use temporary copies and clean them on exit.
- Only the locally supplied Mac SE, Color Classic, and PowerBook ROMs and the user-supplied System 7.5.5 image were used for the observed runs. No third-party system image or ROM was downloaded.
- The runner executes only commands explicitly supplied in that local configuration, as the current Windows user. Review those commands before running them.
- No compatibility claims are made by this spike's tool-presence probe or automated fixture tests.
