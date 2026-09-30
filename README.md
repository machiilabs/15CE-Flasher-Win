# 15CE Flasher — Windows

Windows version of **15CE Flasher**, a guided firmware flasher for post-2015 Voyager calculators: the HP 15c Collector's Edition, HP 16c Collector's Edition, and post-2015 HP 12c. Separate repo from the Mac app, [HP15C-Flasher](https://github.com/machiilabs/HP15C-Flasher).

## Download

**https://machiilabs.com/flasher#windows** — one self-contained `.exe`, nothing else to install.

Testers: see [docs/TESTERS.md](docs/TESTERS.md).

## Features

- **FLASH wizard** — 7 steps: cable, programming mode, check and back up the current firmware, choose firmware, flash and verify, restart, checksum test
- **Known firmware** — names the firmware on the calculator and in the chosen file from `known-firmware.json`, and warns when a file is for a different model
- **BATCH mode** — flash many calculators in a row with the same firmware
- **DEMO mode** — in-app simulator, no hardware
- **Connection Probe** — finds the Atmel SAM-BA port (`03EB:6124`), ignores FTDI (`0403:6015`), reads the chip ID

## Requirements

- **Users:** Windows 10/11 x64 and the official USB pogo programming cable
- **Mac developers:** .NET 8 SDK (`brew install dotnet@8`) for the core library and tests

## For developers (Mac)

| Task | Where |
|------|--------|
| Edit protocol / flash logic | Mac — `FifteenCEFlasherCore` + unit tests |
| Build the shippable `.exe` | **GitHub Actions** (`windows-latest`) — push, open a pull request, or **Run workflow** |
| Test USB / cable | Windows PC + hardware |

See [docs/RELEASE.md](docs/RELEASE.md).

## Known firmware

`src/FifteenCEFlasherCore/known-firmware.json` lists publicly released firmware by the checksum the calculator shows in its test menu (2.C): model, default backup file name, and description. The Mac app keeps an identical copy in `Sources/HP15CFlasherCore/known-firmware.json`; change both together.

## Protocol

SAM-BA 2.16 monitor + official `applet-flash-sam4l4.bin`. Application flash at `0x4000`, 112 KB (`0x1C000`). Ported from the Mac `HP15CFlasherCore`.

## License

Copyright © Mach II Labs. SAM-BA applet: Atmel Corporation (see `THIRD_PARTY.md`).
