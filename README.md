# OANS Auto Zoom (MSFS 2024)

Ein Community-Addon für den Microsoft Flight Simulator 2024. Direkt nach der Landung blendet es
auf beiden Navigation Displays automatisch die Flughafenkarte ein und zoomt so weit hinein, dass
Flughafen und eigenes Flugzeug gut zu sehen sind. Du musst dafür nichts tun.

Vorbild sind der iniBuilds A380 („OANS Auto Zoom: ND automatically changes to the zoom range on
touchdown“) und der echte A350 („At landing, the ND automatically displays the ANF in ARC mode,
with a 2 NM range“, FCOM).

## Unterstützte Flugzeuge

| Flugzeug | Was nach der Landung passiert | Kartendaten für die Flughafenkarte |
| -------- | ----------------------------- | ---------------------------------- |
| **FlyByWire A380X** | beide NDs: Modus ARC, Range ZOOM 2 NM (OANS) | Navigraph oder [AMDB Bridge](https://github.com/Vihaan2012-cmyk/Free-Airport-Mapping-DB) (Setup-Option „A350 and A380X“) |
| **iniBuilds A350** | beide NDs: Modus ARC, Range ZOOM 2 NM (ANF), F/O-Seite 2 s nach dem Captain | Navigraph oder AMDB Bridge (Setup-Option „A350 and A380X“) |
| **Synaptic A220** | beide MAP-Displays: Range 1 NM (Airport Moving Map) | Synaptic A220 **v1.0.10 oder neuer** (eigene Flughafenkarte) **oder** die A220-Karte von AMDB Bridge (Setup-Option „A220 moving map“, Paket `zzz-amdb-a220-amm`). Ohne beides zeigt der A220 in diesem Bereich „AIRPORT MAP FAULT“. |

Mit AMDB Bridge: Starte AMDB Bridge **vor** dem Laden des Flugzeugs. Der FBW A380X fragt nur
beim Laden nach, ob es Karten gibt.

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

**Update von einer älteren Version:** Sim beenden, den alten Ordner im Community-Ordner
löschen (der mit `manifest.json`, Titel „OANS Auto Zoom“) und den neuen hineinlegen. Nicht
beide Versionen gleichzeitig drin lassen.

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

- **Log-Datei:** Das Addon schreibt alles, was es tut, in `oans_autozoom.log` (bei jedem
  Sim-Start neu):
  - Microsoft Store / Xbox-App:
    `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024\<Ordnername>\work\oans_autozoom.log`
  - Steam: `%APPDATA%\Microsoft Flight Simulator 2024\WASM\MSFS2024\<Ordnername>\work\oans_autozoom.log`

  `<Ordnername>` ist der Name des Addon-Ordners im Community-Ordner. So liest du das Log:

  | Letzte Zeile im Log | Bedeutung |
  | ------------------- | --------- |
  | keine Datei | Das Modul wurde nicht geladen: Ordner falsch verschachtelt (`manifest.json` muss direkt im Ordner liegen) oder Paket deaktiviert |
  | `initialised, waiting for an aircraft` | Modul läuft, aber noch kein Flug geladen |
  | `aircraft "…": not supported` | Flugzeug nicht erkannt. Titel und Pfad stehen in der Zeile, bitte melden |
  | `aircraft "…": FlyByWire A380X` (o. ä.) | Flugzeug erkannt, wartet auf den Abflug |
  | `armed for the next landing` | in der Luft, wartet auf die Landung |
  | `landing confirmed …` und `send …` | Landung erkannt, Befehle gesendet |
  | `… after: …` | Stand der Displays 2 s danach (FBW, A350) |

- **Es passiert gar nichts:** Prüfe unter *Marketplace → My Library*, ob das Paket
  „OANS Auto Zoom“ aktiviert ist. MSFS 2024 deaktiviert Community-Pakete manchmal von selbst.
  Im Entwicklermodus zeigt auch *Debug → Console* alle Meldungen (Präfix `[OansAutoZoom]`).
- **FBW A380X, im Log `L:A32NX_OANS_AVAILABLE = 0`:** Das Addon stellt das ND trotzdem auf
  ARC und ZOOM 2 NM, aber das OANS hatte beim Laden des Flugzeugs keine Kartendaten. Starte
  AMDB Bridge (bzw. verknüpfe Navigraph) vor dem Laden des Flugzeugs.
- **A220 zeigt „AIRPORT MAP FAULT“:** Es ist keine Flughafenkarte installiert: A220 v1.0.10
  oder neuer, oder die A220-Karte von AMDB Bridge (und AMDB Bridge muss laufen).
- **iniBuilds A350:** Der A350 hat eine eigene Option dafür („autozoom“ im OIS). Ist sie an,
  ist die Karte beim Auslösen schon da und das Addon lässt sie so. Beides zusammen stört sich
  nicht.

## Status

Version 1.1.0. Das Modul ist mit Compiler und Linker aus dem offiziellen MSFS 2024 SDK 1.7.3
gebaut (`clang-cl.exe`, `wasm-ld.exe`), mit den Optionen des offiziellen
„MSFS2024“-Visual-Studio-Toolsets. Die komplette Logik ist mit Host-Tests geprüft. Mangels
Windows-Rechner mit MSFS ist es noch **nicht in einem laufenden Simulator getestet**. Welche
Annahmen dabei offen sind, steht in [docs/aircraft-profiles.md](docs/aircraft-profiles.md).

Änderungen in 1.1.0:

- FBW A380X: Version 1.0.0 hat nur umgestellt, wenn FBW `L:A32NX_OANS_AVAILABLE` = 1 meldete.
  Mit AMDB Bridge statt Navigraph kann der Wert 0 bleiben, dann passierte nichts. Jetzt wird
  immer umgestellt.
- A380X wird auch an seinem MSFS-2020-Ordnernamen erkannt.
- Log-Datei `oans_autozoom.log` (siehe Fehlersuche).
- Gebaut mit der offiziellen SDK-Toolchain statt mit einem freien clang.

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

`tools/build.sh` ruft `clang-cl.exe` und `wasm-ld.exe` aus dem SDK auf: unter Windows (Git
Bash) direkt, unter Linux mit Wine. Unter Linux braucht es `wine64`, `python3`, `curl`,
`unzip` und `msitools`:

```sh
tools/get-msfs-sdk.sh                    # lädt das offizielle MSFS 2024 SDK 1.7.3 von Microsoft
export MSFS_SDK="$PWD/.msfs-sdk/MSFS 2024 SDK"
tools/build.sh                           # baut modules/oans_autozoom.wasm und layout.json
tests/host/run.sh                        # Logik-Tests, brauchen nur einen C++-Compiler
```

Unter Windows mit installiertem SDK: `MSFS_SDK="C:/MSFS 2024 SDK" tools/build.sh` in Git Bash.
