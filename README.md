# OANS Auto Zoom (MSFS 2024)

Ein Community-Addon für den Microsoft Flight Simulator 2024: Kurz nach der Landung blendet
es auf beiden Navigation Displays automatisch die Flughafenkarte (OANS) ein und zoomt so weit
hinein, dass Flughafen und eigenes Flugzeug gut zu sehen sind. Du musst dafür nichts tun.
Vorbild ist das Verhalten des iniBuilds A380.

## Unterstützte Flugzeuge

| Flugzeug | Was das Addon nach der Landung macht |
| -------- | ------------------------------------ |
| FlyByWire A380X | ND-Modus ROSE ILS/VOR → ARC, dann Range-Wahlschalter auf ZOOM 2 NM (beide Seiten) |

Andere Flugzeuge ignoriert das Addon komplett.

## Installation

1. Das Repository als ZIP herunterladen (GitHub: **Code → Download ZIP**) und entpacken.
2. Den entpackten Ordner in den Community-Ordner von MSFS 2024 verschieben:
   - bevorzugt in **`Community2024`** (gibt es seit Sim Update 4), sonst in **`Community`**.
   - Microsoft Store / Xbox-App:
     `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community2024`
   - Steam: `%APPDATA%\Microsoft Flight Simulator 2024\Packages\Community2024`
   - Hast du den Pfad bei der Installation geändert, steht er in der Datei `UserCfg.opt`
     (Zeile `InstalledPackagesPath`), oder du öffnest ihn im Sim über
     *Marketplace → My Library → Einstellungen (Zahnrad)*.
3. **Wichtig:** `manifest.json` muss direkt in diesem Ordner liegen, also
   `Community2024\<Ordnername>\manifest.json` und nicht eine Ebene tiefer.
   Am besten benennst du den Ordner kurz um, z. B. in `oans-autozoom`.
4. Sim neu starten. Community-Pakete werden nur beim Start eingelesen.

## So verhält es sich

- Das Addon schaltet sich erst scharf, wenn das Flugzeug mindestens 30 Sekunden höher als
  100 ft über Grund geflogen ist. Rollen, Startlauf und kurze Hüpfer lösen nichts aus.
- Nach dem Aufsetzen (2 Sekunden am Boden) wartet es, bis die Groundspeed unter 80 kt fällt,
  höchstens aber 15 Sekunden. Dann blendet es das OANS ein.
- Es greift **einmal pro Landung** ein. Danach bleibt das Display komplett in deiner Hand,
  bis zum nächsten Flug.
- Bounces und Touch-and-Go ohne Abbremsen unter 80 kt lösen nichts aus.

## Fehlersuche

- **Nichts passiert:** Prüfe unter *Marketplace → My Library*, ob das Paket „OANS Auto Zoom“
  aktiviert ist. MSFS 2024 deaktiviert Community-Pakete manchmal von selbst.
- **FBW A380X zeigt „OANS nicht verfügbar“:** Das OANS braucht die Navigraph-Anbindung im
  flyPad. Ohne sie lässt das Addon das Display bewusst unverändert.
- **Log ansehen:** Im Entwicklermodus zeigt *Debug → Console* alle Meldungen mit dem
  Präfix `[OansAutoZoom]`, z. B. welches Flugzeug erkannt wurde und was gesendet wurde.

## Aufbau

| Pfad | Inhalt |
| ---- | ------ |
| `manifest.json`, `layout.json`, `modules/oans_autozoom.wasm` | das eigentliche MSFS-Paket |
| [`src/OansAutoZoom.cpp`](src/OansAutoZoom.cpp) | Quellcode des WASM-Moduls |
| [`tests/host/`](tests/host/) | Tests der Logik ohne Simulator (Landung, Bounce, Touch-and-Go usw.) |
| [`tools/`](tools/) | SDK herunterladen, Modul bauen, `layout.json` erzeugen |
| [`docs/`](docs/) | Recherche: wie die einzelnen Flugzeuge intern angesteuert werden |

## Selbst bauen

Unter Linux oder WSL (braucht `clang`, `lld`, `python3`, `curl`, `unzip`, `msitools`):

```sh
tools/get-msfs-sdk.sh                    # lädt das offizielle MSFS 2024 SDK 1.7.3 von Microsoft
export MSFS_SDK="$PWD/.msfs-sdk/MSFS 2024 SDK"
tools/build.sh                           # baut modules/oans_autozoom.wasm und layout.json
tests/host/run.sh                        # Logik-Tests, brauchen nur einen C++-Compiler
```

Die Compiler-Optionen entsprechen denen des offiziellen „MSFS2024“-Toolsets aus dem SDK.
