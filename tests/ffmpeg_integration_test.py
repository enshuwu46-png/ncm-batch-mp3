#!/usr/bin/env python3
from __future__ import annotations

import subprocess
import shutil
import stat
import struct
import tempfile
from pathlib import Path

from ncm_fixture import build_ncm


ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "dist" / "NCM批量转MP3.app"
APP_BIN = APP / "Contents" / "MacOS" / "NCMConverter"
BUNDLED_FFMPEG = APP / "Contents" / "Resources" / "ffmpeg"


def decode_duration(path: Path) -> float:
    pcm = subprocess.run(
        [
            str(BUNDLED_FFMPEG),
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(path),
            "-map",
            "0:a:0",
            "-ar",
            "44100",
            "-ac",
            "1",
            "-f",
            "s16le",
            "-",
        ],
        check=True,
        stdout=subprocess.PIPE,
    ).stdout
    return len(pcm) / 2 / 44100


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="ncm-ffmpeg-test-") as tmp:
        tmp_path = Path(tmp)
        out_dir = tmp_path / "out"
        cover = tmp_path / "cover.png"

        subprocess.run(
            [
                str(BUNDLED_FFMPEG),
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-f",
                "lavfi",
                "-i",
                "color=c=orange:s=64x64",
                "-frames:v",
                "1",
                str(cover),
            ],
            check=True,
        )

        mp3_audio = b""
        for audio_format, codec in (("flac", "flac"), ("mp3", "libmp3lame")):
            audio = tmp_path / f"tone.{audio_format}"
            source_ncm = tmp_path / f"tone-{audio_format}.ncm"
            title = f"Synthetic {audio_format.upper()}"

            subprocess.run(
                [
                    str(BUNDLED_FFMPEG),
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-y",
                    "-f",
                    "lavfi",
                    "-i",
                    "sine=frequency=440:duration=3",
                    "-c:a",
                    codec,
                    str(audio),
                ],
                check=True,
            )
            build_ncm(
                source_ncm,
                audio.read_bytes(),
                audio_format,
                title=title,
                cover=cover.read_bytes(),
            )
            if audio_format == "mp3":
                mp3_audio = audio.read_bytes()

            subprocess.run(
                [
                    str(APP_BIN),
                    "--cli-convert",
                    str(source_ncm),
                    "--output",
                    str(out_dir),
                    "--rename",
                ],
                check=True,
            )
            output = out_dir / f"Codex - {title}.mp3"
            if not output.exists():
                raise AssertionError(f"expected MP3 output: {output}")
            if decode_duration(output) < 2.8:
                raise AssertionError(f"audio was truncated while embedding cover: {output.name}")

            subprocess.run(
                [
                    str(BUNDLED_FFMPEG),
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-i",
                    str(output),
                    "-f",
                    "null",
                    "-",
                ],
                check=True,
            )

            extracted_cover = tmp_path / f"{audio_format}-cover.jpg"
            subprocess.run(
                [
                    str(BUNDLED_FFMPEG),
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-y",
                    "-i",
                    str(output),
                    "-map",
                    "0:v:0",
                    "-frames:v",
                    "1",
                    str(extracted_cover),
                ],
                check=True,
            )
            if not extracted_cover.read_bytes().startswith(b"\xff\xd8\xff"):
                raise AssertionError(f"missing JPEG cover in {output.name}")

            metadata = subprocess.run(
                [
                    str(BUNDLED_FFMPEG),
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-i",
                    str(output),
                    "-f",
                    "ffmetadata",
                    "-",
                ],
                check=True,
                stdout=subprocess.PIPE,
                text=True,
            ).stdout
            for expected in (f"title={title}", "artist=Codex", "album=Synthetic Album"):
                if expected not in metadata:
                    raise AssertionError(f"missing metadata {expected!r} in {output.name}: {metadata}")

            print(f"bundled ffmpeg cover metadata ok: {output.name}")

        bad_cover_source = tmp_path / "bad-cover.ncm"
        build_ncm(bad_cover_source, mp3_audio, "mp3", cover=b"\xff\xd8\xff" + bytes(100))
        subprocess.run(
            [str(APP_BIN), "--cli-convert", str(bad_cover_source), "--output", str(out_dir)],
            check=True,
        )
        bad_cover_output = out_dir / "bad-cover.mp3"
        if decode_duration(bad_cover_output) < 2.8:
            raise AssertionError("a damaged cover should not prevent audio export")
        no_cover = subprocess.run(
            [str(BUNDLED_FFMPEG), "-v", "error", "-i", str(bad_cover_output), "-map", "0:v:0", "-f", "null", "-"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        if no_cover.returncode == 0:
            raise AssertionError("damaged cover should be skipped")

        large_metadata_source = tmp_path / "large-metadata.ncm"
        build_ncm(large_metadata_source, mp3_audio, "mp3", album="A" * 100_000)
        subprocess.run(
            [str(APP_BIN), "--cli-convert", str(large_metadata_source), "--output", str(out_dir)],
            check=True,
            timeout=8,
        )

        oversized_header = tmp_path / "oversized-header.ncm"
        build_ncm(oversized_header, mp3_audio, "mp3")
        raw = bytearray(oversized_header.read_bytes())
        raw[10:14] = struct.pack("<I", 16 * 1024 * 1024 + 1)
        oversized_header.write_bytes(raw)
        oversized_result = subprocess.run(
            [str(APP_BIN), "--cli-convert", str(oversized_header), "--output", str(out_dir)],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        if oversized_result.returncode == 0:
            raise AssertionError("oversized header should be rejected")

        failing_app = tmp_path / "failing-ffmpeg.app"
        shutil.copytree(APP, failing_app)
        failing_ffmpeg = failing_app / "Contents" / "Resources" / "ffmpeg"
        failing_ffmpeg.write_text("#!/bin/sh\nexit 9\n")
        failing_ffmpeg.chmod(failing_ffmpeg.stat().st_mode | stat.S_IXUSR)
        failing_source = tmp_path / "failing.ncm"
        build_ncm(failing_source, mp3_audio, "mp3", cover=cover.read_bytes())
        protected_output = out_dir / "failing.mp3"
        original = b"previous successful output"
        protected_output.write_bytes(original)
        failed = subprocess.run(
            [
                str(failing_app / "Contents" / "MacOS" / "NCMConverter"),
                "--cli-convert",
                str(failing_source),
                "--output",
                str(out_dir),
                "--overwrite",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        if failed.returncode == 0 or protected_output.read_bytes() != original:
            raise AssertionError("a failed transcode must preserve an existing output")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
