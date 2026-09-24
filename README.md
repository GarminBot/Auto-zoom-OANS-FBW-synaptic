# OANS Auto-Zoom für den FlyByWire A380X (MSFS 2024)

Ziel: ein Addon für den Community-Ordner des Microsoft Flight Simulator 2024, das den
Zoom des OANS (Onboard Airport Navigation System) im FlyByWire A380X automatisch an die
Rollgeschwindigkeit anpasst und nach dem Start die vorherige ND-Range wiederherstellt.

> **Status:** Recherche und Startgerüst. Der Code ist gegen die MSFS-API geschrieben und
> die Logik ist mit einem Host-Test geprüft, er wurde aber **noch nicht im Simulator getestet**.

## Inhalt

| Pfad | Inhalt |
| ---- | ------ |
| [`docs/fbw-a380x-oans-zoom.md`](docs/fbw-a380x-oans-zoom.md) | Analyse des FBW-Codes: wie der OANS-Zoom intern gesteuert wird, welche Events und L-Vars es gibt, Fallstricke |
| [`src/OansAutoZoom.cpp`](src/OansAutoZoom.cpp) | Standalone-WASM-Modul mit der Auto-Zoom-Logik (Startgerüst) |
| [`tests/host/`](tests/host/) | Logik-Test ohne Simulator: simuliert Rollen, Start, Landung und manuelle Eingaben |

## Logik testen (ohne Simulator)

Braucht nur einen C++17-Compiler (g++ oder clang++):

```sh
tests/host/run.sh
```

## Lizenz

Noch nicht festgelegt.
