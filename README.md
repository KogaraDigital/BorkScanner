# BorkScanner

BorkScanner is a fast, multi-threaded CLI tool for scanning video files for corruption using FFmpeg. It supports full or quick scans, concurrent processing, and outputs detailed logs of any errors found.

Currently only for Linux, Windows version in progress.

---

## Features

- Full or fast scan modes
  - Full = Entire file
  - Fast = First frame only

- Multi-threaded file processing

- Limit concurrent FFmpeg processes

- Recursive directory scanning

- Produces detailed reports and splits files into
  - Major Errors (Unplayable)
  - Minor Errors (Issues but playable)
  - Clean (No issues)

- Self-contained single-file executable for Linux, Windows, or macOS

---

## Installation
### Download and make executable

``` bash
wget -L https://github.com/KogaraDigital/BorkScanner/releases/download/v0.0.1/BorkScanner
chmod +x BorkScanner
```
### Run
 ```bash
./BorkScanner
```

### Usage 
```bash
BorkScanner <directory> [full|fast] [--filethreads <int>] [--ffmpeginstances <int>] [--recursive|--norecursive]
```

### Arguments:
  - <directory>              Directory to scan (required)
  - full|fast                Scan mode. 'full' = entire file, 'fast' = first frame only (default: full)
  - --filethreads <int>       Number of file-processing threads (default: logical processors / 2)
  - --ffmpeginstances <int>   Max number of concurrent ffmpeg processes (default: 4)
  - --recursive               Scan subdirectories (default)
  - --norecursive             Disable scanning subdirectories


