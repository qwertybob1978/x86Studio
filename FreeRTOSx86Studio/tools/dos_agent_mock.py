#!/usr/bin/env python3
"""MSAG v1 debug-only peer for testing the Studio serial connection."""

import argparse
import struct
import sys
import time
from typing import List, Optional, Tuple

MAGIC = b"MSAG"
VERSION = 1
HEADER_SIZE = 26
MAX_PAYLOAD_SIZE = 1024
HELLO = 0x01
HELLO_ACK = 0x02
DEBUG_RECORD = 0x30
DEBUG_ACK = 0x31
DISCONNECT = 0x7E

Frame = Tuple[int, bytes, bytes]


def crc16(data: bytes) -> int:
    crc = 0xFFFF
    for value in data:
        crc ^= value << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc


def encode_frame(message_type: int, session_id: bytes, payload: bytes = b"") -> bytes:
    if len(session_id) != 16:
        raise ValueError("session IDs must be exactly 16 bytes")
    if len(payload) > MAX_PAYLOAD_SIZE:
        raise ValueError("payload exceeds the MSAG v1 limit")
    header = MAGIC + bytes((VERSION, message_type, 0, 0)) + session_id
    header += struct.pack(">H", len(payload))
    body = header + payload
    return body + struct.pack(">H", crc16(body))


class FrameDecoder:
    def __init__(self) -> None:
        self._buffer = bytearray()

    def append(self, data: bytes) -> List[Frame]:
        self._buffer.extend(data)
        frames: List[Frame] = []
        while len(self._buffer) >= len(MAGIC):
            offset = self._buffer.find(MAGIC)
            if offset < 0:
                del self._buffer[:-3]
                break
            if offset:
                del self._buffer[:offset]
            if len(self._buffer) < HEADER_SIZE:
                break

            payload_length = struct.unpack_from(">H", self._buffer, 24)[0]
            if (
                self._buffer[4] != VERSION
                or self._buffer[6] != 0
                or self._buffer[7] != 0
                or payload_length > MAX_PAYLOAD_SIZE
            ):
                del self._buffer[0]
                continue

            frame_length = HEADER_SIZE + payload_length + 2
            if len(self._buffer) < frame_length:
                break
            expected_crc = struct.unpack_from(">H", self._buffer, frame_length - 2)[0]
            if crc16(self._buffer[: frame_length - 2]) != expected_crc:
                del self._buffer[0]
                continue

            message_type = self._buffer[5]
            session_id = bytes(self._buffer[8:24])
            payload = bytes(self._buffer[HEADER_SIZE : HEADER_SIZE + payload_length])
            frames.append((message_type, session_id, payload))
            del self._buffer[:frame_length]
        return frames


def self_test() -> None:
    if crc16(b"123456789") != 0x29B1:
        raise AssertionError("CRC-16/CCITT-FALSE check vector failed")
    expected = (HELLO, bytes(range(16)), b"\x01\x01\x01\x00")
    frame = encode_frame(*expected)
    decoder = FrameDecoder()
    actual = decoder.append(frame[:11]) + decoder.append(frame[11:])
    if actual != [expected]:
        raise AssertionError("split-frame round trip failed")
    corrupted = bytearray(frame)
    corrupted[-1] ^= 0xFF
    if decoder.append(bytes(corrupted)):
        raise AssertionError("corrupt frame was accepted")
    print("MSAG v1 framing self-test passed.")


def run_peer(port_name: str, baud_rate: int, interval: float) -> None:
    try:
        import serial
    except ImportError as error:
        raise SystemExit(
            "pyserial is required; install it with "
            "'python -m pip install -r requirements-debug.txt'."
        ) from error

    decoder = FrameDecoder()
    session_id: Optional[bytes] = None
    pending_sequence: Optional[int] = None
    next_sequence = 1
    next_message_time = time.monotonic()

    print(f"Waiting as a debug-only Agent on {port_name} at {baud_rate} baud.")
    with serial.Serial(
        port_name,
        baud_rate,
        bytesize=serial.EIGHTBITS,
        parity=serial.PARITY_NONE,
        stopbits=serial.STOPBITS_ONE,
        timeout=0.1,
        write_timeout=1.0,
    ) as connection:
        while True:
            for message_type, incoming_session, payload in decoder.append(connection.read(256)):
                if message_type == HELLO:
                    if len(payload) != 4 or payload[0] > VERSION or payload[1] < VERSION:
                        continue
                    session_id = incoming_session
                    pending_sequence = None
                    hello_ack = bytes((VERSION, 1, 0, 0, 5, 0))
                    hello_ack += struct.pack(">HIII", 0x8088, 0, 0, 1)
                    connection.write(encode_frame(HELLO_ACK, session_id, hello_ack))
                    connection.flush()
                    next_message_time = time.monotonic()
                    print("Studio IDE connected; DOS/8088 debug capabilities advertised.")
                elif session_id == incoming_session and message_type == DISCONNECT:
                    session_id = None
                    pending_sequence = None
                    print("Studio IDE disconnected.")
                elif (
                    session_id == incoming_session
                    and message_type == DEBUG_ACK
                    and len(payload) == 5
                    and pending_sequence is not None
                    and struct.unpack_from(">I", payload)[0] == pending_sequence
                ):
                    if payload[4] == 0:
                        pending_sequence = None
                        next_message_time = time.monotonic() + interval

            if (
                session_id is not None
                and pending_sequence is None
                and time.monotonic() >= next_message_time
            ):
                message = f"Python peer heartbeat {next_sequence}".encode("ascii")
                payload = struct.pack(">IBH", next_sequence, 1, len(message)) + message
                connection.write(encode_frame(DEBUG_RECORD, session_id, payload))
                connection.flush()
                pending_sequence = next_sequence
                next_sequence = (next_sequence + 1) & 0xFFFFFFFF
                next_message_time = time.monotonic() + interval


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", default="COM6", help="paired serial port (default: COM6)")
    parser.add_argument("--baud", type=int, default=9600, help="baud rate (default: 9600)")
    parser.add_argument(
        "--interval",
        type=float,
        default=1.0,
        help="seconds between acknowledged heartbeat records",
    )
    parser.add_argument("--self-test", action="store_true", help="test framing without a COM port")
    arguments = parser.parse_args()

    if arguments.self_test:
        self_test()
        return
    if arguments.baud <= 0 or arguments.interval <= 0:
        parser.error("--baud and --interval must be positive")
    run_peer(arguments.port, arguments.baud, arguments.interval)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nStopped.", file=sys.stderr)
