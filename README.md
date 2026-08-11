# KroModIx.Plugin.RenPyAssist

[![CI](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.RenPyAssist)](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/releases)

**Ren'Py Assist** — ordnerbasierter Update-Manager für Ren'Py-Spiele
mit direkter [f95zone.to](https://f95zone.to/)-Anbindung. Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx).

## Ziel

Nicht Steam-Spiele mit ihrem `game/`-Marker-Ordner:
Ren'Py-Adult-Visual-Novels aus f95zone. Der User hat einen
**Root-Ordner** mit vielen Container-Unterordnern; das Plugin

- erkennt jeden Container mit `game/` oder Version-Sub-Ordnern
  (`MyGame-0.240-pc/game/`) als Ren'Py-Spiel,
- verknüpft jeden Container mit einem f95zone-Thread,
- pollt die Threads periodisch auf neue Versionen und zeigt einen
  Update-Badge auf der Sidebar-Kachel,
- installiert Update-ZIPs in einen **neuen Sub-Ordner** neben dem alten
  und **kopiert die Save-Games** aus `game/saves/` automatisch mit —
  Sub-Path-Rotation-Pattern aus RenPack.

## Aktivierung im Host

Ab v0.2.0: **Host-Wizard „🎮 Ordner mit Spielen scannen"** (Host v1.8.0+).
Klick auf das 🎮-Icon in der Sidebar → Ren'Py-Root wählen → Host scannt
rekursiv nach `game/`-Marker → „N Ren'Py-Spiele gefunden. Importieren?"
→ Sammel-Kachel entsteht in der Sidebar, RenPyAssist übernimmt.

Kein Steam-Bezug nötig. Der Root-Ordner kommt vom Wizard direkt ins
Plugin — das Plugin-Settings-Root-Feld ist nur noch Backward-Compat-
Fallback für User die pre-v0.2 den Root manuell gesetzt hatten.

**Braucht Host v1.8.0 oder neuer.**

## Features (v0.2.0)

### Spiele-Tab
- Card-Liste aller registrierter Ren'Py-Spiele mit Cover, aktuellem
  Sub-Ordner, lokaler + remote Version
- **🔄 Rescan** merged Filesystem gegen Registry (neue Container rein,
  verschwundene raus, f95zone-Metadata bleibt bei bestehenden erhalten)
- **⬇ Updates prüfen** pollt alle verknüpften Threads sofort
  (Rate-Limit 1 s zwischen Requests)
- Inline-TextBox pro Row zum Setzen der f95zone-Thread-URL
- **▶ Start** öffnet den Ren'Py-Launcher (`*.sh` auf Linux, `*.exe` auf
  Windows) im aktiven Sub-Ordner
- **⬆ Update installieren** entpackt eine ZIP in einen neuen Sub-Ordner,
  kopiert Save-Games mit, rotiert die aktive Version — der alte
  Sub-Ordner bleibt liegen (Safety-Net)
- **📂 Ordner** öffnet den aktiven Sub-Path im Datei-Explorer

### Einstellungen-Tab
- Ren'Py-Root-Ordner (Pflicht)
- Downloads-Watch-Ordner (Default `~/Downloads`) — überwacht ZIPs
- Update-Check-Intervall (Default 60 min, min 15 min)
- **f95zone-Login** (User/Passwort → Session-Cookies verschlüsselt via
  Host-`ISecretProtection` / DPAPI / libsecret abgelegt; Passwort landet
  nie auf der Platte)

### IUpdateNotifier
Grüner ↑-Badge auf der Sidebar-Kachel: Summe aller Spiele mit
verfügbarem Update. Tooltip zeigt die genaue Zahl.

## Sub-Path-Rotation

Referenz: RenPack (`GamesRegistry.DetectRenpySubFolder`). Ein Container
kann direktes `game/` haben (Legacy) oder mehrere Version-Sub-Ordner
mit je eigenem `game/saves/`. Der Detektor priorisiert (1) direktes
`game/`, dann (2/3) den Sub-Ordner mit der höchsten extrahierbaren
Version. Bei Update wird nur `RenPyGame.ActiveSubPath` umgeschrieben —
der neue ZIP-Sub-Ordner wird zur aktiven Version, der alte bleibt bis
zur manuellen Aufräumung liegen.

## F95zone-Anbindung

- **Login**: CSRF-Token-Handshake (`/login/` → `_xfToken` extrahieren →
  POST `/login/login`), Session-Cookies (`xf_user`, `xf_session`)
  verschlüsselt gespeichert.
- **Thread-Metadata**: Titel aus `og:title`, Version-Regex auf
  `[vX.Y.Z]`, Cover aus dem ersten
  `attachments.f95zone.to/YYYY/MM/…`-Full-Size-Bild.
- **Cover-Cache**: SHA256(URL) als Dateiname, AVIF/WebP → PNG via
  SixLabors.ImageSharp (keine ffmpeg-Abhängigkeit).
- **Robustheit**: alle Methoden geben bei Fehler leere Liste / null
  zurück — kein Crash im Host wenn f95zone-Layout sich ändert.

## Installation

Aus [Release](https://github.com/KroModIx/KroModIx.Plugin.RenPyAssist/releases)
das ZIP entpacken nach:

- **Linux:** `~/.config/KroModIx/plugins/kroste.renpyassist/`
- **Windows:** `%APPDATA%\KroModIx\plugins\kroste.renpyassist\`

Alternativ: 1-Klick-Install über die Install-Karte in der KroModIx-Sidebar
(sobald `KroModIx.PluginIndex` den Eintrag hat).

## Erste Schritte

1. Plugin über die Sidebar-Install-Karte installieren
   (oder Release-ZIP manuell entpacken — siehe unten).
2. In der Sidebar auf **🎮** (neben „➕ Spiel hinzufügen") klicken.
3. Root-Ordner deiner Ren'Py-Sammlung wählen → **🔍 Scannen** →
   Host meldet „Ren'Py: N Spiele gefunden" → **Sammlung importieren**.
4. Neue Sidebar-Kachel „Ren'Py Games" anklicken → **Spiele-Tab** zeigt
   deine Spiele als Cards.
5. Optional: **Einstellungen-Tab** → **f95zone-Login** eintragen
   (für Cover-Downloads).
6. Pro Spiel: f95zone-Thread-URL in die Inline-TextBox einfügen,
   💾 klicken → Version-Poll läuft sofort.

## Referenz

- **RenPack** (`github.com/Kroste/RenPack`) — Original-Referenz für
  Sub-Path-Rotation und f95zone-Integration. Ren'Py Assist portiert die
  Kern-Logik in die KroModIx-Plugin-Architektur.
- **F95zone** — [f95zone.to](https://f95zone.to/) (Forum + Attachment-
  Hosting für Ren'Py-Adult-Games).

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
