"""Local extraction benchmark; synthetic payloads are checked byte-for-byte by SHA256."""
import argparse
import hashlib
import json
from pathlib import Path
import statistics
import subprocess
import time

from ncm_fixture import build_ncm

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / ".build/performance"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("executable", type=Path)
    parser.add_argument("--label", required=True)
    args = parser.parse_args()
    fixtures = WORK / "fixtures"
    fixtures.mkdir(parents=True, exist_ok=True)
    outputs = WORK / (args.label + "-out")
    results = {}
    for name, size, count in [("small-batch", 65536 + 137, 32), ("large", 32 * 1024 * 1024 + 137, 1)]:
        audio = b"ID3" + (bytes(range(256)) * ((size + 255) // 256))[:size - 3]
        source = fixtures / (name + ".ncm")
        if not source.exists():
            build_ncm(source, audio, "mp3")
        expected = hashlib.sha256(audio).hexdigest()
        command = [str(args.executable.resolve()), "--cli-convert", *([str(source)] * count),
                   "--output", str(outputs), "--mode", "original", "--overwrite"]
        times = []
        for _ in range(3):
            start = time.perf_counter()
            subprocess.run(command, check=True, capture_output=True)
            times.append(time.perf_counter() - start)
            assert hashlib.sha256((outputs / (name + ".mp3")).read_bytes()).hexdigest() == expected
        results[name] = {"bytes_per_file": size, "files": count,
                         "seconds": times, "median_seconds": statistics.median(times), "sha256": expected}
    path = WORK / (args.label + ".json")
    path.write_text(json.dumps(results, indent=2) + "\n")
    print(path)
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
