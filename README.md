# BorkScanner

BorkScanner is a fast, multi-threaded, multi-platform, CLI tool for scanning video files for corruption using FFmpeg. It supports scanning full files or only checking first frame for speed, concurrent processing, and outputs detailed logs of any errors found.

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

---

## Installation

### NuGet (Recommended)
Requires [.NET SDK](https://dotnet.microsoft.com/en-us/download) installed.
```bash
dotnet tool install --global BorkScanner
```
[NuGet website](https://www.nuget.org/packages/BorkScanner/)

### Wget

``` bash
wget -L https://github.com/KogaraDigital/BorkScanner/releases/download/v0.1.4/BorkScanner
chmod +x BorkScanner
```

## Usage 
```bash
BorkScanner <directory> [full|fast] [--filethreads <int>] [--ffmpeginstances <int>] [--recursive|--norecursive]

  - <directory>               Directory to scan (required)
  - full|fast                 Scan mode. 'full' = entire file, 'fast' = first frame only (default: full)
  - --filethreads <int>       Number of file-processing threads (default: logical processors / 2)
  - --ffmpeginstances <int>   Max number of concurrent ffmpeg processes (default: 4)
  - --recursive               Scan subdirectories (default)
  - --norecursive             Disable scanning subdirectories
```

## Example
Scan the first frame of all videos in ~/Videos recursively, using 2 file threads and limiting FFmpeg to 3 concurrent instances:
```bash
BorkScanner ~/Videos fast --filethreads 2 --ffmpeginstances 3 --recursive
```


