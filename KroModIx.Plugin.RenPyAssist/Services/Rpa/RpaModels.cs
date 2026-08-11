using System.Collections.Generic;
using System.Linq;

namespace KroModIx.Plugin.RenPyAssist.Services.Rpa;

/// <summary>Unterstützte Ren'Py-Archiv-Formate.</summary>
public enum RpaVersion
{
    /// <summary>RPA-2.0 — Index ohne XOR-Verschleierung, kein Key.</summary>
    V2_0,
    /// <summary>RPA-3.0 — Offsets/Längen mit 32-bit-Key XOR-verschleiert (Standard).</summary>
    V3_0,
    /// <summary>RPA-3.2 — wie 3.0, Header mit zusätzlichem Feld (<c>RPA-3.2 &lt;offset&gt; 0 &lt;key&gt;</c>).</summary>
    V3_2,
}

/// <summary>Ein zusammenhängender Datenabschnitt einer archivierten Datei. Die meisten
/// Dateien bestehen aus genau einem Segment; das Format erlaubt aber mehrere.
/// Bytes eines Segments: <c>Prefix + Archivbytes[Offset .. Offset + Length - Prefix.Length]</c>.</summary>
public sealed record RpaSegment(long Offset, long Length, byte[] Prefix)
{
    public long BytesFromArchive => Length - Prefix.Length;
}

public sealed record RpaEntry(string Path, IReadOnlyList<RpaSegment> Segments)
{
    public long Size => Segments.Sum(s => s.Length);
}

public static class RpaVersionExtensions
{
    public static string ToDisplay(this RpaVersion v) => v switch
    {
        RpaVersion.V2_0 => "RPA-2.0",
        RpaVersion.V3_0 => "RPA-3.0",
        RpaVersion.V3_2 => "RPA-3.2",
        _ => v.ToString(),
    };
}

public sealed record RpaArchiveInfo(
    string ArchivePath,
    RpaVersion Version,
    uint Key,
    IReadOnlyList<RpaEntry> Entries)
{
    public long TotalSize => Entries.Sum(e => e.Size);
}
