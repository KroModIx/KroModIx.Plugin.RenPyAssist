using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Row-DTO für die Card-Liste im Games-Tab. Wraps ein
/// <see cref="RenPyGame"/> mit UI-Convenience-Properties (Cover-Bitmap,
/// Update-Badge-Text, StatusText).</summary>
public sealed partial class GameRow : ObservableObject
{
    public RenPyGame Game { get; }

    [ObservableProperty]
    private Bitmap? _cover;

    [ObservableProperty]
    private string _threadUrlDraft;

    public GameRow(RenPyGame game)
    {
        Game = game;
        _threadUrlDraft = game.ThreadUrl ?? "";
    }

    public string DisplayName => Game.DisplayName;
    public string ContainerPath => Game.ContainerPath;

    public string SubPathText => string.IsNullOrEmpty(Game.ActiveSubPath)
        ? "(Legacy-Layout)" : Game.ActiveSubPath!;

    public string VersionText => Game.LocalVersion is null
        ? "lokal: (?)" : $"lokal: {Game.LocalVersion}";

    public string RemoteText => Game.LastRemoteVersion is null
        ? "remote: —" : $"remote: {Game.LastRemoteVersion}";

    public string ThreadShort => string.IsNullOrEmpty(Game.ThreadUrl)
        ? "kein f95zone-Thread verknüpft" : Game.ThreadUrl!;

    public bool HasThread => !string.IsNullOrEmpty(Game.ThreadUrl);
    public bool HasUpdate => Game.HasUpdate;
    public string UpdateBadgeText => Game.LastRemoteVersion is null
        ? "↑ Update" : $"↑ {Game.LastRemoteVersion}";

    public void SetCoverFromPath(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path))
            {
                Cover = null;
                return;
            }
            using var s = File.OpenRead(path);
            Cover = new Bitmap(s);
        }
        catch
        {
            Cover = null;
        }
    }

    /// <summary>Muss nach Registry-Mutation aufgerufen werden, damit die
    /// UI die aktualisierten Wrapped-Properties re-liest.</summary>
    public void RaiseAll()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SubPathText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(RemoteText));
        OnPropertyChanged(nameof(ThreadShort));
        OnPropertyChanged(nameof(HasThread));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBadgeText));
    }
}
