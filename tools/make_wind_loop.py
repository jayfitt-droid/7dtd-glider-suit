#!/usr/bin/env python3
"""Make the wind loop (GliderWind.wav) from Jason's recording (D30).

Source: tools/source_audio/gliding_air.wav, 15 s, 44.1 kHz 16-bit stereo, with
a fade-in of about 0.3 s and a fade-out of about 0.6 s. Looped as it is, it
would dip every 15 s. This trims both fades and crossfades the last
CROSSFADE_SECONDS of the remainder into its start (equal power, since the
channels are uncorrelated noise), so the loop has no seam. Stereo is kept.

Run from the repo root:
  python tools/make_wind_loop.py
then rebuild the bundle with build-assets.ps1 -SkipBlender.
"""

import os
import wave

import numpy as np

TRIM_START_SECONDS = 0.40
TRIM_END_SECONDS = 0.80
CROSSFADE_SECONDS = 0.75

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "source_audio", "gliding_air.wav")
TARGET = os.path.join(HERE, "..", "unity", "GliderSuitAssets", "Assets", "GliderSuit", "Audio", "GliderWind.wav")


def main():
    with wave.open(SOURCE, "rb") as w:
        channels, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        if width != 2:
            raise SystemExit("expected 16-bit samples, got %d bytes" % width)
        audio = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64)
    audio = audio.reshape(-1, channels) / 32768.0

    body = audio[int(TRIM_START_SECONDS * rate):audio.shape[0] - int(TRIM_END_SECONDS * rate)]
    fade = int(CROSSFADE_SECONDS * rate)
    t = np.linspace(0.0, 1.0, fade)[:, None]
    loop = body[:-fade].copy()
    # Start of the loop: the tail fading out into the head fading in, so the
    # last sample of the loop runs straight into the tail's next sample.
    loop[:fade] = body[:fade] * np.sin(0.5 * np.pi * t) + body[-fade:] * np.cos(0.5 * np.pi * t)

    peak = float(np.max(np.abs(loop)))
    if peak > 0.98:
        loop *= 0.98 / peak
    out = np.round(loop * 32767.0).astype(np.int16)
    with wave.open(TARGET, "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(out.tobytes())

    seam = float(np.max(np.abs(loop[0] - loop[-1])))
    step = float(np.percentile(np.abs(np.diff(loop, axis=0)), 99))
    print("wrote %s (%.2f s, %d channels, peak %.1f dBFS); seam step %.4f against a typical step of %.4f" % (
        os.path.normpath(TARGET), loop.shape[0] / rate, channels, 20 * np.log10(np.max(np.abs(loop))), seam, step))


if __name__ == "__main__":
    main()
