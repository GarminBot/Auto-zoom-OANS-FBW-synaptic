# OANS Auto Zoom (MSFS 2024)

Ein Community-Addon für den Microsoft Flight Simulator 2024. Direkt nach der Landung blendet es
auf beiden Navigation Displays automatisch die Flughafenkarte ein und zoomt so weit hinein, dass
Flughafen und eigenes Flugzeug gut zu sehen sind. Du musst dafür nichts tun.

Vorbild sind der iniBuilds A380 („OANS Auto Zoom: ND automatically changes to the zoom range on
touchdown“) und der echte A350 („At landing, the ND automatically displays the ANF in ARC mode,
with a 2 NM range“, FCOM).

## Unterstützte Flugzeuge

| Flugzeug | Was nach der Landung passiert | Voraussetzung |
| -------- | ----------------------------- | ------------- |
| **FlyByWire A380X** | beide NDs: Modus ARC, Range ZOOM 2 NM (OANS) | Navigraph im flyPad verknüpft (sonst gibt es kein OANS, dann ändert das Addon nichts) |
| **iniBuilds A350** | beide NDs: Modus ARC, Range ZOOM 2 NM (ANF), F/O-Seite 2 s nach dem Captain | Navigraph (für die ANF-Karten) |
| **Synaptic A220** | beide MAP-Displays: Range 1 NM (Airport Moving Map) | **Synaptic A220 v1.0.10 oder neuer**, die erste Version mit Flughafenkarte (mit Navigraph vollständig, ohne nur Pisten). Bis v1.0.9 zeigt der A220 in diesem Bereich „AIRPORT MAP FAULT“. |

Hat eine Seite schon eine ZOOM-Stufe gewählt, bleibt diese. Andere Flugzeuge ignoriert das
Addon komplett.

## Installation

1. Auf GitHub **Code → Download ZIP** und die ZIP-Datei entpacken.
2. Den entpackten Ordner in den Community-Ordner von MSFS 2024 verschieben, bevorzugt
   **`Community2024`** (gibt es seit Sim Update 4), sonst **`Community`**:
   - Microsoft Store / Xbox-App:
     `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community2024`
   - Steam: `%APPDATA%\Microsoft Flight Simulator 2024\Packages\Community2024`
   - Anderer Installationsort: steht in `UserCfg.opt` (Zeile `InstalledPackagesPath`). Im Sim
     findest du ihn unter *Marketplace → My Library → Einstellungen (Zahnrad)*.
3. **Wichtig:** `manifest.json` muss direkt in diesem Ordner liegen, also
   `Community2024\<Ordner>\manifest.json`, nicht eine Ebene tiefer. Benenne den Ordner am besten
   kurz um, z. B. in `oans-autozoom`.
4. Sim neu starten. Community-Pakete werden nur beim Start eingelesen.

## So verhält es sich

- **Scharf** schaltet es sich erst, wenn das Flugzeug mindestens 15 s höher als 100 ft über
  Grund war. Rollen, Startlauf und ein abgebrochener Start lösen also nichts aus.
- **Landung** heißt: 3 Sekunden ununterbrochen am Boden und beim Aufsetzen mindestens 40 kt
  schnell. Ein Bounce setzt die Zählung zurück. Wird das Flugzeug dagegen nur auf den Boden
  gesetzt (Slew, Versetzen ans Gate), gilt das nicht als Landung.
- Es greift **einmal pro Landung** ein und fasst die Displays danach bis zum nächsten Flug nicht
  mehr an. Beim Start passiert nichts.
- Nach dem Einblenden kann der Sim beim ersten Laden einer Flughafenkarte kurz stocken
  (bis zu einige Sekunden). Das liegt an den Flugzeugen, nicht am Addon.

## Fehlersuche

- **Es passiert gar nichts:** Prüfe unter *Marketplace → My Library*, ob das Paket
  „OANS Auto Zoom“ aktiviert ist. MSFS 2024 deaktiviert Community-Pakete manchmal von selbst.
- **Log ansehen:** Im Entwicklermodus zeigt *Debug → Console* alle Meldungen mit dem
  Präfix `[OansAutoZoom]`: welches Flugzeug erkannt wurde, wann es scharf geschaltet hat, wann
  es die Landung erkannt hat und welche Befehle gesendet wurden.
- **FBW A380X, Meldung „OANS not available“:** Das OANS braucht die Navigraph-Anbindung im
  flyPad.
- **A220 zeigt „AIRPORT MAP FAULT“:** Deine A220-Version hat noch keine Flughafenkarte. Update
  auf v1.0.10 oder neuer.
- **iniBuilds A350:** Der A350 hat eine eigene Option dafür („autozoom“ im OIS). Ist sie an,
  ist die Karte beim Auslösen schon da und das Addon lässt sie so. Beides zusammen stört sich
  nicht.

## Status

Das Modul ist mit dem offiziellen MSFS 2024 SDK 1.7.3 gebaut, das zur aktuellen Retail-Version
(Sim Update 6) gehört. Die Compiler-Optionen entsprechen dem offiziellen „MSFS2024“-Toolset.
Die komplette Logik ist mit Host-Tests geprüft. Mangels Windows-Rechner mit MSFS ist es noch
**nicht in einem laufenden Simulator getestet**. Welche Annahmen dabei offen sind, steht in
[docs/aircraft-profiles.md](docs/aircraft-profiles.md).

## Aufbau des Repositorys

Das Repository ist selbst das MSFS-Paket. `layout.json` listet nur `modules/`, alles andere
lädt der Simulator nicht.

| Pfad | Inhalt |
| ---- | ------ |
| `manifest.json`, `layout.json`, `modules/oans_autozoom.wasm` | das eigentliche Paket |
| [`src/OansAutoZoom.cpp`](src/OansAutoZoom.cpp) | Quellcode des WASM-Moduls |
| [`tests/host/`](tests/host/) | Tests der Logik ohne Simulator: alle drei Flugzeuge, Bounce, Touch-and-Go, Flugzeugwechsel usw. |
| [`tools/`](tools/) | SDK herunterladen, Modul bauen, `layout.json` erzeugen |
| [`docs/aircraft-profiles.md`](docs/aircraft-profiles.md) | wie die drei Flugzeuge angesteuert werden, mit Quellen |
| [`docs/fbw-a380x-oans-zoom.md`](docs/fbw-a380x-oans-zoom.md) | Analyse des FBW-A380X-Quellcodes |
| [`docs/msfs2024-addon-leitfaden.md`](docs/msfs2024-addon-leitfaden.md) | wie man ein Community-Addon für MSFS 2024 baut |

## Selbst bauen

Unter Linux oder WSL (braucht `clang`, `lld`, `python3`, `curl`, `unzip` und `msitools`):

```sh
tools/get-msfs-sdk.sh                    # lädt das offizielle MSFS 2024 SDK 1.7.3 von Microsoft
export MSFS_SDK="$PWD/.msfs-sdk/MSFS 2024 SDK"
tools/build.sh                           # baut modules/oans_autozoom.wasm und layout.json
tests/host/run.sh                        # Logik-Tests, brauchen nur einen C++-Compiler
```
