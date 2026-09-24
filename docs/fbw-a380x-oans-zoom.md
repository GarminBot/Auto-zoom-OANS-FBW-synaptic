# FBW A380X: So wird der OANS-Zoom intern gesteuert

Analyse des FlyByWire-Quellcodes (`flybywiresim/aircraft`, Branch `master`,
Commit [`2baa2b35`](https://github.com/flybywiresim/aircraft/tree/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2)
vom 20.09.2026). Grundlage für ein Addon, das den OANS-Zoom automatisch
verstellt. Alle Aussagen sind mit Quellstellen belegt. FBW kann das jederzeit
ändern, deshalb vor jedem Release gegenprüfen.

## Kurzfassung

- Das OANS erscheint auf dem ND, wenn der **Range-Wahlschalter des EFIS-CP auf einer
  ZOOM-Stufe** steht **und** der ND-Modus **ROSE NAV, ARC oder PLAN** ist.
- ZOOM-Stufen und normale ND-Ranges sind **ein einziger Drehschalter mit 12 Positionen (0–11)**.
- **Steuern** über die Custom-Events `A32NX.FCU_EFIS_L_RANGE_SET` / `A32NX.FCU_EFIS_R_RANGE_SET`
  (Wert 0–11) bzw. `…_RANGE_INC` / `…_RANGE_DEC`.
- **Lesen** über `L:A32NX_EFIS_L_ND_RANGE`, `L:A32NX_EFIS_L_OANS_RANGE`, `L:A32NX_EFIS_L_ND_MODE`
  (`_R_` für die rechte Seite) und `L:A32NX_OANS_AVAILABLE`.
- **Die L-Vars nicht direkt schreiben.** Sie sind Ausgänge der FCU-Simulation und
  werden von `fbw.wasm` in jedem Frame überschrieben. Ein direkt geschriebener Wert
  hält höchstens einen Frame.

## Die 12 Schalterpositionen

Die FCU-Simulation kennt diese Positionen
([`A380FcuComputer_types.h` Z. 21–35](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/model/A380FcuComputer_types.h#L21-L35)):

| Wert für `…_RANGE_SET` | Bedeutung       | danach `L:A32NX_EFIS_x_ND_RANGE` | danach `L:A32NX_EFIS_x_OANS_RANGE` |
| ---------------------- | --------------- | -------------------------------- | ---------------------------------- |
| 0                      | ZOOM 0,2 NM     | 0 (= ZOOM)                       | 0                                  |
| 1                      | ZOOM 0,5 NM     | 0                                | 1                                  |
| 2                      | ZOOM 1 NM       | 0                                | 2                                  |
| 3                      | ZOOM 2 NM       | 0                                | 3                                  |
| 4                      | ZOOM 5 NM       | 0                                | 4                                  |
| 5                      | 10 NM           | 1                                | 5 (= kein Zoom)                    |
| 6                      | 20 NM           | 2                                | 5                                  |
| 7                      | 40 NM           | 3                                | 5                                  |
| 8                      | 80 NM           | 4                                | 5                                  |
| 9                      | 160 NM          | 5                                | 5                                  |
| 10                     | 320 NM          | 6                                | 5                                  |
| 11                     | 640 NM          | 7                                | 5                                  |

Belege für die Spalten:

- `L:A32NX_EFIS_x_ND_RANGE` ist ein Index in `[-1, 10, 20, 40, 80, 160, 320, 640]`, wobei
  `-1` für ZOOM steht
  ([`NavigationDisplay.ts` Z. 19](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-common/src/systems/instruments/src/NavigationDisplay.ts#L19)).
- `L:A32NX_EFIS_x_OANS_RANGE` ist ein Index in `[0.2, 0.5, 1, 2, 5]` NM. `5` heißt „kein Zoom“
  ([`Oanc.tsx` Z. 95](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-common/src/systems/instruments/src/OANC/Oanc.tsx#L95),
  [`FlyByWireInterface.cpp` Z. 2540–2573](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/FlyByWireInterface.cpp#L2540-L2573)).
- Die OANS-Karte übernimmt die Zoomstufe direkt aus `oansRange`
  ([`Oanc.tsx` Z. 430–432](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-common/src/systems/instruments/src/OANC/Oanc.tsx#L430-L432)).

## Wann ist das OANS sichtbar?

```ts
// fbw-a380x/src/systems/instruments/src/ND/instrument.tsx, Z. 397 ff.
if (this.efisCpRange === -1 && [EfisNdMode.PLAN, EfisNdMode.ARC, EfisNdMode.ROSE_NAV].includes(this.efisNdMode)) {
  // nd_show_oans = true
}
```

Die Werte von `L:A32NX_EFIS_x_ND_MODE` sind `0` ROSE ILS, `1` ROSE VOR, `2` ROSE NAV, `3` ARC und `4` PLAN
([`A380FcuComputer_types.h` Z. 7–14](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/model/A380FcuComputer_types.h#L7-L14)).
Den Modus stellst du mit `A32NX.FCU_EFIS_L_MODE_SET` (gleiche Werte) oder `…_MODE_INC/DEC` um.

Außerdem braucht das OANS Kartendaten von Navigraph. `L:A32NX_OANS_AVAILABLE` wird nur `1`,
wenn der Abruf der Navigraph-Flughafendatenbank klappt und der ARPT-NAV-Reset nicht gezogen ist
([`OansControlPanel.tsx` Z. 102–106 und 335](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/systems/instruments/src/ND/OansControlPanel.tsx#L102-L106)).

## Warum direktes Schreiben der L-Vars nicht funktioniert

`fbw.wasm` berechnet die EFIS-Werte in jedem Frame aus den Ausgängen der FCU-Simulation und
schreibt sie in die L-Vars (der „FCU Shim“):

```cpp
// fbw-a380x/src/wasm/fbw_a380/src/FlyByWireInterface.cpp, Z. 2602 ff.
const auto oansRangeLeft = getOansRange(/* Bits 19–23 aus efis_discrete_word_1 */);
idFcuShimLeftNdRange->set(getNdRange(/* Bits 24–29 */, oansRangeLeft != 5));
idFcuShimLeftNdOansRange->set(oansRangeLeft);
```

Schreibst du `L:A32NX_EFIS_L_ND_RANGE` selbst, gilt der Wert höchstens bis zum nächsten Frame.
Die FCU-Simulation selbst bekommt davon nichts mit.

## Der richtige Weg: die Custom-Events der FCU

`fbw.wasm` registriert für jede Seite diese Events
([`SimConnectInterface.cpp` Z. 705–710 und 731 ff.](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/interface/SimConnectInterface.cpp#L705-L710)):

| Event                          | Wirkung                                  |
| ------------------------------ | ---------------------------------------- |
| `A32NX.FCU_EFIS_L_RANGE_INC`   | Range-Knopf eine Raste im Uhrzeigersinn  |
| `A32NX.FCU_EFIS_L_RANGE_DEC`   | eine Raste zurück                        |
| `A32NX.FCU_EFIS_L_RANGE_SET`   | Position direkt setzen (Wert 0–11)       |
| `A32NX.FCU_EFIS_L_MODE_SET`    | ND-Modus setzen (0–4)                    |
| … `_R_` …                      | dasselbe für die rechte Seite (F/O)      |

Das Modell übernimmt den Wert und begrenzt ihn auf 0–11
([`A380FcuComputer.cpp` Z. 2214–2246](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/model/A380FcuComputer.cpp#L2214-L2246)).
FBW nutzt `RANGE_SET` selbst: Beim Start auf der Runway wird die Range auf 5 (10 NM) gesetzt,
beim Start in der Luft auf 6 (20 NM)
([`FlyByWireInterface.cpp` Z. 925–960](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/FlyByWireInterface.cpp#L925-L960)).

Custom-Events mit einem Punkt im Namen sind global. Jeder SimConnect-Client, jedes
WASM-Modul und jedes JS-Instrument kann sie auslösen:

```js
// JavaScript (Toolbar-Panel, Instrument): so macht es FBW selbst, z. B. FmcAircraftInterface.ts Z. 1628
SimVar.SetSimVarValue('K:A32NX.FCU_EFIS_L_RANGE_SET', 'number', 2); // ZOOM 1 NM
```

```cpp
// WASM (C++), Legacy-API aus gauges.h, die FBW in seinem 2024-Build ebenfalls nutzt
execute_calculator_code("2 (>K:A32NX.FCU_EFIS_L_RANGE_SET)", nullptr, nullptr, nullptr);
```

```cpp
// SimConnect (WASM-Modul oder externe .exe)
SimConnect_MapClientEventToSimEvent(hSimConnect, EVT_RANGE_SET_L, "A32NX.FCU_EFIS_L_RANGE_SET");
SimConnect_TransmitClientEvent(hSimConnect, SIMCONNECT_OBJECT_ID_USER, EVT_RANGE_SET_L, 2,
                               SIMCONNECT_GROUP_PRIORITY_HIGHEST, SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY);
```

## Fallstricke

1. **Moduswechsel verschiebt die Range.** Beim Wechsel **in** den ARC-Modus senkt die FCU die
   Position um eins, wenn sie über 5 liegt. Beim Wechsel **aus** ARC hebt sie sie um eins an,
   außer bei Position 4
   ([`A380FcuComputer.cpp` Z. 2218–2227](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/wasm/fbw_a380/src/model/A380FcuComputer.cpp#L2218-L2227)).
   Setz deshalb zuerst den Modus und erst **in einem späteren Frame** die Range.
2. **Events wirken nur mit geladenem FBW-A380X und laufender FCU.** In anderen Flugzeugen
   passiert beim Auslösen nichts, es schadet aber auch nicht. Prüf trotzdem, ob der A380X geladen
   ist, damit dein Addon nicht ins Leere arbeitet.
3. **Das ist keine stabile API.** FBW baut gerade eine „stabile Cockpit-API“ über Input Events
   (`B:`-Variablen) auf, siehe
   [`a380x-input-events.md`](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/docs/a380x-input-events.md).
   Bisher ist dort nur das RMP dokumentiert. Sobald der EFIS-CP dazukommt, auf `B:`-Events umstellen.
4. **Manuelle Eingaben respektieren.** Dreht der Pilot selbst am Knopf, ändert sich
   `L:A32NX_EFIS_x_ND_RANGE` bzw. `…_OANS_RANGE`, ohne dass dein Addon etwas gesendet hat. In dem
   Fall die Automatik eine Weile pausieren, sonst kämpft sie gegen den Piloten.
5. **Nicht jeden Frame senden.** Nur bei einem Wechsel der Zielstufe senden, mit Hysterese
   (z. B. unterschiedliche Schwellen beim Beschleunigen und Abbremsen), sonst springt die Karte hin und her.
6. **Nur A380X.** Die A32NX-FCU hat keine ZOOM-Stufen (`a320EfisRangeSettings = [10 … 320]`).
   Das OANC ist dort ein separates Instrument, das bei diesem Commit nicht in der `panel.cfg` des
   A32NX eingebunden ist. Ein Auto-Zoom über die FCU-Events funktioniert also nur im A380X.

## Vorschlag für die Auto-Zoom-Logik

Nur als Ausgangspunkt, die Schwellen musst du im Sim abstimmen:

| Situation (Seite L und/oder R)                 | Zielposition             |
| ---------------------------------------------- | ------------------------ |
| am Boden, GS < 12 kt (Rollen um Kurven, Gate)  | 1 (ZOOM 0,5 NM)          |
| am Boden, 12–30 kt (normales Rollen)           | 2 (ZOOM 1 NM)            |
| am Boden, > 30 kt (Start- oder Landerollstrecke) | 3 oder 4 (ZOOM 2/5 NM) |
| in der Luft, nach dem Start                    | zurück auf die Range, die vor dem Zoomen eingestellt war (z. B. 5 = 10 NM) |

Zu lesende SimVars: `SIM ON GROUND` (Bool), `GROUND VELOCITY` (Knots), optional
`PLANE ALT ABOVE GROUND` (Feet), dazu die oben genannten `L:A32NX_EFIS_*`-Variablen.
