#!/usr/bin/env python3
"""Analyze PCM WAV output using only the Python standard library."""

from __future__ import annotations

import argparse
import cmath
import json
import math
import struct
import sys
import wave
from pathlib import Path


def decode_pcm(raw: bytes, sample_width: int) -> list[float]:
    if sample_width == 1:
        return [(value - 128) / 128.0 for value in raw]
    if sample_width == 2:
        count = len(raw) // 2
        return [value / 32768.0 for value in struct.unpack(f"<{count}h", raw)]
    if sample_width == 3:
        values = []
        for offset in range(0, len(raw), 3):
            value = int.from_bytes(raw[offset : offset + 3], "little", signed=False)
            if value & 0x800000:
                value -= 1 << 24
            values.append(value / 8388608.0)
        return values
    if sample_width == 4:
        count = len(raw) // 4
        return [value / 2147483648.0 for value in struct.unpack(f"<{count}i", raw)]
    raise ValueError(f"unsupported PCM sample width: {sample_width} bytes")


def fft(values: list[complex]) -> None:
    size = len(values)
    j = 0
    for i in range(1, size):
        bit = size >> 1
        while j & bit:
            j ^= bit
            bit >>= 1
        j ^= bit
        if i < j:
            values[i], values[j] = values[j], values[i]

    length = 2
    while length <= size:
        root = cmath.exp(-2j * math.pi / length)
        for start in range(0, size, length):
            factor = 1 + 0j
            half = length // 2
            for offset in range(half):
                even = values[start + offset]
                odd = values[start + offset + half] * factor
                values[start + offset] = even + odd
                values[start + offset + half] = even - odd
                factor *= root
        length *= 2


def dominant_frequency(samples: list[float], sample_rate: int) -> float:
    if not samples:
        return 0.0
    size = 1
    limit = min(len(samples), 65536)
    while size * 2 <= limit:
        size *= 2
    if size < 32:
        return 0.0
    segment = samples[:size]
    mean = sum(segment) / size
    spectrum = [
        complex((value - mean) * (0.5 - 0.5 * math.cos(2 * math.pi * i / (size - 1))), 0.0)
        for i, value in enumerate(segment)
    ]
    fft(spectrum)
    first_bin = max(1, math.ceil(20.0 * size / sample_rate))
    last_bin = min(size // 2, math.floor(20000.0 * size / sample_rate))
    peak = max(range(first_bin, last_bin + 1), key=lambda index: abs(spectrum[index]))
    return peak * sample_rate / size


def analyze(path: Path) -> dict[str, object]:
    with wave.open(str(path), "rb") as wav:
        if wav.getcomptype() != "NONE":
            raise ValueError(f"compressed WAV is unsupported: {wav.getcomptype()}")
        channels = wav.getnchannels()
        sample_rate = wav.getframerate()
        frames = wav.getnframes()
        sample_width = wav.getsampwidth()
        decoded = decode_pcm(wav.readframes(frames), sample_width)

    channel_samples = [decoded[index::channels] for index in range(channels)]
    energies = [sum(value * value for value in values) / max(1, len(values)) for values in channel_samples]
    rms = [math.sqrt(energy) for energy in energies]
    mono = [sum(frame) / channels for frame in zip(*channel_samples)]
    pan = None
    if channels == 2:
        total = energies[0] + energies[1]
        pan = (energies[1] - energies[0]) / total if total else 0.0

    return {
        "path": str(path),
        "sample_rate": sample_rate,
        "channels": channels,
        "frames": frames,
        "duration_seconds": frames / sample_rate,
        "rms": rms,
        "channel_energy": energies,
        "pan": pan,
        "dominant_frequency_hz": dominant_frequency(mono, sample_rate),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("wav", type=Path)
    parser.add_argument("--min-rms", type=float)
    parser.add_argument("--expect-frequency", type=float)
    parser.add_argument("--frequency-tolerance", type=float, default=25.0)
    parser.add_argument("--expect-pan", type=float)
    parser.add_argument("--pan-tolerance", type=float, default=0.10)
    args = parser.parse_args()

    try:
        result = analyze(args.wav)
    except (OSError, EOFError, wave.Error, ValueError) as error:
        print(f"analyze-wav: {error}", file=sys.stderr)
        return 2

    print(json.dumps(result, indent=2, sort_keys=True))
    failures = []
    if args.min_rms is not None and max(result["rms"], default=0.0) < args.min_rms:
        failures.append(f"peak channel RMS is below {args.min_rms}")
    if args.expect_frequency is not None:
        delta = abs(result["dominant_frequency_hz"] - args.expect_frequency)
        if delta > args.frequency_tolerance:
            failures.append(
                f"dominant frequency differs by {delta:.2f} Hz "
                f"(tolerance {args.frequency_tolerance:.2f} Hz)"
            )
    if args.expect_pan is not None:
        if result["pan"] is None:
            failures.append("pan assertion requires a stereo WAV")
        else:
            delta = abs(result["pan"] - args.expect_pan)
            if delta > args.pan_tolerance:
                failures.append(f"pan differs by {delta:.4f} (tolerance {args.pan_tolerance:.4f})")

    for failure in failures:
        print(f"analyze-wav: {failure}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
