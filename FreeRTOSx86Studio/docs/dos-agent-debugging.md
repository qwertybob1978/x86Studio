# DOS Agent debugging

The 8088 Hercules runtime includes a separate FreeRTOS Agent task. Its UART
receiver is interrupt-driven, with a task-side polling fallback; the task
parses MSAG v1 frames, acknowledges debug records, relays LVGL initialization
and click events, and accepts raw file uploads through XMODEM-CRC. Application
launch and stop commands remain disabled because the Agent has no DOS
executable loader and only runs while the FreeRTOS application is active.

## DOS file transfer and filesystem support

The Agent writes uploads through the DOS C runtime's file services, which in
turn use the active DOS kernel and filesystem driver. It does not implement a
FAT driver or access disk sectors directly. Consequently, FAT16 and FAT32
support depends on the DOS version or driver providing the mounted drive:
MS-DOS systems with FAT16 support can use FAT16 volumes; FAT32 requires a DOS
kernel/driver that supports FAT32 (for example, MS-DOS 7.1/Windows 95 OSR2 or
FreeDOS).

The DOS IDE action transfers a raw file to the DOS current directory, which the
Build & Run script sets to the runtime's `build` directory. Destination names
must use a conservative 8.3 form; path components are rejected. The receiver
stages data in `DOSAGT.$$$`, verifies the transfer CRC, then renames it into
place. `DOSAGT.$$$` and `DOSAGT.BAK` are reserved staging names. Uploads do not
execute the file. The DOSBox-X directory mount validates DOS file-service
access but does not prove behavior on a physical FAT16 or FAT32 volume; verify
the target DOS kernel/driver on each real filesystem before relying on it.

## DOSBox-X serial link

The default runtime settings are COM1 at I/O base `0x3F8`, IRQ 4, 9600 baud.
The IDE's Build & Run ribbon checkbox configures DOSBox-X guest COM1 to use
host COM2 (`directserial realport:COM2`). With the configured COM2↔COM4 pair:

1. Leave **Agent COM2** checked and click **Build & Run**.
2. Open **Agent Debug → DOSBox COM4** in the IDE and connect at 9600 baud.
3. The connection window should show a DOS/8088 target and receive the
   `LVGL GUI initialized` record. Click a generated button to receive an event
   record.
4. Use **Send File...** to transfer a DOS file such as an `.EXE` or `.COM`.
   Select its destination 8.3 name; the file is written to the DOS current
   directory but is not launched.

The direct-serial configuration uses a 1000 ms receive delay so DOSBox-X
doesn't discard a burst while the 18 Hz DOS scheduler gives the Agent task CPU
time. The IDE also paces DOSBox COM4 writes by 10 ms per byte because DOSBox-X's
direct-serial receiver polls at millisecond intervals; the Python peer path
doesn't add that delay. This pacing also applies to XMODEM data blocks, so a
DOSBox-X upload is slower than the nominal 9600-baud wire rate. If host COM2 is
unavailable, uncheck **Agent COM2** to run DOSBox-X with a dummy serial device.

## Python protocol peer (VS Code/Copilot only)

The Python protocol peer is a developer-only helper for VS Code/Copilot
debugging. It is not an option in the FreeRTOSx86Studio IDE; that IDE exposes
only the DOSBox-X Agent connection. Run the helper from VS Code's integrated
terminal when using the Python peer in the direct Copilot workflow. For the
configured COM5↔COM6 pair:

```powershell
python -m pip install -r requirements-debug.txt
python tools\dos_agent_mock.py --port COM6
```

The peer sends acknowledged heartbeat records. Its framing implementation can
be checked without opening a COM port:

```powershell
python tools\dos_agent_mock.py --self-test
python -m py_compile tools\dos_agent_mock.py
```

## UART configuration

`DOSAGENT.CFG` is read from the DOS current directory at startup. The
Build & Run script creates it in the generated `build` directory if it is
missing; it does not overwrite an existing file.

```text
BASE=0x3F8
IRQ=4
```

`BASE` accepts `0x100` through `0xFFF`; the supported IRQ values are 3 and 4.
Use `BASE=0x2F8` and `IRQ=3` for the conventional COM2 settings. The UART
interrupt handler saves and restores its vector, PIC mask bit, UART divisor,
line control, modem control, and interrupt-enable state when the scheduler
exits.

The DOSBox-X smoke test validates LVGL startup and rendering with the serial
device disabled. A live direct-serial session is required to validate the host
COM2↔COM4 link, Agent protocol, and file transfer. The directory mount is not a
substitute for testing on the target's actual FAT16/FAT32 setup.
