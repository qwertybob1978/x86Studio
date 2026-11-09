# MacStudio Serial Agent Protocol v1

**Status:** The sample Agent implements HELLO/HELLO_ACK, cooperative
background scheduling, AppleEvent-based application logging and launch/quit,
and streaming XMODEM-CRC receipt of MacBinary II applications into the HFS
`MacStudio` directory. The host implements PUT, RUN, STOP, and a sequenced
debug-record receiver/acknowledger. Two consecutive 3,968-byte PUT/install
operations, including replacement of the same destination, passed on
Basilisk II/System 7.5.5 over COM30/COM31. RUN/STOP and generated-project log
handling have build and fake-peer coverage but have not yet been verified
end-to-end in an emulator. The Agent can select Modem or Printer with the
optional `MacStudio/AgentPort` file; physical port mappings remain
unverified. Disk-full and corrupted-transfer recovery, real-Mac behavior, and
System 7.0 support remain unverified.

This specification defines bounded control frames exchanged between the IDE
and the MacStudio Agent. It does not define a remote shell. A received
application is never launched unless the Agent accepts a separate `RUN`
request for that completed build in the current session.

## Transport and framing

- Serial settings are explicit per connection. The first supported
  configuration is 9600 baud, 8 data bits, no parity, one stop bit, and no
  flow control.
- Control frames use network byte order (big-endian) for multi-byte integers.
- Each frame is:

  | Field | Size | Description |
  |---|---:|---|
  | Magic | 4 bytes | ASCII `MSAG` (`4D 53 41 47`) |
  | Version | 1 byte | `1` for this frame format |
  | Type | 1 byte | Message type from the table below |
  | Flags | 2 bytes | Must be zero in version 1 |
  | Session ID | 16 bytes | Random host-generated ID, echoed by the Agent |
  | Payload length | 2 bytes | Payload size, `0..1024` |
  | Payload | 0..1024 bytes | Message-specific data |
  | CRC | 2 bytes | CRC-16/CCITT-FALSE over header and payload |

- The maximum frame size is 1052 bytes. Receivers must reject lengths above
  1024 before buffering the payload.
- CRC-16/CCITT-FALSE uses polynomial `0x1021`, initial value `0xFFFF`, no
  reflection, and xor-out `0x0000`; transmit the CRC high byte first.
- The parser scans for the magic value, waits for the bounded header and
  payload, then checks version and CRC. On invalid framing or CRC, discard
  one byte and rescan; never trust a malformed length.
- Version 1 has one active session. Except for `HELLO`, frames with a session
  ID different from the active session are ignored. A new valid `HELLO`
  replaces the previous idle session but cannot interrupt an active transfer.
- Control frames are not interleaved with XMODEM data. After `READY`, the
  byte stream belongs exclusively to XMODEM-CRC until EOT completion,
  cancellation, timeout, or disconnect.

## Message types

| Type | Name | Purpose |
|---:|---|---|
| `0x01` | `HELLO` | Discover the Agent and negotiate a protocol version |
| `0x02` | `HELLO_ACK` | Report Agent, System, target, and capability information |
| `0x10` | `PUT_BEGIN` | Request receipt of a packaged application |
| `0x11` | `READY` | Accept or reject the requested transfer |
| `0x12` | `PUT_COMPLETE` | Report package verification and installation result |
| `0x20` | `RUN` | Launch an installed build by build ID |
| `0x21` | `RUN_RESULT` | Report launch result |
| `0x30` | `DEBUG_RECORD` | Relay one bounded application log record |
| `0x31` | `DEBUG_ACK` | Acknowledge a debug record sequence |
| `0x40` | `STOP` | Request termination of the active BUT |
| `0x41` | `STOP_RESULT` | Report stop result |
| `0x7E` | `DISCONNECT` | End the active session |
| `0x7F` | `ERROR` | Report a bounded, actionable protocol error |

Unknown message types and unsupported versions must not change Agent state.
Where a session is established, the Agent may return `ERROR` with the rejected
message type and an error code.

## Payload layouts

All payloads are fixed-layout unless a length field is specified. Reserved
fields must be zero and must be rejected when nonzero.

### HELLO (`0x01`)

| Field | Size |
|---|---:|
| Minimum supported protocol version | 1 byte |
| Maximum supported protocol version | 1 byte |
| Host major version | 1 byte |
| Host minor version | 1 byte |

The host generates a fresh 16-byte Session ID for each connection attempt.
The Agent echoes it in `HELLO_ACK`. A version range with no overlap is
rejected.

### HELLO_ACK (`0x02`)

| Field | Size |
|---|---:|
| Selected protocol version | 1 byte |
| Agent major, minor, patch | 3 bytes |
| System major, minor | 2 bytes |
| Machine type | 2 bytes |
| Capability flags | 4 bytes |
| Free bytes on target volume | 4 bytes |
| Supported baud-rate flags | 4 bytes |

Capability bit 0 advertises PUT, bit 1 RUN, and bit 2 STOP support.
Supported-baud bit assignments are:

| Bit | Baud rate |
|---:|---:|
| 0 | 9600 |
| 1 | 19200 |
| 2 | 38400 |
| 3 | 57600 |
| 4 | 300 |
| 5 | 600 |
| 6 | 1200 |
| 7 | 1800 |
| 8 | 2400 |
| 9 | 3600 |
| 10 | 4800 |
| 11 | 7200 |
| 12 | 14400 |
| 13 | 28800 |

Bit 0 remains 9600 for compatibility with the first protocol prototype;
unknown bits are ignored by the host.

