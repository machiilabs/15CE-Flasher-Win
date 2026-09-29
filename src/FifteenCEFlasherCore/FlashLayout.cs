namespace FifteenCEFlasherCore;

/// <summary>ATSAM4L flash map used by the HP 15c Collector's Edition.</summary>
public static class FlashLayout
{
    public const uint BootloaderStart = 0x0000;
    public const uint BootloaderSize = 0x4000;
    public const uint ApplicationStart = 0x4000;
    public const uint ApplicationSize = 0x1C000;

    public static uint ApplicationEnd => ApplicationStart + ApplicationSize;

    public static int ExpectedFirmwareByteCount => (int)ApplicationSize;

    public static bool IsSafeApplicationRange(uint address, uint length)
    {
        if (length == 0 || address < ApplicationStart)
            return false;
        var end = (ulong)address + length;
        return end <= ApplicationEnd;
    }
}
