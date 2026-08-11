using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Ein registriertes Ren'Py-Spiel. Persistiert in <c>games.json</c>.
///
/// <para><b>Sub-Path-Rotation:</b> RenPack-Pattern — der Container-Ordner
/// (z. B. „A Wife and Mother") kann mehrere Version-Sub-Ordner enthalten
/// (<c>Sophia_Parker...-0.230-pc/</c>, <c>...-0.240-pc/</c>). Der aktive
/// Sub-Ordner steht in <see cref="ActiveSubPath"/>. Bei Update wird nur
/// die Property umgeschrieben — der neue Sub-Ordner ist entpackt, der alte
/// bleibt bis zur manuellen Aufräumung liegen. <c>game/saves/</c> lebt in
/// jedem Sub-Ordner separat.</para></summary>
public sealed class RenPyGame
{
    /// <summary>Sichtbarer Name (Container-Ordnername als Default,
    /// f95zone-Titel überschreibt via User-Wahl).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Absoluter Pfad zum Container-Ordner (User-Root/name/).</summary>
    [JsonPropertyName("containerPath")]
    public string ContainerPath { get; set; } = "";

    /// <summary>Aktueller Version-Sub-Ordner (relativer Pfad im Container).
    /// null wenn das Spiel direkt im Container liegt (Legacy).</summary>
    [JsonPropertyName("activeSubPath")]
    public string? ActiveSubPath { get; set; }

    /// <summary>Lokale Version, aus dem Sub-Ordner-Namen extrahiert.</summary>
    [JsonPropertyName("localVersion")]
    public string? LocalVersion { get; set; }

    /// <summary>URL zum f95zone-Thread (vom User oder per Suche gesetzt).</summary>
    [JsonPropertyName("threadUrl")]
    public string? ThreadUrl { get; set; }

    /// <summary>Letzte bekannte Version aus dem f95zone-Thread.</summary>
    [JsonPropertyName("lastRemoteVersion")]
    public string? LastRemoteVersion { get; set; }

    [JsonPropertyName("lastCheckedUtc")]
    public DateTime? LastCheckedUtc { get; set; }

    /// <summary>Cover-URL (aus f95zone oder User-Override).</summary>
    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    /// <summary>Optional User-Override für den Anzeige-Namen.</summary>
    [JsonPropertyName("displayNameOverride")]
    public string? DisplayNameOverride { get; set; }

    /// <summary>Kurzbeschreibung aus dem f95zone-Thread (Overview-Sektion).
    /// Wird vom Worker beim Thread-Check aktualisiert. v0.5+.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Genre-Tags aus dem f95zone-Thread (z. B. "3DCG", "Romance",
    /// "Corruption"). v0.5+.</summary>
    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    /// <summary>KI-übersetzte Beschreibung (Sprach-Code → Text). Cache pro
    /// Locale, damit nicht bei jedem View-Open neu übersetzt wird. v0.5+.</summary>
    [JsonPropertyName("descriptionTranslations")]
    public Dictionary<string, string> DescriptionTranslations { get; set; } = new();

    /// <summary>Lokal gecachter Cover-Pfad im Container (relativ zum
    /// Container-Ordner: <c>.renpyassist/cover.img</c>). v0.5+.</summary>
    [JsonPropertyName("localCoverPath")]
    public string? LocalCoverPath { get; set; }

    [JsonIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(DisplayNameOverride) ? DisplayNameOverride! : Name;

    /// <summary>Hat der Remote-Thread eine neuere Version als lokal?</summary>
    [JsonIgnore]
    public bool HasUpdate => !string.IsNullOrWhiteSpace(LastRemoteVersion)
                          && !string.IsNullOrWhiteSpace(LocalVersion)
                          && !string.Equals(LastRemoteVersion, LocalVersion, StringComparison.OrdinalIgnoreCase);
}
