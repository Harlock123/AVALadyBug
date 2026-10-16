#!/usr/bin/env python3
"""Synthesises AVALadyBug's sound effects and tunes into ../src/AVALadyBug/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVALadyBug", "Sounds")
RNG = random.Random(1981)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # A flower eaten: a tiny bright blip.
    write("Dot.wav", tone(0.035, 1500, 1900, "tri", attack=0.002, release=1.4), 0.2)
    write("Letter.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.3) for n in ["E6", "G6", "C7"]]), 0.3)
    write("Heart.wav", cat(tone(0.08, hz("A5"), kind="sine", release=0.4), tone(0.16, hz("E6"), kind="sine", release=0.6)), 0.34)
    # A gate swinging: a creak and a clack.
    write("Gate.wav", cat(tone(0.12, 300, 520, "saw", vibrato=0.08, vib_rate=40, release=0.6, gain=0.4), tone(0.04, 1200, 700, "noise", release=1.5, gain=0.6)), 0.26)
    # A bug let out: a buzz rising. A bug gone on a skull: a pop and a fall.
    write("Release.wav", tone(0.45, 160, 420, "saw", vibrato=0.12, vib_rate=50, release=0.5, gain=0.6), 0.24)
    write("BugDown.wav", cat(tone(0.06, 1400, 600, "noise", release=1, gain=0.7), tone(0.3, 900, 120, "square", release=0.6, gain=0.4)), 0.3)
    write("Skull.wav", cat(tone(0.5, 220, 70, "saw", vibrato=0.1, vib_rate=7, release=0.5), tone(0.4, 70, 40, "sine", release=0.6)), 0.42)
    write("Caught.wav", cat(tone(0.08, 600, 300, "square", release=0.6), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4", "E4"]]), 0.42)
    write("Vegetable.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]]), 0.34)
    write("Special.wav", tune(160, [("C6", 0.5), ("E6", 0.5), ("G6", 0.5), ("C7", 1), ("G6", 0.5), ("C7", 2)], [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 0.5), ("C3", 1.5)], kind="pulse"), 0.42)
    write("Extra.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "C6", "E6", "G6", "C7"]]), 0.38)
    write("LevelStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.25) for n, d in [("G5", 0.1), ("E5", 0.1), ("C6", 0.3)]]), 0.3)
    write("LevelClear.wav", tune(150, [("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 1), ("D6", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 0.5), ("C3", 1.5)], kind="pulse"), 0.42)

    # In the garden: a light skipping tune (an original composition), a whole number of beats long so it loops.
    lead = [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("E5", 0.5), ("F5", 0.5), ("A5", 0.5), ("G5", 1),
            ("E5", 0.5), ("C5", 0.5), ("D5", 0.5), ("E5", 0.5), ("D5", 2),
            ("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("B5", 0.5), ("G5", 0.5), ("A5", 1),
            ("G5", 0.5), ("E5", 0.5), ("D5", 0.5), ("B4", 0.5), ("C5", 2)]
    bass = [("C3", 1), ("G2", 1), ("F2", 1), ("C3", 1), ("A2", 1), ("F2", 1), ("G2", 2),
            ("C3", 1), ("E2", 1), ("F2", 1), ("A2", 1), ("G2", 1), ("G2", 1), ("C3", 2)]
    melody = tune(140, lead, bass, kind="pulse")
    write("Music.wav", melody, 0.28)

    # The title: the tune, a second strain, and a march under it.
    title_lead = lead + [("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("C5", 1), ("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("D5", 0.5), ("E5", 2),
                         ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("G5", 1), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("B4", 0.5), ("C5", 2)]
    title_bass = bass + [("F2", 1), ("F2", 1), ("C3", 1), ("C3", 1), ("G2", 1), ("G2", 1), ("C3", 2),
                         ("F2", 1), ("F2", 1), ("C3", 1), ("E2", 1), ("G2", 1), ("G2", 1), ("C3", 2)]
    write("TitleTune.wav", cat(mix(tune(140, title_lead, title_bass, kind="pulse"), drums(140, 8)), silence(1.0)), 0.4)
    write("StartTune.wav", tune(150, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("C6", 1)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.42)
    write("GameOver.wav", tune(70, [("C5", 1), ("B4", 1), ("A4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("E2", 2), ("F2", 2), ("A2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
