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

Außerdem braucht das OANS Flughafendaten vom Kartenserver `amdb.api.navigraph.com`: von
Navigraph selbst oder von [AMDB Bridge](https://github.com/Vihaan2012-cmyk/Free-Airport-Mapping-DB),
das diese Adresse auf den eigenen PC umleitet und kein Navigraph-Konto braucht.
`L:A32NX_OANS_AVAILABLE` wird `1`, wenn die Flughafensuche beim Laden des Flugzeugs klappt und
der ARPT-NAV-Reset nicht gezogen ist
([`OansControlPanel.tsx` Z. 102–106, 277–288, 305 und 335](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/src/systems/instruments/src/ND/OansControlPanel.tsx#L277-L305)).
FBW sucht nur beim Laden und wenn sich das Navigraph-Token ändert. Lief der Kartenserver in
diesem Moment nicht, bleibt der Wert für den ganzen Flug `0`.

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
   Für das Addon spielt das keine Rolle: Es wechselt nur **in** ARC und setzt eine ZOOM-Position
   (0–4), die dabei nie verschoben wird. Ein im selben FCU-Durchlauf verarbeitetes `RANGE_SET`
   wird vor der Verschiebung übernommen und ist mit 3 ebenfalls nicht betroffen.
2. **Events wirken nur mit geladenem FBW-A380X und laufender FCU.** In anderen Flugzeugen
   passiert beim Auslösen nichts, es schadet aber auch nicht.
3. **Das ist keine stabile API.** FBW baut gerade eine „stabile Cockpit-API“ über Input Events
   (`B:`-Variablen) auf, siehe
   [`a380x-input-events.md`](https://github.com/flybywiresim/aircraft/blob/2baa2b35eadaf4c78e172ce41bbe6b40b4aeafb2/fbw-a380x/docs/a380x-input-events.md).
   Bisher ist dort nur das RMP dokumentiert. Sobald der EFIS-CP dazukommt, auf `B:`-Events umstellen.
4. **Nur A380X.** Die A32NX-FCU hat keine ZOOM-Stufen (`a320EfisRangeSettings = [10 … 320]`).
   Das OANC ist dort ein separates Instrument, das bei diesem Commit nicht in der `panel.cfg` des
   A32NX eingebunden ist.

## Was das Addon im A380X tut

Nach der Landung, einmal pro Seite (F/O eine Sekunde nach dem Captain):

1. ND-Modus nicht ARC → `3 (>K:A32NX.FCU_EFIS_x_MODE_SET)`
2. keine ZOOM-Stufe gewählt (`L:A32NX_EFIS_x_ND_RANGE` ≠ 0) → `3 (>K:A32NX.FCU_EFIS_x_RANGE_SET)` (ZOOM 2 NM)

`L:A32NX_OANS_AVAILABLE` wird nur ins Log geschrieben, nicht abgefragt: Version 1.0.0 hat bei `0`
nichts getan, und so blieb im Test mit AMDB Bridge statt Navigraph das ND unverändert. Zwei
Sekunden nach dem letzten Befehl liest das Addon `…_ND_MODE`, `…_ND_RANGE` und `…_OANS_RANGE`
beider Seiten zurück und schreibt sie ins Log.

Das entspricht dem echten A350 („At landing, the ND automatically displays the ANF in ARC mode,
with a 2 NM range“). Zeitpunkt und Bedingungen: [aircraft-profiles.md](aircraft-profiles.md).
