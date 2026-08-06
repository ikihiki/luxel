#!/usr/bin/env python3
"""Deterministic self-test for analyze-wav.py."""

import importlib.util
import math
import struct
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

SCRIPT = Path(__file__).with_name("analyze-wav.py")
spec = importlib.util.spec_from_file_location("analyze_wav", SCRIPT)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

sample_rate = 48000
frequency = 1000.0
frames = sample_rate
with tempfile.TemporaryDirectory() as directory:
    path = Path(directory) / "fixture.wav"
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        payload = bytearray()
        for index in range(frames):
            sample = math.sin(2.0 * math.pi * frequency * index / sample_rate)
            payload.extend(struct.pack("<hh", int(sample * 16384), int(sample * 8192)))
        wav.writeframes(payload)

    result = module.analyze(path)
    assert result["sample_rate"] == sample_rate
    assert result["channels"] == 2
    assert abs(result["dominant_frequency_hz"] - frequency) < 2.0, result
    assert abs(result["rms"][0] - math.sqrt(0.125)) < 0.001, result
    assert abs(result["rms"][1] - math.sqrt(0.03125)) < 0.001, result
    assert abs(result["pan"] - (-0.6)) < 0.01, result

    subprocess.run(
        [
            sys.executable,
            str(SCRIPT),
            str(path),
            "--min-rms",
            "0.1",
            "--expect-frequency",
            "1000",
            "--frequency-tolerance",
            "2",
            "--expect-pan",
            "-0.6",
            "--pan-tolerance",
            "0.01",
        ],
        check=True,
        capture_output=True,
        text=True,
    )

print("WAV analyzer self-test passed")
