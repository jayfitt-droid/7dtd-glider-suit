#!/usr/bin/env python3
"""Procedurally create the three Glider Suit sounds.

Output: 44.1 kHz, 16-bit mono WAV, peak -1 dBFS, fixed RNG seed, so the same
run always produces the same files and they can be committed.

  GliderUnfold.wav  ~0.9 s  buckle release, pole sections snapping straight,
                            tarp unfurling, skin going taut
  GliderFold.wav    ~0.7 s  tarp collapsing, poles clattering, strap cinch
  GliderWind.wav    ~3.0 s  seamless air-noise loop, loudness driven in game

The wind loop is made seamless by crossfading its last 0.3 s into its first,
so no fade is applied to it (a fade would click every time round).

Since D30 the shipped GliderWind.wav comes from Jason's recording through
tools/make_wind_loop.py. This script still builds its own wind loop, but only
writes it when --wind is given, so a normal run leaves the recording alone.
"""

import argparse
import math
import os
import wave

import numpy as np

SAMPLE_RATE = 44100
SEED = 20261012
FADE_SECONDS = 0.005


def one_pole_lowpass(x, cutoff_hz, sample_rate=SAMPLE_RATE):
    """Single-pole RC low-pass; cutoff may be an array for a sweep."""
    cutoff = np.asarray(cutoff_hz, dtype=np.float64)
    if cutoff.ndim == 0:
        cutoff = np.full(x.shape, float(cutoff))
    alpha = 1.0 - np.exp(-2.0 * math.pi * cutoff / sample_rate)
    y = np.empty_like(x)
    state = 0.0
    for i in range(x.shape[0]):
        state += alpha[i] * (x[i] - state)
        y[i] = state
    return y


def bandpass(x, low_hz, high_hz):
    return one_pole_lowpass(x, high_hz) - one_pole_lowpass(x, low_hz)


def pink_noise(rng, length):
    """1/f-ish noise: sum a few octaves of white noise."""
    out = np.zeros(length)
    for octave in range(6):
        step = 2 ** octave
        count = length // step + 2
        coarse = rng.standard_normal(count)
        out += np.interp(np.arange(length), np.arange(count) * step, coarse) / (octave + 1)
    return out


def tock(rng, length, freq, decay, resonance=0.25):
    """A hollow tent-pole 'tock': a damped sine plus a tube resonance."""
    t = np.arange(length) / SAMPLE_RATE
    body = np.sin(2.0 * math.pi * freq * t) * np.exp(-t * decay)
    body += resonance * np.sin(2.0 * math.pi * freq * 2.7 * t) * np.exp(-t * decay * 2.2)
    click_len = max(1, int(0.004 * SAMPLE_RATE))
    click = bandpass(rng.standard_normal(click_len), 800.0, 4000.0)
    click *= np.exp(-np.arange(click_len) / (0.2 * click_len))
    click /= max(np.max(np.abs(click)), 1e-9)
    body[:click_len] += click * 0.5
    return body


def crinkle(rng, length, rate_hz):
    """Fast random amplitude gating: the plastic-tarp character."""
    t = np.arange(length) / SAMPLE_RATE
    gate = rng.random(length) < (rate_hz / SAMPLE_RATE)
    carrier = np.zeros(length)
    idx = np.flatnonzero(gate)
    for start in idx:
        span = min(length - start, int(0.004 * SAMPLE_RATE))
        if span <= 0:
            break
        burst = rng.standard_normal(span)
        carrier[start:start + span] += burst * np.exp(-np.arange(span) / (0.25 * span))
    return carrier * (0.5 + 0.5 * np.sin(2.0 * math.pi * 11.0 * t))


def thump(length, start_hz, end_hz, decay):
    t = np.arange(length) / SAMPLE_RATE
    freq = np.linspace(start_hz, end_hz, length)
    phase = 2.0 * math.pi * np.cumsum(freq) / SAMPLE_RATE
    return (np.sin(phase) + 0.3 * np.sin(2.0 * phase)) * np.exp(-t * decay)


def add_at(track, start_s, segment, gain=1.0):
    start = int(round(start_s * SAMPLE_RATE))
    end = min(track.shape[0], start + segment.shape[0])
    if end <= start:
        return
    track[start:end] += segment[: end - start] * gain


