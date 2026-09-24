#!/usr/bin/env python3
"""Generates a small, fully tagged test music library (sine tones + cover art) with ffmpeg.

Usage: generate-test-library.py <output-dir>
Used for integration tests against a real Subsonic server (Navidrome / Supysonic).
"""
import os
import subprocess
import sys

LIBRARY = [
    # artist, album, year, genre, [(track title, seconds, frequency)]
    ("Aurora Lights", "Northern Skies", 2019, "Ambient", [("Polar Night", 12, 220), ("Solar Wind", 10, 247), ("Magnetosphere", 14, 262)]),
    ("Aurora Lights", "Deep Blue", 2021, "Ambient", [("Abyss", 11, 294), ("Current", 9, 330)]),
    ("The Test Tones", "Frequencies", 2015, "Electronic", [("Four Forty", 8, 440), ("Octave Up", 8, 880), ("Low End", 10, 110), ("Middle C", 9, 262)]),
    ("Björk Sample", "Ünïcödé Album", 2003, "Pop", [("Æther", 7, 349), ("Ça va", 7, 392)]),
    ("Various Numbers", "1999", 1999, "Rock", [("One", 6, 523), ("Two", 6, 587), ("Three", 6, 659)]),
]


def run(cmd):
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "test-library"
    os.makedirs(out, exist_ok=True)
    colors = ["0x3b82f6", "0x22c55e", "0xef4444", "0xf59e0b", "0xa855f7"]
    for index, (artist, album, year, genre, tracks) in enumerate(LIBRARY):
        album_dir = os.path.join(out, artist, album)
        os.makedirs(album_dir, exist_ok=True)
        cover = os.path.join(album_dir, "cover.jpg")
        if not os.path.exists(cover):
            run(["ffmpeg", "-y", "-f", "lavfi", "-i", f"color=c={colors[index % len(colors)]}:s=500x500",
                 "-frames:v", "1", cover])
        for track_no, (title, seconds, freq) in enumerate(tracks, start=1):
            path = os.path.join(album_dir, f"{track_no:02d} - {title}.mp3")
            if os.path.exists(path):
                continue
            run(["ffmpeg", "-y",
                 "-f", "lavfi", "-i", f"sine=frequency={freq}:duration={seconds}",
                 "-i", cover,
                 "-map", "0:a", "-map", "1:v",
                 "-c:a", "libmp3lame", "-b:a", "128k", "-c:v", "mjpeg",
                 "-id3v2_version", "3",
                 "-metadata:s:v", "title=Album cover", "-metadata:s:v", "comment=Cover (front)",
                 "-metadata", f"title={title}", "-metadata", f"artist={artist}",
                 "-metadata", f"album_artist={artist}", "-metadata", f"album={album}",
                 "-metadata", f"date={year}", "-metadata", f"genre={genre}",
                 "-metadata", f"track={track_no}/{len(tracks)}",
                 path])
    print(f"Test library generated in {out}")


if __name__ == "__main__":
    main()
