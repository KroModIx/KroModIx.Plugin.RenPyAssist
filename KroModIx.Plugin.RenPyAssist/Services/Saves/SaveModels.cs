using System;
using System.Collections.Generic;

namespace KroModIx.Plugin.RenPyAssist.Services.Saves;

/// <summary>Kurz-Metadaten aus dem <c>json</c>-Eintrag eines Ren'Py-Saves.</summary>
public sealed record SaveMetadata(
    string? SaveName,
    DateTimeOffset? SaveTime,
    string? RenpyVersion,
    string? GameName,
    IReadOnlyDictionary<string, object?> Raw);

/// <summary>Eine einzelne Store-Variable aus einem Save.</summary>
public sealed record SaveVariable(string Name, string TypeName, string Value, bool IsInternal);

/// <summary>Ergebnis des Save-Ladens (read-only Inspector).</summary>
public sealed record SaveInfo(
    string SavePath,
    SaveMetadata Metadata,
    byte[]? ScreenshotBytes,
    IReadOnlyList<SaveVariable> Variables,
    string? LogError);

/// <summary>Eine geplante Änderung einer einzelnen Save-Variable.</summary>
/// <param name="Name">Variablen-Kurzname (ohne <c>store.</c>-Präfix).</param>
/// <param name="NewValue">Neuer Wert — muss ein von <see cref="PicklePatcher.EncodeValue"/>
/// unterstützter Typ sein (int/long/double/string/bool/null oder flat/geschachtelte
/// Liste/Dict/Tuple).</param>
public sealed record SaveEdit(string Name, object? NewValue);
