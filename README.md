# KroModIx.Plugin.RenPyAssist

[![CI](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.RenPyAssist)](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/releases)

**Ren'Py Assist** — ordnerbasierter Update-Manager für Ren'Py-Spiele
mit direkter [f95zone.to](https://f95zone.to/)-Anbindung. Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx).

## Neu in v0.14.0
- **DE+EN-Übersetzung** aller User-facing Strings (218 Keys) — Tab-Labels,
  Buttons, Placeholders, Tooltips, Statusmeldungen, Notifications, Dialoge,
  Detail-Sektionen (Archive-Browser, Save-Editor, Mods-Pipeline, Settings,
  Cover-Crop, Translate-Setup, Rename-Config). Sprachwechsel im Host schaltet
  nach Kachel-Reselect live um.

## Ziel

Nicht Steam-Spiele mit `game/`-Marker-Ordner: Ren'Py-Adult-Visual-Novels
aus f95zone. Der User hat einen **Root-Ordner** mit vielen Container-
Unterordnern; das Plugin

- erkennt jeden Container mit `game/` oder Version-Sub-Ordnern
  (`MyGame-0.240-pc/game/`) als Ren'Py-Spiel,
- verknüpft jeden Container mit einem f95zone-Thread,
- pollt die Threads periodisch auf neue Versionen und zeigt einen
  Update-Badge auf der Sidebar-Kachel,
- installiert Update-ZIPs in einen neuen Sub-Ordner neben dem alten,
  **kopiert die Save-Games** aus `game/saves/` automatisch mit,
  löscht den alten Sub-Ordner und archiviert die ZIP in `archive/`,
- bietet einen **RPA-Archive-Browser** + **Save-Editor** mit
  **Screenshot-Timeline** (chronologische Thumbnail-Leiste, Klick
  selektiert den Save) und einen **Inline-Video-Player** (ffmpeg-MJPEG-
  Stream, kein LibVLC),
- rendert **animierte GIF-Cover** in der Detail-View via
  `Avalonia.Labs.Gif` (Sidebar-Kachel bleibt Standbild),
- führt eine **KrosteMod-Pipeline** aus, die aus dekompilierten `.rpy`-
  Dateien fertige Walkthrough-/Cheat-/Rename-Mods baut und ins `game/`
  deployt — mit **Choice-Auto-Expand** (if-Conditions als Requirements
  im Walkthrough-Hint) und **Konditional-Cheat-Markierung** (🔓 für
  Store-Vars die Choices freischalten).

## Aktivierung im Host

**Host-Wizard „🎮 Ordner mit Spielen scannen"**: Klick auf 🎮 in der
Sidebar → Ren'Py-Root wählen → Host scannt rekursiv nach `game/`-Marker
→ „N Ren'Py-Spiele gefunden. Importieren?" → **pro Spiel** entsteht
**eine eigene Sidebar-Kachel**, RenPyAssist übernimmt jede über engine-
basiertes Matching (`target.engine = "renpy"`).

Kein Steam-Bezug nötig. Jede Kachel ist ein eigener Container-Ordner
(mit `game/`-Marker oder Sub-Version-Ordnern), Plugin rendert für jede
eine Detail-View mit Cover, Version, Thread-URL, Actions.

**Braucht Host v1.10.3 oder neuer.**

## Tabs

### Übersicht

- **Cover** (aus f95zone via CoverCache, AVIF/GIF/WebP → PNG via ffmpeg-
  Thumbnail-Filter)
- Titel + Genre-Tags + KI-übersetzte Beschreibung (System-Locale)
- Lokale Version + Remote-Version + „zuletzt geprüft"

### Archive

RPA-Browser für `.rpa`-Files im `game/`-Verzeichnis. Zeigt Index-Baum,
Text-/Bild-/Video-Preview mit **Inline-Playback** (ffmpeg-MJPEG-Stream,
12 fps, kein Audio — zum Screening reicht das) oder externem Player.
Einzelnes File oder ganzes Archiv extrahieren.

### Saves

Ren'Py-Save-Editor (v0.4+): pickle-basierter Save-Reader mit
Screenshot-Preview.

### Mods (KrosteMod-Pipeline + Standalone-Decompiler)

Portiert aus [RenPack](https://github.com/Kroste/RenPack). Plugin
dekompiliert `.rpyc` in-process via **`Razorvine.Pickle`**-basiertem C#-
Decompiler (kein Python, cross-platform, Ren'Py 6.99–8.x, deckt Screens/
ATL/Transform/LayeredImage ab). Analysiert die `.rpy` und deployt einen
fertigen Mod ins `game/`-Verzeichnis mit Uninstall-Manifest.

**Buttons im Mods-Tab:**

- **▶ Bauen** — komplette KrosteMod-Pipeline für den gewählten Typ:
  - **Walkthrough** — Choice-Labels annotieren
  - **Cheat** — F11-Overlay mit Live-Editor für Store-Variablen
  - **Rename** — Character-Namen umbenennen (mit KI-Text-Rewrite falls
    konfiguriert)
  - **Translate** — KI-Batch-Übersetzung der Dialoge (via
    `IHostServices.Ai`)
- **🔓 .rpyc dekompilieren** (v0.13) — Standalone-Decompiler auf alle
  `.rpyc` im aktiven `game/`-Ordner, ohne KrosteMod-Build. Nutzt
  `skipUpToDate=true` (bereits vorhandene aktuelle `.rpy` werden
  übersprungen). Für User die Skripte lesen/modden wollen ohne einen
  der KrosteMod-Typen zu bauen.
- **🗑 Deinstallieren** — restauriert `.rpyc`-Backups aus dem Manifest.

### Einstellungen (plugin-global + spiel-spezifisch)

Spiel-spezifisch: Thread-URL, ▶ Start, ⬆ Update installieren, 🔄 Prüfen,
📂 Ordner, **✏ Ordner umbenennen** (Container-Rename via
`IHostServices.TryRenameManualGame`, .renpyassist/-Metadaten wandern mit),
🖼 Sidebar-Ausschnitt wählen.

Plugin-global: Downloads-Watch-Ordner, Check-Intervall, **f95zone-Login**
(User/Passwort → Session-Cookies verschlüsselt via Host-`ISecretProtection`
/ DPAPI / libsecret abgelegt; Passwort landet nie auf der Platte).

## Sub-Path-Rotation

Referenz: RenPack (`GamesRegistry.DetectRenpySubFolder`). Ein Container
kann direktes `game/` haben (Legacy) oder mehrere Version-Sub-Ordner mit
je eigenem `game/saves/`. Der Detektor priorisiert (1) direktes `game/`,
dann (2/3) den Sub-Ordner mit der höchsten extrahierbaren Version. Bei
Update wird `RenPyGame.ActiveSubPath` auf den neuen ZIP-Sub-Ordner
umgeschrieben, Saves aus dem alten in den neuen kopiert, alter Sub-Ordner
gelöscht, ZIP nach `<container>/archive/` verschoben.

## F95zone-Anbindung

- **Login**: CSRF-Token-Handshake (`/login/` → `_xfToken` extrahieren →
  POST `/login/login`), Session-Cookies (`xf_user`, `xf_session`)
  verschlüsselt gespeichert.
- **Thread-Metadata**: Titel aus `og:title`, Version-Regex auf `[vX.Y.Z]`,
  Cover aus dem ersten `attachments.f95zone.to/YYYY/MM/…`-Full-Size-Bild
  (ohne `thumb/`-Prefix), Description aus dem ersten Post-Body,
  Genre-Tags aus dem BB-Spoiler-Block.
- **Cover-Cache**: SHA256(URL) als Dateiname, AVIF/WebP → PNG via
  SixLabors.ImageSharp, animierte GIFs → repräsentatives Frame via
  `ffmpeg -vf thumbnail -frames:v 1` (statt starrer Frame 0, der bei
  Fade-In-Splash-Bildern oft weiß ist).
- **Robustheit**: alle Methoden geben bei Fehler leere Liste / null
  zurück — kein Crash im Host wenn f95zone-Layout sich ändert.

## Container-lokaler Metadaten-Store

Pro Container liegt in `<container>/.renpyassist/`:

- `game.json` — komplette Metadaten (ThreadUrl, CoverUrl, Description,
  Genres, KI-Übersetzungen, ActiveSubPath, LocalVersion)
- `cover.img` — Auto-Cover-Bild (PNG nach ffmpeg-Convert)
- `sidebar-cover.png` — User-Custom-Ausschnitt (2:3-Portrait, hat Vorrang
  in der Sidebar)

Vorteil: wenn der User seine Sammlung auf einen anderen PC überträgt oder
den Ordner umbenennt, kommt die Config automatisch mit.

## Installation

Aus [Release](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/releases)
das ZIP entpacken nach:

- **Linux:** `~/.config/KroModIx/plugins/kroste.renpyassist/`
- **Windows:** `%APPDATA%\KroModIx\plugins\kroste.renpyassist\`

Alternativ: 1-Klick-Install über die Install-Karte in der KroModIx-Sidebar.

**ffmpeg** wird für Video-Frame-Grab + Inline-Playback + AVIF/GIF-Cover-
Convert gebraucht:

- Fedora/Bazzite: `sudo dnf install ffmpeg-free`
- Debian/Ubuntu: `sudo apt install ffmpeg`
- Windows: `winget install ffmpeg`
- macOS: `brew install ffmpeg`

Ohne ffmpeg funktioniert das Plugin — Cover werden dann per ImageSharp
konvertiert (kein AVIF), Video-Preview zeigt Install-Hint, externer
Player läuft weiter.

## Erste Schritte

1. Plugin über die Sidebar-Install-Karte installieren.
2. In der Sidebar auf **🎮** (neben „➕ Spiel hinzufügen") klicken.
3. Root-Ordner deiner Ren'Py-Sammlung wählen → **🔍 Scannen** →
   Host meldet „Ren'Py: N Spiele gefunden" → **Spiele importieren**.
4. Die Sidebar hat jetzt **eine Kachel pro Ren'Py-Spiel**. Anklicken →
   Detail-View mit den Tabs (Übersicht / Archive / Saves / Mods /
   Einstellungen).
5. Optional: Einstellungen-Tab → **f95zone-Login** eintragen (für Cover-
   Downloads bei login-required Threads).
6. Pro Spiel: f95zone-Thread-URL im Feld einfügen, 💾 klicken → Version-
   Poll + Cover-Download + Description-KI-Übersetzung laufen im
   Hintergrund.

## Referenz

- **RenPack** (`github.com/Kroste/RenPack`) — Original-Referenz für
  Sub-Path-Rotation, f95zone-Integration, KrosteMod-Pipeline,
  Media-Preview mit ffmpeg-MJPEG-Stream. Ren'Py Assist portiert die
  Kern-Logik in die KroModIx-Plugin-Architektur.
- **F95zone** — [f95zone.to](https://f95zone.to/) (Forum + Attachment-
  Hosting).

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
