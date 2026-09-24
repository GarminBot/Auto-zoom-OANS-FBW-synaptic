# Leitfaden: ein funktionierendes Community-Addon für MSFS 2024 bauen

Stand: 24.09.2026. Die Retail-Version ist Sim Update 6 (1.8.14.0), das passende SDK ist 1.7.3.
Offizielle Doku: <https://docs.flightsimulator.com/msfs2024/retail/>. Der ältere Baum unter
`/msfs2024/html/` ist als „Legacy: Locked SU5“ markiert.

## 1. Grundprinzip

Ein Addon ist ein **Paket**: ein Ordner mit zwei Dateien auf oberster Ebene.

- `manifest.json`: Metadaten (Titel, Typ, Version, Mindest-Sim-Version).
- `layout.json`: Liste **aller** übrigen Dateien des Pakets, mit exakter Größe.

Beim Start blendet der Sim den Inhalt jedes Pakets in sein virtuelles Dateisystem (VFS) ein.
`modules\…` landet dann unter `modules\…`, `html_ui\…` unter `html_ui\…` usw. Neue oder
geänderte Community-Pakete werden **nur beim Start** eingelesen. Nach jeder Änderung also den
Sim neu starten.

## 2. Wo der Community-Ordner liegt

| Version | Standardpfad |
| ------- | ------------ |
| Microsoft Store / Xbox-App | `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community2024` |
| Steam | `%APPDATA%\Microsoft Flight Simulator 2024\Packages\Community2024` |

- Seit Sim Update 4 gibt es **`Community2024`** für reine 2024-Pakete. `Community` wird weiter
  geladen, ist aber für MSFS-2020-Pakete gedacht. Bei gleichem Paketnamen gewinnt
  `Community2024`.
- Wurde der Pfad bei der Installation geändert, steht er in `UserCfg.opt` in der Zeile
  `InstalledPackagesPath "…"`. Die Datei liegt im Store unter `…\LocalCache\UserCfg.opt`, bei
  Steam unter `%APPDATA%\Microsoft Flight Simulator 2024\UserCfg.opt`.
- Im Sim: *Marketplace → My Library → Zahnrad → Open/Browse*.

## 3. manifest.json und layout.json

Echtes Beispiel, wie es das 2024-Build-System erzeugt:

```json
{
  "dependencies": [],
  "content_type": "MISC",
  "title": "EFB StreamDeckVR",
  "manufacturer": "",
  "creator": "Marc Fleury",
  "package_version": "0.1.10",
  "minimum_game_version": "1.8.16",
  "minimum_compatibility_version": "8.11.0.236",
  "export_type": "Community",
  "builder": "Microsoft Flight Simulator 2024",
  "package_order_hint": "MISC",
  "release_notes": { "neutral": { "LastUpdate": "", "OlderHistory": "" } },
  "total_package_size": "560533"
}
```

- `content_type` ist einer von `AIRCRAFT`, `INSTRUMENTS`, `LIVERY`, `SCENERY`, `MISSION`, `MISC`.
- `minimum_game_version` darf **nicht höher** sein als die Version des Spielers. Pakete aus einer
  Beta-SU gehören nicht in eine ältere Retail-Version. Dieses Addon übernimmt die Werte, mit
  denen FlyByWire seinen A380X für MSFS 2024 ausliefert (`1.7.27` / `7.26.0.214`).
- `layout.json` hat die Form `{"content": [{"path": "modules/x.wasm", "size": 12345, "date": 134…}]}`.
  `date` ist ein Windows-FILETIME als Zahl (100-ns-Schritte seit 1601).
- **Die Größen müssen exakt stimmen.** Eine falsche Größe kann das Laden blockieren. Deshalb
  hat dieses Repo eine `.gitattributes`: Git darf an den Paketdateien keine Zeilenenden ändern.
- `manifest.json` und `layout.json` selbst stehen nicht in `layout.json`.
- Paketname = Ordnername, üblich ist `firma-typ-name` in Kleinbuchstaben. Zwei Pakete mit
  gleichem Namen: nur eins wird aktiv.

