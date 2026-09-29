using System.Globalization;
using System.Text.Json;

namespace FifteenCEFlasherCore;

/// <summary>One firmware the app recognizes by its test-menu checksum.</summary>
public sealed record KnownFirmwareEntry(ushort Checksum, string Model, string FileName, string Description)
{
    /// <summary>"HP 15c Collector’s Edition", "HP 16c Collector’s Edition", or "HP 12c".</summary>
    public string ModelName => $"HP {Model}";

    /// <summary>Model and version, for example "HP 15c Collector’s Edition original firmware". Description never repeats the model.</summary>
    public string DisplayName => $"{ModelName} {Description}";
}

/// <summary>
/// The known-firmware list in known-firmware.json (embedded). The Mac app keeps
/// an identical copy, so edit the JSON rather than adding entries here.
/// </summary>
public static class KnownFirmware
{
    public static readonly string[] Models = ["15c Collector’s Edition", "16c Collector’s Edition", "12c"];

    private static readonly Lazy<IReadOnlyList<KnownFirmwareEntry>> Entries = new(Load);

    public static IReadOnlyList<KnownFirmwareEntry> All => Entries.Value;

    public static KnownFirmwareEntry? Find(ushort checksum) =>
        All.FirstOrDefault(entry => entry.Checksum == checksum);

    private static IReadOnlyList<KnownFirmwareEntry> Load()
    {
        using var stream = typeof(KnownFirmware).Assembly.GetManifestResourceStream("known-firmware.json")
            ?? throw new InvalidOperationException("known-firmware.json is not embedded.");
        return Parse(stream);
    }

    internal static IReadOnlyList<KnownFirmwareEntry> Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = new List<KnownFirmwareEntry>();
        foreach (var item in document.RootElement.GetProperty("firmware").EnumerateArray())
        {
            var text = item.GetProperty("checksum").GetString() ?? "";
            var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
            if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var checksum))
                throw new FormatException($"Known firmware checksum \"{text}\" is not a 4-digit hex value.");

            var model = item.GetProperty("model").GetString() ?? "";
            if (!Models.Contains(model))
                throw new FormatException($"Known firmware model \"{model}\" must be one of {string.Join(", ", Models)}.");

            var fileName = item.TryGetProperty("fileName", out var name) ? name.GetString() ?? "" : "";
            if (fileName.Length == 0 || !fileName.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                throw new FormatException($"Known firmware fileName \"{fileName}\" may use only lowercase letters, digits, and hyphens.");

            if (entries.Any(entry => entry.Checksum == checksum))
                throw new FormatException($"Known firmware checksum {text} is listed twice.");

            entries.Add(new KnownFirmwareEntry(checksum, model, fileName, item.GetProperty("description").GetString() ?? ""));
        }
        return entries;
    }
}
