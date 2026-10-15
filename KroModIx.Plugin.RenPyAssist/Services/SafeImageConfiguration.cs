using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Die ImageSharp-Konfiguration für alles, was dieses Plugin lädt —
/// mit genau den Decodern, die wirklich vorkommen.
///
/// <para><b>Warum nicht die Standard-Konfiguration:</b> <c>Image.Load</c>
/// erkennt das Format am Inhalt und zieht dafür jeden registrierten Decoder
/// in Betracht, auch solche, die das Plugin nie braucht. GHSA-wmxv-xphr-5c9g
/// (Oktober 2026) sitzt im BigTIFF-Decoder: 24 Byte genügen, um einen Thread
/// in eine Schleife zu schicken, die nicht vorankommt. Cover kommen von
/// f95zone und damit nicht aus vertrauenswürdiger Quelle — der Decoder war
/// also erreichbar.</para>
///
/// <para>Behoben ist die Lücke erst in ImageSharp 4.1.2, und 4.x verlangt für
/// Release-Builds eine kostenpflichtige Lizenz. Für ein frei verteiltes
/// Plugin keine Option. Den Decoder nicht zu registrieren schließt den Pfad
/// genauso — und spart nebenbei Startzeit.</para>
///
/// <para>Cover sind PNG, JPEG, GIF oder WebP; BMP ist für vom User selbst
/// gewählte Bilder dabei. Kommt irgendwann ein Format dazu, gehört es hier
/// hinein und nicht in einen zweiten <c>Image.Load</c>-Aufruf ohne
/// Konfiguration.</para></summary>
public static class SafeImageConfiguration
{
    private static readonly Configuration Instance = CreateConfiguration();

    /// <summary>Für <c>Image.Load(DecoderOptions, …)</c>.</summary>
    public static DecoderOptions Decoder { get; } = new() { Configuration = Instance };

    private static Configuration CreateConfiguration()
        => new(
            new PngConfigurationModule(),
            new JpegConfigurationModule(),
            new GifConfigurationModule(),
            new WebpConfigurationModule(),
            new BmpConfigurationModule());
}
