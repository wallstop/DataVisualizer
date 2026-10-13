#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["Pillow==12.3.0"]
# ///
"""Encode reviewed full-window Unity frames as a creation-controls GIF.

Pillow is local-only. CI publication contracts use a supplied encoder.
GIF uses a shared 256-color palette; PNG captures remain the full-color sources.
"""
from __future__ import annotations

import argparse
import io
import os
import sys
import tempfile
from pathlib import Path
from typing import Callable, Sequence, Tuple

FRAME_NAMES: Tuple[str, ...] = ('data-visualizer-layout-frame.png', 'data-visualizer-create-frame.png')
DURATIONS: Tuple[int, ...] = (1200, 1800)
DEFAULT_OUTPUT = Path(__file__).resolve().parents[1] / 'docs/images/data-visualizer-create.gif'


def encode_verified(frames: Sequence[Path]) -> bytes:
    from PIL import GifImagePlugin, Image

    # Build one palette from both frames so new control colors survive without background flicker.
    with Image.open(frames[0]) as closed, Image.open(frames[1]) as opened:
        if closed.format != 'PNG' or opened.format != 'PNG':
            raise ValueError('Animation inputs must be PNG captures.')
        if closed.size != opened.size:
            raise ValueError('Animation frames must have matching dimensions.')
        combined = Image.new('RGB', (closed.width, closed.height * 2))
        combined.paste(closed.convert('RGB'), (0, 0))
        combined.paste(opened.convert('RGB'), (0, closed.height))
        palette = combined.quantize(colors=256, dither=Image.Dither.NONE)
        first = closed.convert('RGB').quantize(palette=palette, dither=Image.Dither.NONE)
        second = opened.convert('RGB').quantize(palette=palette, dither=Image.Dither.NONE)
        if first.convert('RGB').tobytes() == second.convert('RGB').tobytes():
            raise ValueError('Creation controls must change the second frame.')
        encoded = io.BytesIO()
        first.save(encoded, format='GIF', save_all=True, append_images=[second],
                   duration=list(DURATIONS), loop=0, disposal=2, optimize=False)
        result = encoded.getvalue()
        with Image.open(io.BytesIO(result)) as decoded:
            if not isinstance(decoded, GifImagePlugin.GifImageFile) or decoded.n_frames != 2 or decoded.size != closed.size or decoded.info.get('loop') != 0:
                raise ValueError('GIF did not retain both full-window frames and its loop.')
            for index, expected in enumerate((first, second)):
                decoded.seek(index)
                if decoded.info.get('duration') != DURATIONS[index]:
                    raise ValueError('GIF changed the frame timing.')
                if decoded.convert('RGB').tobytes() != expected.convert('RGB').tobytes():
                    raise ValueError('GIF changed the quantized frame pixels.')
        return result


def publish(frames_dir: Path, output: Path, check: bool, encoder: Callable[[Sequence[Path]], bytes] = encode_verified) -> bool:
    frames = [frames_dir / name for name in FRAME_NAMES]
    for frame in frames:
        if not frame.is_file():
            raise ValueError(f'Missing animation frame: {frame}')
    encoded = encoder(frames)
    if output.is_file() and output.read_bytes() == encoded:
        return False
    if check:
        raise ValueError(f'Stale or missing GIF: {output}')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.gif-export-', dir=output.parent) as staging:
        temporary = Path(staging) / output.name
        temporary.write_bytes(encoded)
        os.replace(temporary, output)
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frames', type=Path, required=True, help='reviewed CaptureCreationFramesFromCommandLine output directory')
    parser.add_argument('--output', type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument('--check', action='store_true', help='verify committed output without writing')
    options = parser.parse_args()
    try:
        changed = publish(options.frames, options.output, options.check)
    except (OSError, ValueError) as error:
        print(f'[gif] ERROR: {error}', file=sys.stderr)
        return 1
    print(f"[gif] OK: {options.output}; {'updated' if changed else 'unchanged'}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
