# KroModIx.Plugin.RenPyAssist

## Grundlagen

- **Was:** Ordner-basierter Update-Manager für Ren'Py-Spiele als Plugin
  für KroModIx, mit f95zone.to-Anbindung. Kein Steam-Match — Aktivierung
  läuft über Proton-Experimental (Steam AppId 1493710) als Anchor bis der
  Host in v0.2 einen ordner-basierten Discovery-Contract bekommt.
- **Stack:** .NET 10, `KroModIx.Plugin.Contracts` v1.7.0 als
  PackageReference. `SixLabors.ImageSharp` für AVIF/WebP → PNG.
- **Repo:** `github.com/KroModIx/KroModIx.Plugin.RenPyAssist`.
- **Deploy-Ziel:** `~/.config/KroModIx/plugins/kroste.renpyassist/`
  bzw. `%APPDATA%\KroModIx\plugins\kroste.renpyassist\`.

## Architektur

- **Services/**:
  - `RenPyPaths` — Data/Cache/Cover/Cookies-Dirs.
  - `RenPyGame` — Registry-Record mit Sub-Path-Rotation-Semantik
    (ContainerPath, ActiveSubPath, LocalVersion, ThreadUrl,
    LastRemoteVersion, CoverUrl, DisplayNameOverride).
  - `RenPyGameDetector` — Scan mit `game/`-Marker-Erkennung. Prio 1
    direktes `game/`, Prio 2/3 Version-Sub-Ordner. Version-Extract via
    Regex `[-_\s.v]?(\d+(?:\.\d+)+[a-z0-9]*)`.
  - `GamesRegistry` — persistente `games.json` mit Sync-Merge in
    `Rescan`: neue Container hinzu, gelöschte raus, bestehende behalten
    f95zone-Metadata, aktualisieren nur ActiveSubPath/LocalVersion/Name.
    Emit `Changed`-Event fürs UI. `PendingUpdatesCount` für
    `IUpdateNotifier`.
  - `RenPySettings` + `RenPySettingsService` — Settings mit atomarem
    File-Move.
  - `F95zoneClient` — CSRF-Login + Search + Thread-Info (Titel via
    `og:title`, Version via Bracket-Regex `[vX.Y]`, Cover via
    Attachment-Full-Size-Regex) + Cover-Download.
  - `F95zoneSessionStore` — Cookies verschlüsselt via `ISecretProtection`
    (DPAPI/libsecret). Passwort landet nie auf der Platte.
  - `CoverCache` — SHA256(URL)-Filename + Magic-Byte-Check +
    ImageSharp-Convert für AVIF/WebP.
  - `RenPyWorker` — Bootstrap 30 s Delay, dann Poll-Loop mit
    konfigurierbarem Intervall (Default 60 min, min 15 min), Rate-Limit
    1 s zwischen Thread-Fetches.
  - `DownloadWatcher` — FileSystemWatcher auf `~/Downloads` (default),
    2 s Stability-Timer bevor `StableZipDetected` feuert.
  - `GameUpdateInstaller` — Sub-Path-Rotation: ZIP entpacken → Diff-Detect
    neuer Sub-Ordner → Save-Games aus altem in neuen kopieren → Registry
    aktualisieren. Alter Sub-Ordner bleibt liegen (Safety-Net).

- **Views/**:
  - `GamesView` + `GamesViewModel` + `GameRow` — Card-Liste (Cover 120×160,
    Titel, Sub-Path, Version-Row, Inline-TextBox für Thread-URL, Action-
    Buttons). Cover-Load off-UI-Thread via `Dispatcher.UIThread.Post`.
  - `SettingsView` + `SettingsViewModel` — Root/Downloads/Interval-Setup,
    f95zone-Login-Formular.

- **RenPyAssistPlugin** — Entry-Point, orchestriert die Services,
  restauriert Cookies beim Startup, triggert Initial-Rescan im
  Hintergrund, startet Worker + DownloadWatcher.

## v0.1.0 — was drin ist

- Games-Tab (Card-Liste, Rescan/Update-Check/Install-Update/Play/
  Ordner/Remove).
- Settings-Tab (Root, Downloads-Watch, Interval, f95zone-Login).
- Sub-Path-Rotation komplett (Detect + Install + Save-Copy).
- f95zone-Login mit verschlüsselter Cookie-Persistenz.
- Cover-Cache mit AVIF/WebP → PNG via ImageSharp.
- Worker pollt in Intervall + `CheckNowAsync` für „Jetzt prüfen".
- DownloadWatcher meldet stabile ZIPs als Notification.
- IUpdateNotifier feuert Sidebar-Badge (Summe HasUpdate).

## v0.2 — Roadmap

- **Ordner-basierter Discovery-Contract im Host** — `IFolderGameProvider`
  o. ä., damit die Registry-Einträge direkt als Sidebar-Kacheln erscheinen
  statt via Proton-Anchor.
- **Auto-Match-Dialog**: neue Container mit ähnlichem Namen → Vorschlag
  aus f95zone-Search, User klickt „✓ verknüpfen".
- **Download-Zuordnung**: stabile ZIP im Downloads-Ordner → Auto-Match
  gegen bekannte Games (Name-Fuzzy) → Vorschlag „⬆ Install als Update
  für X".
- **DownloadLinkExtractor** (analog RenPack) — Thread-HTML nach
  Mega/Workupload/Pixeldrain-Links parsen, im UI anzeigen.
- **Versions-Historie pro Game** — alte Sub-Ordner aufräumen-Button.

## Kernkonzepte

- **Sub-Path-Rotation:** ein Container-Ordner hält mehrere Version-Sub-
  Ordner. Der aktive steht in `RenPyGame.ActiveSubPath`. `game/saves/`
  lebt in jedem Sub-Ordner separat — daher der Copy beim Update.
- **f95zone-Robustheit:** kein öffentliches API → HTML-Regex. Alle
  Methoden failsafe (leere Liste / null bei Fehler), keine Crashes im
  Host.
- **Cookie-Verschlüsselung:** Cookies über `ISecretProtection` (Windows
  DPAPI, Linux libsecret/AES). Klartext-Cookies liegen NIE auf der Platte.
  Passwort wird zur Login-Anfrage benutzt, nie persistiert.

## Referenz

- **RenPack** (`github.com/Kroste/RenPack` und
  `external/RenPack.Plugins.F95zone/`) — Original-Referenz für alle
  Kern-Konzepte (Sub-Path-Rotation, f95zone-Client, CoverCache,
  DownloadWatcher). Ren'Py Assist portiert diese Muster in die
  KroModIx-Plugin-Architektur.
- **Kroste-Plugin-Skill:** `~/.claude/skills/KroModIx-Plugin/` — Struktur-
  Konventionen, Kernprinzip 6 (Bulk-Install / Post-Install-Refresh /
  Bulk-Update-All).
- **Vorbild-Plugins:** LS25 (Metadata-Cache-Pattern) und Satisfactory
  (Card-Layout + IUpdateNotifier).

## Bekannte Grenzen

- **v0.1.0 hat keine ordner-basierte Discovery** — der Proton-Anchor ist
  Placeholder bis der Host in v0.2 einen `IFolderGameProvider`-Contract
  bekommt. Ohne installiertes Proton Experimental taucht die Kachel nicht
  in der Sidebar auf.
- **Kein Download-→Game-Matching** — der DownloadWatcher meldet stabile
  ZIPs nur per Notification; „⬆ Update installieren" braucht manuelle
  ZIP-Auswahl über File-Picker.
- **Kein Enable/Disable pro Spiel** — Update-Install ist immer aktiv.
  Alte Sub-Ordner bleiben liegen und können manuell im Filesystem
  weggeräumt werden.