The Agent advertises PUT, RUN, and STOP after its HFS staging directory and
target volume are available. Free bytes reports the current target volume.
Generated projects expose `MacStudioDebugLog(severity, message)` for bounded
printable ASCII records; it returns an `OSErr` if the Agent is absent, the
request is invalid, or the one-record Agent queue is occupied.
The Agent supports the classic Serial Manager rates from 300 through 57600.
Its active rate is set by the optional `MacStudio/AgentBaudRate` text file;
absence selects 9600 baud. The host and Agent must use the same rate before
HELLO.

### PUT_BEGIN (`0x10`)

| Field | Size |
|---|---:|
| Build ID | 16 bytes |
| Package length | 4 bytes |
| Package CRC-32/ISO-HDLC | 4 bytes |
| Transfer mode | 1 byte (`1` = XMODEM-CRC) |
| Destination name length | 1 byte |
| Destination name | 1..31 ASCII bytes |

The destination name must be a single classic HFS filename, not a path. The
Agent rejects zero or excessive package lengths, invalid names, insufficient
space, unsupported modes, and transfers while another operation owns the
serial stream.

### READY (`0x11`)

| Field | Size |
|---|---:|
| Result code | 1 byte |

Result `0` accepts the transfer and starts XMODEM-CRC reception. Nonzero
results reject the transfer; the serial stream remains in control-frame mode.
The Agent uses `1` for an invalid request, `2` for insufficient space, `4` for
a file-system error, and `5` when another operation owns the serial stream.

### PUT_COMPLETE (`0x12`)

| Field | Size |
|---|---:|
| Build ID | 16 bytes |
| Result code | 1 byte |
| Verified package CRC-32/ISO-HDLC | 4 bytes |

Success means the exact package length and checksum were verified and the
application was installed. It does not authorize launch. Result codes `1`,
`2`, `3`, and `4` report a length mismatch, package checksum mismatch,
malformed MacBinary package, and HFS installation/cleanup failure,
respectively.

### RUN and RUN_RESULT (`0x20`, `0x21`)

`RUN` contains one 16-byte Build ID. `RUN_RESULT` contains that Build ID and
one result-code byte: `0` means launched or already running, `1` means the
Build ID is not installed in the current Agent process, `2` means launch
failed, and `3` means another build is already running. The Agent launches
only the most recently installed Build ID in its current process. The previous
known-good application is retained as `.MSAGPrevious` when a replacement is
installed.

### DEBUG_RECORD and DEBUG_ACK (`0x30`, `0x31`)

`DEBUG_RECORD` contains a 4-byte sequence number, a 1-byte severity, a
2-byte record length, and at most 512 record bytes. Version 1 record bytes
must be printable ASCII plus tab; the Agent rejects other control bytes.
`DEBUG_ACK` contains the 4-byte sequence number and a 1-byte result code.

The sample Agent accepts local debug writes as an AppleEvent with event class
`MSAG`, event ID `DBUG`, and a `typeChar` direct parameter. The parameter is a
severity byte, a 2-byte big-endian record length, then up to 512 printable
ASCII bytes (tab is also allowed). The Agent queues at most one record, sends
it once after a serial session is established, and clears it only when the
matching `DEBUG_ACK` has result code zero. This is a feasibility prototype:
there is no retry or durable queue. Generated forms include the
`MacStudioDebugLog` API, and the IDE validates and acknowledges records while
displaying their sequence, severity, and text.

### STOP and STOP_RESULT (`0x40`, `0x41`)

`STOP` contains one 16-byte Build ID. `STOP_RESULT` contains that Build ID
and one result-code byte: `0` means the Agent accepted the target application's
standard quit AppleEvent, `1` means no BUT is running, `2` means the Build ID
does not identify the running BUT, and `3` means the quit request failed.
Acceptance of the quit event does not guarantee that the application has
finished shutting down.

### DISCONNECT and ERROR (`0x7E`, `0x7F`)

`DISCONNECT` has an empty payload. `ERROR` contains a 2-byte error code, a
1-byte rejected message type, a 1-byte reason length, and at most 128 printable
ASCII reason bytes.

## Application package

Use **MacBinary II** as the first application package format. It preserves the
data fork, resource fork, Finder file type/creator, and Finder metadata needed
to install a Retro68 application on HFS. The host calculates package length
and CRC-32 over the complete MacBinary II byte stream. The Agent validates
the header and fork lengths before creating the destination, then writes both
forks and metadata to a temporary HFS file before atomic replacement.

The host derives the 16-byte Build ID from the first 16 bytes of SHA-256 over
the complete MacBinary II stream. Package CRC-32 uses CRC-32/ISO-HDLC over the
same bytes, including the header and fork padding. The host validates the
MacBinary header CRC and exact padded fork lengths before transfer.

The Retro68 `AgentLaunchTarget` app has passed an `hcopy -m` MacBinary II
extract/import/re-extract check: its header CRC validated, the 3,781-byte
resource fork and Finder metadata were preserved, and its empty data fork
remained empty. Basilisk II/System 7.5.5 live tests then received and installed
the package, including replacement of an existing destination, while
preserving both forks and Finder metadata. Real-Mac and System 7.0 behavior
remain unverified.

Raw XMODEM bytes are not an application package. The Agent must never launch a
package solely because transfer completed; a valid `RUN` for the installed
Build ID is required.

## Phase 1 IPC decision

AppleEvents are selected for the System 7 MultiFinder prototype: local probes
have submitted a debug record and launch request, and the Agent relayed the
record and returned to serial service. Generated projects now expose the
AppleEvent logging API, but the Agent still has no retry/backpressure or
durable queue. Neither this API nor the RUN/STOP path has been validated
across System 7.0 or on real hardware. The Agent remains the sole owner of the
serial port.
