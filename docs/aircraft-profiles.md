# Flugzeugprofile: was das Addon nach der Landung wo einstellt

Stand der Recherche: 24.09.2026. Pro Flugzeug steht hier, woran das Addon es erkennt, was es
nach der Landung sendet und worauf das beruht. **Belegt** heißt: aus Quellcode, offizieller
Doku oder mehreren unabhängigen, praxiserprobten Quellen. **Annahme** heißt: plausibel und
begründet, aber nicht im Sim nachgeprüft.

## Vorbild: was das echte Flugzeug und der iniBuilds A380 tun

- **Airbus A350, FCOM** (DSC-34-NAV-80-20-10, EFIS CP / ND MODE SELECTOR): *„At landing, the ND
  automatically displays the ANF in ARC mode, with a 2 NM range.“* An anderer Stelle
  (DSC-34-NAV-80-10): *„At landing, regardless of the position of the ND mode selector and the
  ND range selector, the airport moving map appears in ARC mode with a 1 NM or 2 NM range in order
  to entirely display the runway that is ahead of the aircraft.“*
  ([ETH A350 FCOM, archive.org](https://archive.org/download/eth-a350-fleet-fcom/ETH%20A350%20FLEET%20FCOM_djvu.txt))
- **iniBuilds A380** (MSFS 2024, Handbuch 1.2, EFB → Settings → Simulation): *„OANS Auto Zoom:
  ND automatically changes to the zoom range on touchdown.“*
  ([iniBuilds-Forum](https://forum.inibuilds.com/topic/40789-aircraft-manual-a380-airliner/))
- Ein automatischer Wechsel beim Start ist nirgends beschrieben. Das Addon macht daher beim
  Start nichts.

Daraus folgt das Verhalten des Addons:

1. **Scharf schalten** erst nach einer echten Flugphase: mindestens 15 s über 100 ft AGL.
2. **Landung** = 3 s ununterbrochen am Boden (`SIM ON GROUND`), mit mindestens 40 kt beim
   ersten Bodenkontakt. Das ähnelt Airbus' eigener Definition für Autobrake/BTV („nose gear
   down or 5 s after main gear down“). Ein Bounce setzt die Zählung zurück. Wird das Flugzeug
   dagegen auf den Boden gesetzt (Slew, Versetzen ans Gate), gilt das nicht als Landung.
3. **Einmal pro Landung** werden beide NDs auf **ARC** und **ZOOM 2 NM** gestellt. Ist auf
   einer Seite schon eine ZOOM-Stufe gewählt (vom Piloten oder vom Flugzeug selbst), bleibt
   diese Stufe. Danach fasst das Addon die Displays bis zum nächsten Flug nicht mehr an.
4. Befehle gehen **einer pro Frame** raus, damit keine Knopfraste verloren geht.

## Erkennung des Flugzeugs

Ein Profil greift, wenn der `TITLE` oder der Pfad der geladenen `aircraft.cfg` (aus dem
SimConnect-Event `AircraftLoaded`, ab dem Ordner `SimObjects`) das Stichwort enthält.
Groß-/Kleinschreibung spielt keine Rolle.

| Profil | Stichwort | Beispiele |
| ------ | --------- | --------- |
| FlyByWire A380X | `a380x` | Titel `FlyByWire A380X (A380-842)`, Ordner `SimObjects\AirPlanes\FlyByWire_A380X\…` |
| iniBuilds A350 | `a350` | Ordner `SimObjects\Airplanes\A350\presets\iniBuilds\A350-900\…` |
| Synaptic A220 | `a220` | Titel `A220-300`, Ordner `SimObjects\Airplanes\Synaptic_A220\…` |

## FlyByWire A380X — belegt

Details mit Quellstellen: [fbw-a380x-oans-zoom.md](fbw-a380x-oans-zoom.md).

| Was | Wie | Quelle |
| --- | --- | ------ |
| OANS verfügbar? | `L:A32NX_OANS_AVAILABLE` = 1 (braucht Navigraph im flyPad) | FBW-Quellcode |
| ND-Modus lesen | `L:A32NX_EFIS_{L,R}_ND_MODE`: 0 ROSE ILS, 1 ROSE VOR, 2 ROSE NAV, 3 ARC, 4 PLAN | FBW-Quellcode |
| ZOOM schon aktiv? | `L:A32NX_EFIS_{L,R}_ND_RANGE` = 0 | FBW-Quellcode |
| ARC einstellen | `3 (>K:A32NX.FCU_EFIS_{L,R}_MODE_SET)` | FBW-Quellcode |
| ZOOM 2 NM | `3 (>K:A32NX.FCU_EFIS_{L,R}_RANGE_SET)` (0–4 = ZOOM 0,2/0,5/1/2/5 NM) | FBW-Quellcode |

Die L-Vars sind nur zum Lesen: FBW überschreibt sie in jedem Frame aus seiner FCU-Simulation.
Ist das OANS nicht verfügbar, ändert das Addon nichts.

## iniBuilds A350 — belegt

| Was | Wie | Quelle |
| --- | --- | ------ |
| ND-Modus | `L:INI_MAP_MODE_{CAPT,FO}_SWITCH`: 0 LS, 1 VOR, 2 NAV, 3 ARC, 4 PLAN | iniBuilds-L-Var-Liste (Feb. 2025), HubHop, YourControls, CrewMate |
| Range-Knopf | `L:INI_MAP_RANGE_{CAPT,FO}_SWITCH`: 0–4 = ZOOM, 5–11 = 10–640 NM | HubHop (u. a. „Zoom Stage 1“ = 4, ZOOM-Lampe = Wert < 5), YourControls, CrewMate |
| ZOOM-Stufen | 4 = 5 NM, 3 = 2 NM, 2 = 1 NM, 1 = 0,5 NM, 0 = 0,2 NM | Handbuch 1.3, S. 57 (Stufen 5 → 0,2 NM gegen den Uhrzeigersinn); Zuordnung daraus abgeleitet |
| Direktes Schreiben | funktioniert; iniBuilds erlaubt ausdrücklich die Steuerung über diese L-Vars | iniBuilds-Forum, CrewMate liest den Wert nach dem Schreiben zurück |

- **Schreibweise:** iniBuilds' eigene L-Var-Liste nennt den Range-Knopf
  `INI_MAP_MODE_RANGE_{CAPT,FO}_SWITCH`, alle Tools aus der Praxis nutzen
  `INI_MAP_RANGE_{CAPT,FO}_SWITCH`. Das Addon prüft mit `check_named_variable`, welcher Name im
  geladenen Flugzeug existiert, und schreibt nur diesen.
- **Andere iniBuilds-Flugzeuge** (A320neo V2, A330, A340 …) haben gleichnamige L-Vars mit
  anderer Skala. Deshalb schreibt das Addon nur, wenn der A350 erkannt wurde.
- Die Seite des F/O folgt **2 s nach** dem Captain. Das gleichzeitige Laden der Karte auf beiden
  NDs hat den A350 früher zum Absturz gebracht (behoben in v1.0.5).
- **Eingebaute Funktion:** Der A350 kann das auch selbst (Option „autozoom“ im OIS, laut
  iniBuilds-Forum; L-Var `INI_ANF_AUTO_ZOOM`). Ist sie aktiv, steht der Knopf beim Auslösen
  schon auf ZOOM und das Addon lässt ihn so.
- Beim ersten Laden einer Flughafenkarte kann der Sim laut iniBuilds bis zu 10 s einfrieren.
  Das ist normal.

## Synaptic A220 — belegt, eine Annahme

| Was | Wie | Quelle |
| --- | --- | ------ |
| MAP-Range-Knopf | `(>H:A220_CTP_RANGE_{1,2}_{INC,DEC})`, ein Event pro Raste (1 = Captain, 2 = F/O) | HubHop (zwei unabhängige Einreichungen), FS-Copilot-Analyse der Cockpit-XMLs (`A220_KnobContinuous`) |
| Wann zeigt die MAP die Flughafenkarte? | am Boden unterhalb 2 NM (MAP und PLAN) | [A220 Pilot Guide](https://ugc.production.linktr.ee/2825963b-7686-43fb-913e-0530abe9b753_Airbus-A220-Pilot-Guide.pdf) (nach FCOM), S. 114: *„Zooming in below 2 nm automatically displays the AMM.“* |
| Kartenbereiche der Flughafenkarte | 1000 FT, 2000 FT, 3000 FT, 1 NM (kleinste Stufen) | zwei Community-Mods lesen genau diese Beschriftungen aus dem Flugzeug |

- Der A220 meldet den eingestellten Bereich nirgends zurück (keine L-Var, nur JS-intern). Das
  Addon dreht deshalb jeden Knopf 30 Rasten zurück auf den kleinsten Bereich und dann 3 Rasten
  vor auf **1 NM**, die größte Stufe der Flughafenkarte.
- **Annahme:** Der Knopf bleibt am Ende stehen und springt nicht vom kleinsten auf den größten
  Bereich. So verhalten sich der echte Pro-Line-Fusion-Knopf und das MSFS Avionics Framework,
  auf dem die A220-Displays aufbauen. Im Sim nachgeprüft ist es nicht.
- **Voraussetzung: Synaptic A220 v1.0.10 oder neuer.** Erst diese Version bringt die
  Flughafenkarte mit („Full airport moving map with Navigraph nav data“, „Runways-only airport
  moving map with native nav data“, [Changelog](https://docs.synapticsim.com/changelog),
  angekündigt für den 25.09.2026). Bis v1.0.9 zeigt die MAP in diesen Bereichen
  „AIRPORT MAP FAULT“.
- Der echte A220 zeigt die Karte nach der Landung nicht automatisch; das Addon ergänzt das
  bewusst, damit sich alle drei Flugzeuge gleich verhalten.

## Was sich nicht vorab prüfen ließ

Das Addon wurde gegen das offizielle MSFS 2024 SDK 1.7.3 gebaut und seine Logik mit
Host-Tests geprüft. In einem laufenden Simulator konnte es hier nicht getestet werden.
Offen bleibt deshalb nur, was die Flugzeuge intern tun: vor allem die Annahme zum
A220-Knopf und dass die A220-Karte mit v1.0.10 so kommt wie angekündigt.
