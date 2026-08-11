using System;
using System.IO;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Persistente, verschlüsselte Ablage der f95zone-Session-Cookies
/// im Plugin-Data-Dir. Nutzt den Host-<see cref="ISecretProtection"/>-Service
/// (Windows DPAPI, Linux/macOS AES mit User-Bindung). Klartext-Cookies liegen
/// NIE auf der Platte.</summary>
public sealed class F95zoneSessionStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _path;
    private readonly ISecretProtection _secrets;

    public F95zoneSessionStore(string cookiesPath, ISecretProtection secrets)
    {
        _path = cookiesPath;
        _secrets = secrets;
    }

    /// <summary>Speichert die Cookies verschlüsselt. Leerer Blob löscht die
    /// Datei (User-Logout).</summary>
    public void Save(string cookieBlob)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cookieBlob))
            {
                if (File.Exists(_path)) File.Delete(_path);
                return;
            }
            var protectedBlob = _secrets.Protect(cookieBlob);
            if (protectedBlob is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, protectedBlob);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Session-Store Save fehlgeschlagen");
        }
    }

    /// <summary>Lädt und entschlüsselt die Cookies. Null wenn keine Datei
    /// existiert oder Entschlüsselung fehlschlägt (dann muss der User neu
    /// einloggen).</summary>
    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var protectedBlob = File.ReadAllText(_path);
            return _secrets.Unprotect(protectedBlob);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Session-Store Load fehlgeschlagen");
            return null;
        }
    }
}