def normalize(x, peak_dbfs=-1.0):
    peak = float(np.max(np.abs(x)))
    if peak > 0.0:
        x = x * (10 ** (peak_dbfs / 20.0)) / peak
    return x


def fade_edges(x):
    n = max(1, int(FADE_SECONDS * SAMPLE_RATE))
    n = min(n, x.shape[0] // 2)
    if n <= 0:
        return x
    x = x.copy()
    x[:n] *= np.linspace(0.0, 1.0, n)
    x[-n:] *= np.linspace(1.0, 0.0, n)
    return x


def build_unfold(rng):
    total = int(0.90 * SAMPLE_RATE)
    track = np.zeros(total)

    # 1. Buckle release: two decaying metallic sines and a noise tick.
    click = np.zeros(int(0.10 * SAMPLE_RATE))
    t = np.arange(click.shape[0]) / SAMPLE_RATE
    click += 0.7 * np.sin(2.0 * math.pi * 2100.0 * t) * np.exp(-t * 55.0)
    click += 0.4 * np.sin(2.0 * math.pi * 3400.0 * t) * np.exp(-t * 75.0)
    tick = rng.standard_normal(int(0.006 * SAMPLE_RATE))
    tick *= np.exp(-np.arange(tick.shape[0]) / (0.25 * tick.shape[0]))
    click[: tick.shape[0]] += 0.8 * tick
    add_at(track, 0.0, click, 0.55)

    # 2. Three tent-pole sections snapping straight, ~90 ms apart.
    for index, base in enumerate((620.0, 740.0, 860.0)):
        body = tock(rng, int(0.16 * SAMPLE_RATE), base, 26.0)
        add_at(track, 0.05 + index * 0.09, body, 0.7)

    # 3. Tarp unfurl: band-passed noise sweeping 3 kHz -> 500 Hz, crinkled.
    unfurl_len = int(0.60 * SAMPLE_RATE)
    pink = pink_noise(rng, unfurl_len)
    sweep = np.linspace(3000.0, 500.0, unfurl_len)
    unfurl = one_pole_lowpass(pink, sweep)
    unfurl += 0.6 * bandpass(rng.standard_normal(unfurl_len), 1500.0, 6000.0)
    unfurl *= crinkle(rng, unfurl_len, 900.0)
    t = np.arange(unfurl_len) / SAMPLE_RATE
    unfurl *= np.sin(math.pi * np.clip(t / 0.60, 0.0, 1.0)) ** 1.1
    add_at(track, 0.15, unfurl, 0.5)

    # 4. The skin going taut: a soft low thump with a brief flap tail.
    add_at(track, 0.58, thump(int(0.22 * SAMPLE_RATE), 70.0, 45.0, 12.0), 0.5)
    flap = pink_noise(rng, int(0.18 * SAMPLE_RATE))
    flap = one_pole_lowpass(flap, 900.0)
    flap *= np.exp(-np.arange(flap.shape[0]) / (0.05 * SAMPLE_RATE))
    add_at(track, 0.66, flap, 0.25)
    return fade_edges(normalize(track))


def build_fold(rng):
    total = int(0.70 * SAMPLE_RATE)
    track = np.zeros(total)

    # 1. Tarp collapsing: crinkly band-passed noise, falling envelope,
    #    sweeping 800 Hz -> 2.5 kHz as the fabric gathers.
    collapse_len = int(0.45 * SAMPLE_RATE)
    pink = pink_noise(rng, collapse_len)
    sweep = np.linspace(800.0, 2500.0, collapse_len)
    collapse = one_pole_lowpass(pink, sweep)
    collapse += 0.7 * bandpass(rng.standard_normal(collapse_len), 1200.0, 5000.0)
    collapse *= crinkle(rng, collapse_len, 1400.0)
    t = np.arange(collapse_len) / SAMPLE_RATE
    collapse *= np.exp(-t * 4.2) * (1.0 - 0.6 * t / 0.45)
    add_at(track, 0.0, collapse, 0.55)

    # 2. Poles clattering together: irregular, lower and looser than the unfold.
    for start, base in ((0.20, 430.0), (0.27, 520.0), (0.335, 470.0), (0.42, 580.0)):
        body = tock(rng, int(0.14 * SAMPLE_RATE), base, 34.0, 0.2)
        add_at(track, start, body, 0.55)

    # 3. Strap cinch: a short zip with a ~60 Hz ripple, then a soft click.
    zip_len = int(0.12 * SAMPLE_RATE)
    zip_noise = bandpass(rng.standard_normal(zip_len), 900.0, 4500.0)
    t = np.arange(zip_len) / SAMPLE_RATE
    zip_noise *= 0.5 + 0.5 * np.sin(2.0 * math.pi * 60.0 * t)
    zip_noise *= np.exp(-t * 18.0)
    add_at(track, 0.54, zip_noise, 0.5)
    click = np.zeros(int(0.05 * SAMPLE_RATE))
    t = np.arange(click.shape[0]) / SAMPLE_RATE
    click += np.sin(2.0 * math.pi * 1500.0 * t) * np.exp(-t * 90.0)
    add_at(track, 0.60, click, 0.35)
    return fade_edges(normalize(track))


def build_wind(rng):
    # 3.0 s once the 0.3 s loop crossfade has been folded in.
    total = int(3.3 * SAMPLE_RATE)
    pink = pink_noise(rng, total)
    # A slowly wandering band-pass, 300-1200 Hz.
    t = np.arange(total) / SAMPLE_RATE
    wander = 750.0 + 450.0 * np.sin(2.0 * math.pi * 0.23 * t + 0.7)
    wind = bandpass(pink, np.clip(wander - 350.0, 120.0, None), wander + 350.0)
    wind *= 0.7 + 0.3 * np.sin(2.0 * math.pi * 0.11 * t + 2.1)

    # A quieter high hiss and a faint tarp flutter on top.
    hiss = one_pole_lowpass(rng.standard_normal(total), 7000.0) * 0.25
    flutter = bandpass(rng.standard_normal(total), 500.0, 2000.0)
    flutter *= (0.5 + 0.5 * np.sin(2.0 * math.pi * 6.5 * t)) * 0.18
    track = normalize(wind + hiss + flutter)

    # Seamless loop: crossfade the last 0.3 s into the first.
    fade = int(0.30 * SAMPLE_RATE)
    ramp = np.linspace(0.0, 1.0, fade)
    head = track[:fade].copy()
    track[:fade] = track[:fade] * ramp + track[-fade:] * (1.0 - ramp)
    track = track[:-fade]
    track = normalize(track)
    # The crossfade handles the loop point, so no edge fades here.
    return track


def loop_discontinuity(x):
    """Largest sample-to-sample jump across the loop point, in dBFS."""
    jump = abs(float(x[0]) - float(x[-1]))
    return 20.0 * math.log10(max(jump, 1e-9))


def write_wav(path, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    clipped = np.clip(samples, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype("<i2")
    with wave.open(path, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(pcm.tobytes())


def main():
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    default_dir = os.path.join(
        repo, "unity", "GliderSuitAssets", "Assets", "GliderSuit", "Audio"
    )
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out-dir", default=default_dir, help="output directory")
    parser.add_argument("--wind", action="store_true",
                        help="also write the procedural GliderWind.wav (replaces the recording, D30)")
    args = parser.parse_args()

    rng = np.random.default_rng(SEED)
    pieces = [
        ("GliderUnfold.wav", build_unfold(np.random.default_rng(SEED + 1))),
        ("GliderFold.wav", build_fold(np.random.default_rng(SEED + 2))),
        ("GliderWind.wav", build_wind(np.random.default_rng(SEED + 3))),
    ]
    for name, samples in pieces:
        if name == "GliderWind.wav" and not args.wind:
            continue
        path = os.path.join(args.out_dir, name)
        write_wav(path, samples)
        note = ""
        if name == "GliderWind.wav":
            note = ", loop point jump %.1f dBFS" % loop_discontinuity(samples)
        print("wrote %s (%.2f s, peak %.1f dBFS%s)" % (
            path, samples.shape[0] / SAMPLE_RATE,
            20.0 * math.log10(max(float(np.max(np.abs(samples))), 1e-9)), note))

    # Verify the loop by concatenating two copies and measuring the seam.
    wind = pieces[2][1]
    doubled = np.concatenate([wind, wind])
    seam = int(doubled.shape[0] / 2)
    worst = float(np.max(np.abs(np.diff(doubled[seam - 2:seam + 2]))))
    print("loop check: worst seam step %.4f (a click would be near 1.0)" % worst)


if __name__ == "__main__":
    main()
