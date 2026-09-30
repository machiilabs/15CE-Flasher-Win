# For Windows testers

One file, no installers, no .NET download.

## Download

**https://machiilabs.com/flasher#windows**

Click **Download free**, save the `.exe`, double-click it.

If Windows SmartScreen warns about an unknown publisher:

1. Click **More info**
2. Click **Run anyway**

(Windows builds are not code-signed yet.)

## First test: Connection Probe

1. Plug in the programming cable (USB).
2. Open **15CE Flasher**.
3. On the welcome screen, choose **Connection Probe**.
4. On the cable's switch box: **hold ERASE → press RESET → release ERASE**.
5. Pass: status shows **Connected: ATSAM4LC2C**.

If it stays on “Waiting…”, only FTDI may be visible — repeat ERASE+RESET.

## Requirements

- Windows 10 or 11 (64-bit)
- HP 15c Collector's Edition, HP 16c Collector's Edition, or post-2015 HP 12c
- Official USB pogo programming cable
- Nothing else to install (the `.exe` is self-contained)

## Support

Email **support@machiilabs.com** with screenshots of the app window and Device Manager COM ports if something fails.
