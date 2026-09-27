# AMDB-Karte im A220: Fehlermeldung für AMDB Bridge

Zwei Dinge an der A220-Flughafenkarte von AMDB Bridge (Paket `zzz-amdb-a220-amm`) kann dieses
Addon nicht ändern. Das geht nur im Code von AMDB Bridge. Der folgende Text ist eine fertige
Fehlermeldung für deren Issue-Tracker:
<https://github.com/Vihaan2012-cmyk/Free-Airport-Mapping-DB/issues>. Am besten die beiden
Screenshots (Captain-Display mit Karte, F/O-Display mit „AIRPORT MAP FAULT“) anhängen.

---

**Title:** A220 airport map: only one ND gets the map, and the A220's own aircraft symbol does
not mark the aircraft

**Setup:** MSFS 2024, Synaptic A220, AMDB Bridge with the A220 moving map
(`zzz-amdb-a220-amm` 1.0.0). Both NDs set to an airport map range after landing.

**1. Only one ND shows the map.** The captain's ND shows the map; the first officer's ND keeps
showing `AIRPORT MAP FAULT`. In `amdb-a220-amm.js`, `findMount()` returns the first
`ND_n_MOUNT` (1 to 4) that shows the fault, and there is a single canvas (`CANVAS_ID`) with a
single set of state (`canvas`, `ctx`, `faultEl`, `clutter`, `rangeText`, `viewX`/`viewW`,
`viewCenter`, `drag`, `lastScale`). `L:AMDB_AMM_DISPLAY` only moves the map to another ND.

*Suggestion:* mount one canvas per ND that shows the fault, keep that state per ND, and share
the fetched airport between them.

**2. The aircraft symbol is not where the aircraft is.** The map draws the ownship chevron at a
fixed point, `(vx + vw / 2, h * 0.78)`, and centres the airport there. The A220's own aircraft
symbol stays on top of the map (it is not in `clutter`), at the A220's own position for the
current format. The two do not coincide: on the half-screen MFW the white aircraft sits about
50 canvas units above the chevron. After the map is dragged, the chevron moves to the aircraft's
real place, while the A220's symbol stays put and no longer marks anything. Pilots read the white
aircraft as their position, which is wrong on this map.

*Suggestion:* either take the ownship point from the A220's own display (the centre of its
compass/range rings, or its aircraft symbol) and project the map there, or hide the A220's
aircraft symbol while the map is drawing (like the compass ring) and draw a full-size aircraft
symbol at the real position.

**3. (Minor)** `L:AMDB_AMM_PAN_RESET` is missing from the list of controls at the top of the
file. OANS Auto Zoom uses it to bring the map back onto the aircraft after a landing.
