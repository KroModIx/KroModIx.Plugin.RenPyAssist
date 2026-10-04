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

## Stand

Die maßgebliche Feature-Liste steht in der `description` in `plugin.json` —
sie wird bei jedem Release mitgepflegt und ist damit die einzige Stelle, die
nicht veralten kann. Ergänzend die GitHub-Releases des Repos.

Hier bewusst keine Versions-Momentaufnahme: die vorherige Fassung dieser Datei
beschrieb noch v0.1.0, während das Repo längst deutlich weiter war.

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

## Archive kommen aus dem Host (ab v0.23.0)

`GameUpdateInstaller` bekommt `IHostServices.Archives`; SharpCompress ist aus
dem Plugin verschwunden. Die drei anderen Pakete bleiben plugin-eigen —
AVIF/WebP-Cover (ImageSharp), Python-Pickle und animierte GIFs bringt der
Host nicht mit.

**Wichtiger als die gesparte Abhängigkeit: ein Ausbruchsversuch bricht den
Einbau ab.** Der eigene Schutz war richtig gerechnet, übersprang den
abgelehnten Eintrag aber **still** und ließ den Einbau weiterlaufen. Hier
war das schlimmer als in den anderen Plugins: die Schritte danach (neuen
Unterordner ermitteln, Spielstände kopieren, alten Unterordner löschen)
arbeiteten dann auf einem halb entpackten Stand — und am Ende wird der alte
Unterordner **samt Spielständen** gelöscht. Jetzt bricht der Einbau ab,
bevor irgendetwas am alten Stand passiert, und die Meldung sagt das auch
(„Der alte Stand samt Spielständen ist unangetastet.").

**Was bewusst nicht migriert ist:** `RenpySaveService`. Der **erzeugt**
ZIP-Dateien (Spielstand-Bündel), und dafür hat der Host-Baukasten keine
Schnittstelle — er liest und packt aus, er packt nicht ein. Dasselbe gilt
für Icarus' `PakBackupService` und LS25' `ModBackupService`.

**Der Installer war ungetestet.** Jetzt sechs Tests: beide Layouts
(Versions-Unterordner und flaches Archiv), dass die Spielstände mitwandern,
beide Ausbruch-Fälle mit der Zusage, dass der alte Stand stehen bleibt, die
Inhaltsprüfung und der Endungs-Vorfilter. Die Tests nutzen
`KroModIx.Plugin.TestKit` und bleiben bei `Assert` statt FluentAssertions —
das ist die Konvention dieses Testprojekts.

## Der alte Unterordner wird nicht gelöscht, wenn er ein Verweis ist (ab v0.24.0)

Nach einem Einbau löscht `GameUpdateInstaller` den alten
Versions-Unterordner — **rekursiv**, im Spielordner des Nutzers, und dort
liegen Spielstände. Das ist die destruktivste Stelle im ganzen Plugin.

Seit v0.24.0 bleibt er stehen, wenn `ForeignManagerDetection` ihn als Verweis
erkennt. Wer seine Fassungen bewusst auf eine andere Platte verlinkt, soll
dort nicht überrascht werden.

**Warum nur diese eine Stelle und nicht alle zehn.** Nachgezählt: von den zehn
löschenden Pfaden greift genau **einer** ins Verzeichnis des Nutzers. Die
anderen neun sind Cover-Zwischenspeicher, Einstellungen, f95zone-Sitzung und
Temp-Dateien — alles plugin-eigen. Und anders als bei den acht
Mod-Plugins liefert in einen Ren'Py-Spielordner kein konkurrierender
Mod-Manager aus; die Prüfung dient hier dem verlinkenden Nutzer, nicht einem
fremden Werkzeug.
