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
### Download

``` bash
wget -L https://github.com/KogaraDigital/BorkScanner/releases/download//BorkScanner
chmod +x BorkScanner
```

### Extract

```bash
tar xzvf BorkScanner.v0.0.1_linux.tar.gz
```

### Run
 ```bash
./BorkScanner
```

No chmod is needed if using the tarball.