Die `layout.json` erzeugt entweder das SDK beim Bauen, ein Skript (hier
[`tools/make_layout.py`](../tools/make_layout.py), FlyByWire macht es genauso) oder der
[MSFSLayoutGenerator](https://github.com/HughesMDflyer4/MSFSLayoutGenerator).

## 4. Das SDK

- **Bezugsquelle:** im Sim unter *Developer Mode → Help → SDK Installer*, oder direkt
  `https://sdk.flightsimulator.com/msfs2024/files/installers/1.7.3/MSFS2024_SDK_Core_Installer_1.7.3.zip`.
  Die Samples sind ein eigener Download (ca. 3,4 GB).
- **Inhalt:** `Tools\bin\fspackagetool.exe` (Paket-Builder), `WASM\` (clang-cl, wasm-ld,
  wasi-sysroot, `WasmVersions\MSFS_WasmVersions.a`), `SimConnect SDK\`, Visual-Studio-Integration
  (Plattform „MSFS“, Toolset „MSFS2024“, Vorlage „MSFS 2024 WASM Standalone Module“) und die
  Plugins für Blender und 3ds Max.
- **Projektdatei** (vom SDK-Sample `EFBTemplateAppProject.xml`):

```xml
<Project Version="2" Name="MyProject" FolderName="Packages" MetadataFolderName="PackagesMetadata">
  <OutputDirectory>.</OutputDirectory>
  <TemporaryOutputDirectory>_PackageInt</TemporaryOutputDirectory>
  <Packages>
    <Package>PackageDefinitions\mycompany-mypackage.xml</Package>
  </Packages>
</Project>
```

- **Paketdefinition:** Für ein WASM-Modul reicht eine `Copy`-Gruppe, wie bei MobiFlight:

```xml
<AssetPackage Version="1.0.0">
  <ItemSettings><ContentType>MISC</ContentType><Title>My Module</Title><Manufacturer/><Creator>Me</Creator></ItemSettings>
  <Flags><VisibleInStore>false</VisibleInStore><CanBeReferenced>true</CanBeReferenced></Flags>
  <AssetGroups>
    <AssetGroup Name="Module">
      <Type>Copy</Type>
      <Flags><FSXCompatibility>false</FSXCompatibility></Flags>
      <AssetDir>PackageSources\modules\</AssetDir>
      <OutputDir>modules\</OutputDir>
    </AssetGroup>
  </AssetGroups>
</AssetPackage>
```

- **Bauen:** im Sim im Developer Mode mit dem *Project Editor* („Build All“), oder per
  Kommandozeile `fspackagetool.exe MyProject.xml -nopause`. Das Tool startet dafür den Sim im
  Hintergrund, der eigentliche Builder steckt im Spiel. Ergebnis: `Packages\<name>\` mit
  `manifest.json` und `layout.json`.
- Für reine Kopier-Pakete (JS, fertige `.wasm`) braucht es den Builder nicht; ein Skript, das
  `layout.json` schreibt, genügt.

## 5. Welche Art Addon?

| Typ | Läuft wann | Aufwand | Geeignet für |
| --- | ---------- | ------- | ------------ |
| **Standalone-WASM-Modul** (`modules\*.wasm`) | die ganze Sitzung, egal welches Flugzeug | C++ + SDK | Hintergrund-Automatik, **so ist dieses Addon gebaut** |
| Toolbar-Panel (InGamePanel, `.spb`) | nur solange das Panel offen ist | HTML/JS + SDK | Fenster mit Bedienoberfläche |
| EFB-App (`html_ui\efb_ui\efb_apps\…`) | wenn das EFB läuft; laut Doku für alle Flugzeuge verfügbar | TypeScript, Node | Apps auf dem Tablet |
| Instrument per `panel.cfg`-Override | solange das Flugzeug geladen ist | HTML/JS | eigene Flugzeuge; bei fremden Flugzeugen bricht es bei jedem Update |
| Externes Programm (SimConnect, `exe.xml`) | solange das Programm läuft | beliebige Sprache | Tools außerhalb des Sims, kein Community-Paket |

## 6. Standalone-WASM-Module in der Praxis

- **Einstieg:** `extern "C" MSFS_CALLBACK void module_init(void)` und `module_deinit(void)`.
  Das Modul lädt beim Start des Sims und läuft auf einem eigenen Thread.
- **Takt:** SimConnect geht auch im WASM: `SimConnect_Open`, Abos wie das System-Event `Frame`,
  dann **einmal** `SimConnect_CallDispatch(h, proc, nullptr)`. Danach ruft der Sim `proc` für
  jede Nachricht auf. So arbeiten MobiFlight und dieses Addon. Für eigene Frame-Callbacks nennt
  die Doku `module_pre_sim_physics_update` usw. Viele 2024-Module nutzen stattdessen
  `Update_StandAlone(float)`; welcher Name heute aufgerufen wird, ist nicht sicher belegt.
- **Als 2024-Modul markieren:** `MSFS_WasmVersions.a` mitlinken, das macht das Visual-Studio-
  Toolset automatisch. Fehlt es, behandelt der Sim das Modul wie ein 2020-Modul.
- **APIs:** Neu sind `MSFS_Vars.h` (`fsVars…`) und `MSFS_Events.h` (`fsEvents…`). Die
  Legacy-API aus `gauges.h` (`execute_calculator_code`, `check_named_variable`, …) ist als
  „deprecated“ markiert, funktioniert aber weiter. Keine Exceptions, keine Threads, kein Win32.
- **Ohne Visual Studio bauen:** clang/wasm-ld mit dem `wasi-sysroot` und den Headern aus dem
  SDK, siehe [`tools/build.sh`](../tools/build.sh). Die Optionen stammen aus
  `WASM\vs\2022\Microsoft.Cpp.MSFS.Common.targets` des SDK. `-mcpu=mvp -mbulk-memory` hält
  moderne Compiler bei den WASM-Features, die auch die SDK-Bibliotheken benutzen.
- **Kompilieren beim Nutzer:** Der Sim übersetzt `.wasm` aus Community-Paketen beim ersten Laden
  in nativen Code und cacht das Ergebnis. Ändert sich die Datei, übersetzt er neu.

## 7. Fremde Flugzeuge fernsteuern

- **Variablenarten:**
  - `L:` Lokalvariablen des Flugzeugs.
  - `K:` Key-Events. Custom-Events mit Punkt im Namen, z. B. `A32NX.FCU_EFIS_L_RANGE_SET`,
    gehen an jeden SimConnect-Client, der sie registriert hat.
  - `B:` Input Events (2024).
  - `H:` Events an HTML/JS-Instrumente.
- Am flexibelsten ist RPN über `execute_calculator_code`, z. B. `3 (>L:INI_MAP_MODE_CAPT_SWITCH)`
  oder `(>H:A220_CTP_RANGE_1_DEC)`. Genau so führt MobiFlight die Presets aus der
  Community-Datenbank [HubHop](https://hubhop.mobiflight.com) aus.
- **Stolperfallen:**
  - Manche L-Vars sind Ausgänge, die das Flugzeug in jedem Frame überschreibt. Beim FBW A380X
    betrifft das den ND-Range: dort helfen nur die Events.
  - Gleiche L-Var-Namen können in verschiedenen Flugzeugen anderes bedeuten, z. B.
    `INI_MAP_RANGE_CAPT_SWITCH` beim A350 und beim A320neo V2. Deshalb erst das Flugzeug sicher
    erkennen: über `TITLE` oder den Pfad aus dem SimConnect-Event `AircraftLoaded`.
  - Manche Zustände liegen nur im JavaScript des Flugzeugs und sind von außen nicht lesbar,
    etwa die Kartenreichweite beim A220.

## 8. Debuggen

- **Entwicklermodus:** *Einstellungen → Allgemein → Erweiterte Optionen*.
- **Konsole:** *Debug → Console* zeigt `printf`-Ausgaben von WASM-Modulen.
- **WASM Debug:** Das Fenster zeigt den Zustand jedes Moduls: „COMPILING“, läuft, oder „DIRTY“
  nach einem Absturz.
- **JS-Instrumente:** mit dem Coherent GT Debugger aus dem SDK
  (`Tools\CoherentGT Debugger\Debugger.exe`).
- **Ohne Sim:** Logik mit Host-Tests prüfen, siehe [`tests/host/`](../tests/host/).

## 9. Checkliste vor dem Veröffentlichen

- [ ] `manifest.json` direkt im Paketordner, `minimum_game_version` nicht höher als die Retail-Version
- [ ] `layout.json` passt byteweise zu den Dateien (nach jedem Build neu erzeugen)
- [ ] keine Zeilenend-Konvertierung durch Git (`.gitattributes`)
- [ ] Paketpfade kurz halten: Windows hat eine Grenze von 260 Zeichen, und der Store-Pfad bis
      `Community2024\` ist schon rund 110 Zeichen lang
- [ ] WASM mit dem 2024-SDK gebaut und `MSFS_WasmVersions.a` gelinkt
- [ ] nur mit Flugzeugen interagieren, die sicher erkannt wurden

## Quellen

- MSFS 2024 SDK-Doku: [WebAssembly](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/wasm/webassembly/),
  [Vars API](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/wasm/vars-api/vars-api/),
  [Virtual File System](https://docs.flightsimulator.com/msfs2024/retail/devmode/menus/tools_info/virtual-file-system/virtual-file-system-information/),
  [Package Tool](https://docs.flightsimulator.com/msfs2024/retail/sdk-tools/package-tool/package-tool/),
  [SimConnect](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/simconnect-sdk/)
- Microsoft Support zu den Community-Pfaden: <https://flightsimulator.zendesk.com/hc/en-us/articles/17046732768796>
- Praxisbeispiele: [MobiFlight WASM Module](https://github.com/MobiFlight/MobiFlight-WASM-Module),
  [cj4-plus](https://github.com/liz3/cj4-plus) (2024-Standalone-Modul),
  [FlyByWire](https://github.com/flybywiresim/aircraft) (Build ohne Visual Studio, `layout.json`-Skript)
