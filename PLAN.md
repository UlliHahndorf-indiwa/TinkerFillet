# TinkerFillet — Implementierungsplan (Blazor)

## Context

Der Nutzer modelliert privat in Tinkercad im Browser und druckt die Ergebnisse
per Slicer. Tinkercad deckt seinen Bedarf vollständig ab — mit genau einer
Ausnahme: es kann keine Fillets (weiche Abrundungen an harten Kanten).

Dafür ist bisher Autodesk Fusion 360 im Einsatz. Das funktioniert für direkt
aus Tinkercad übergebene Dateien gut, aber nicht für beliebig importierte STLs,
und bringt für eine einzige benötigte Funktion erhebliche Nachteile mit:
riesiger Installationsumfang, erzwungene Updates, langer Programmstart,
regelmäßig verlorene Personal-Use-Lizenz, und bei komplexen Objekten scheitert
die Übergabe aus Tinkercad.

Gebaut wird stattdessen ein kleines, lokal im Browser laufendes Werkzeug: STL
laden, Kante anklicken, mm-Radius eingeben, abgerundetes STL exportieren.

**Kernschwierigkeit.** STL ist Dreieckssuppe ohne Topologie. Eine "harte Kante"
existiert dort nur implizit als Kette von Dreieckskanten mit großem
Dihedralwinkel. Fusion kann Fillets, weil es intern B-Rep hat. Das Werkzeug
muss diese Topologie also erst rekonstruieren. Das ist der eigentliche
Schwierigkeitsgrad — nicht die Oberfläche.

**Vorhandene Alternative, geprüft:** `ButterySpace Round STL` rundet nur Ränder
(Außensilhouette und Durchgangslöcher), lädt die Datei auf einen Server hoch
und ist nur eingeschränkt kostenlos. Deckt "beliebige Kante markieren, lokal,
offline" nicht ab.

> Diese Fassung ersetzt den ursprünglichen TypeScript-Entwurf. Algorithmen,
> Toleranzen und Stufenplan sind unverändert; Sprache, Projektlayout,
> Interop-Schnitt und Teststrategie sind neu.

---

## Abgestimmte Entscheidungen

| Thema | Entscheidung | Begründung |
|---|---|---|
| Modelltyp | Nur CAD-artige STL (Tinkercad-Stil) | Erlaubt Rekonstruktion echter Flächen, damit exakte Fillets |
| Plattform | Browser, lokal, offline | Kein Install, kein Update, keine Lizenz, Datei verlässt den Rechner nicht |
| Verfahren | B-Rep-Rekonstruktion + echter OCC-Fillet, gestuft | Exaktes Ergebnis statt approximierter Mesh-Rundung |
| Auswahl | Klick wählt ganze Kantenkette | Tessellierter Lochrand besteht aus vielen Einzelsegmenten |
| Workflow | Mehrere Fillets, editierbare Feature-Liste, Undo/Redo | Gewohnter CAD-Ablauf |
| **Stack** | **Blazor WebAssembly standalone (.NET 10), C# für den Algorithmenkern** | Vertraute Sprache genau dort, wo die Arbeit schwierig ist |
| CAD-Kern | **occt-wasm 5.0.0** über JS-Interop, im Web Worker | Es gibt keinen .NET-B-Rep-Kernel mit Fillet — geprüft. Zu `opencascade.js` siehe 1.1-Ergebnisse |
| 3D-Anzeige | Three.js über JS-Interop | Alternative wäre rohes WebGL, deutlich mehr Arbeit |
| Replay-Fehler | Schritt als `failed` markieren, Rest rechnen | Verhalten echter CAD-Systeme, kein Arbeitsverlust |
| Zusatzfunktion | Größten möglichen Radius vorschlagen | Erspart Raten bei zu großem Radius |
| Auslieferung | Blazor-PWA, statisch auf GitHub Pages | Lesezeichen neben Tinkercad, offline, keine Uploads |
| Repo-Ort | `E:\Web\TinkerFillet`, **außerhalb iCloud Drive** | Sync über Git statt iCloud; keine Konflikte bei Build-Artefakten |
| Sichtbarkeit | **Öffentliches** GitHub-Repo | GitHub Free liefert keine Pages aus privaten Repos; das Werkzeug enthält nichts Geheimes |

**Nicht-Ziele v1:** organische/gescannte Meshes, Fase/Chamfer, variabler Radius
entlang einer Kante, Boolean-Operationen, STEP-Export, einstellbare
Export-Auflösung.

### Was Blazor ändert — und was nicht

Blazor heißt hier **nicht** "C# statt JavaScript", sondern "C# für die
Algorithmen, JavaScript für Kernel und 3D".

- **Wird C#:** STL lesen/schreiben, Verschweißen, Regionen, Loops,
  Primitiv-Fitting, Kettenpropagierung, Selektoren, Historie, Oberfläche.
  Das ist der schwierige Teil und grob 70 % des Aufwands.
- **Bleibt JavaScript:** der OpenCASCADE-Aufruf und der Three.js-Viewport,
  realistisch 400–600 Zeilen Klebecode.
- **Entfällt:** npm und `node_modules`. Kernel und Three.js werden als fertige
  Dateien nach `wwwroot/lib/` gelegt. In 1.1 bestätigt: die Node-Tests laufen
  mit `node --test` gegen genau diese Dateien, ohne ein einziges npm-Paket.
- **Wird besser als im TS-Entwurf:** Die Kerntests laufen als xUnit auf
  Desktop-.NET — schneller, kein Browser, normaler Debugger.

---

## Umgebung (geprüft)

- .NET SDK 10.0.400, dazu 9.0.308 und 8.0.424
- Workload `wasm-tools` bereits installiert
- node 24.0.0 (nur für die OCC-Tests nötig, siehe Verifikation — **ohne** npm-Pakete)
- git 2.41.0

Nichts nachzuinstallieren.

Zwei Anpassungen am Rechner-Setup, die der Durchstich erzwungen hat:

- `NuGet.config` im Repo mit `<clear />`. Auf dem Windows-Rechner sind
  NuGet-Quellen registriert, die auf deinstallierte DevExpress-Versionen zeigen
  und jedes `restore` mit NU1301 scheitern lassen. Nebeneffekt: eine der
  geerbten Quellen enthält ein Zugangstoken in der URL — das kann so nicht
  versehentlich ins öffentliche Repo geraten.
- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` im App-Projekt. `[JSImport]`
  erzeugt unsicheren Marshalling-Code und verweigert sonst den Dienst
  (SYSLIB1074).

---

## Ergebnisse des Durchstichs (Stufe 1.1)

Durchgeführt und gemessen. Diese Befunde korrigieren mehrere Annahmen weiter
unten; wo sie widersprechen, gilt dieser Abschnitt.

### Kernelwechsel: opencascade.js → occt-wasm

`opencascade.js` ist faktisch aufgegeben — stabile Version von September 2020,
letzte Beta März 2023. `occt-wasm` wird wöchentlich veröffentlicht.

| | opencascade.js 1.1.1 | occt-wasm 5.0.0 |
|---|---|---|
| Letzte Version | Sep 2020 (Beta Mrz 2023) | 10.09.2026 |
| WASM entpackt | 65,9 MB | **22,2 MB** |
| Lizenz | LGPL-2.1-only | **MIT OR Apache-2.0** |
| API | rohe Emscripten-Bindings | kuratierte TypeScript-API |

Entscheidend war nicht die Größe, sondern die API-Tiefe. `occt-wasm` deckt die
geplante Pipeline fast wörtlich ab:

| Planschritt | Funktion |
|---|---|
| Loops → Fläche mit Löchern | `makeLineEdge` → `makeWire` → `makeFace` → `addHolesInFace` |
| Vernähen und Solidify (1.7) | `buildSolidFromFaces(faces, tolerance)`, `sewAndSolidify` |
| Kantengraph (1.8) | `getSubShapes`, `edgeToFaceMap`, `adjacentFaces`, `sharedEdges`, `outerWire` |
| Fillet (1.10) | `fillet(solid, edges, radius)` |
| Tessellierung (1.10) | `tessellate(shape, { linearDeflection })`, `wireframe` |
| Analytischer Test | `getVolume`, `curveLength` |
| Stufe 2 | `makeCircleEdge`, `getFaceCylinderData` |
| Fehlerbehandlung (1.13) | `healFace`, `healWire`, `healSolid`, `fixFaceOrientations` |

Folge: der JS-Klebecode wird deutlich kleiner als die geschätzten 400–600
Zeilen. **Version exakt pinnen** — zwischen 4.3.1 und 5.0.0 lagen drei Wochen.

### Risiko „OCC-Fehler" — aufgelöst, günstig

Ein unmöglicher Fillet wirft eine fangbare `OcctError` mit
`code: CONSTRUCTION_FAILED`, **kein hartes `abort()`**. Der Kernel ist danach
unverändert weiter benutzbar; der nächste Fillet lief normal durch.

Damit wird der Worker-Neustart aus dem Architekturabschnitt vom tragenden
Designtreiber zum bloßen Sicherheitsnetz. Der Zustandsschnitt bleibt trotzdem
wie beschrieben — er kostet nichts und deckt den Rest ab.

### Risiko „Interop-Kosten" — aufgelöst, unkritisch

Gemessen mit 900 000 doubles (7,2 MB, etwa die Vertexdaten eines
100k-Dreiecke-Meshes), durch die ganze Kette bis in den Worker:

| Pfad | gesamt | davon Kopie/Konvertierung | Worker-Hinweg |
|---|---|---|---|
| `MemoryView` (zweistufig) | 8 ms | 1,6 ms | 5,1 ms |
| `double[]` (naiv) | 12 ms | 1,8 ms | 2,0 ms |

Beide unkritisch. Die Grenze wird einmal pro Operation überquert, nicht pro
Bild. **Empfehlung: den einfachen `double[]`-Pfad nehmen.** Der
Geschwindigkeitsvorteil von `MemoryView` rechtfertigt seine Komplexität nicht —
und `MemoryView` erzwingt sie: eine `Span<T>`-Parameter lässt sich **nicht** mit
einem `Task<T>`-Rückgabewert kombinieren (SYSLIB1072), weil die View nur
während des synchronen Teils gültig ist. Man braucht dann zwingend zwei
Aufrufe: einen synchronen zum Übergeben, einen asynchronen zum Verarbeiten.

### Gemessene Laufzeiten

| Schritt | Zeit |
|---|---|
| JS-Modul laden | 15 ms |
| Kernel im Worker starten (22 MB WASM) | 402 ms |
| Fillet auf Würfelkante, erster Aufruf | 107 ms |
| derselbe Fillet, warm | 7 ms |

### Fillet-Korrektheit durch die ganze Kette

Relativer Fehler gegen `ΔV = (1 − π/4)·r²·L`:

| r | 0,5 | 1 | 2 | 3 | 4 |
|---|---|---|---|---|---|
| rel. Fehler | 2,4e-13 | 3,9e-14 | 3,6e-15 | 7,5e-15 | 3,6e-15 |

Das entfernte Volumen folgt `r²` über fünf Radien auf Maschinengenauigkeit —
exakte B-Rep-Geometrie, keine Näherung. Derselbe Wert kam auch durch die volle
Kette .NET → JS → Worker → WASM zurück.

### Offen geblieben

`filletWithHistory` liefert `EvolutionData` mit `modified`/`generated`/
`deleted`-Flächenhashes — potenziell ein Ersatz für den heuristischen
Fingerabdruck aus Abschnitt 6.2. Der Probelauf ergab aber unplausible Zahlen
(6 Eingabeflächen, 12 „modified", 0 „generated", obwohl der Fillet eine neue
Fläche erzeugt). Semantik ungeklärt. **Abschnitt 6.2 bleibt vorerst wie
geplant**, mit einer Neubewertung, sobald echte Geometrie durchläuft.

---

## Architektur

### Grundsatz

Der gesamte Algorithmenkern liegt in einer **plattformfreien C#-Bibliothek
ohne Browser- und ohne Blazor-Abhängigkeit**. Sie kennt weder OpenCASCADE noch
JavaScript. Damit ist der fehleranfälligste Teil auf dem Desktop debug- und
testbar.

### Pipeline

```
STL-Bytes
  → weld        Vertices verschweißen, Half-Edge-Topologie        [C#]
  → Regionen    koplanare Dreiecke clustern                       [C#]
  → Loops       Randschleifen je Region, außen/Löcher             [C#]
  → [Fitting]   Stufe 2: Zylinder/Kegel erkennen                  [C#]
  → BrepRecipe  Flächenbeschreibung (Trägerfläche + Loops)        [C#]
      ⇅ Interop
  → OCC-Flächen bauen, sew, Solid, Kantengraph                    [JS/Worker]
      ⇅ Interop
Klick → Kettenpropagierung [C#] → Fillet(r) [JS] → Anzeige/Export
```

### Projektstruktur

```
TinkerFillet.sln
src/
  TinkerFillet.Core/           Klassenbibliothek, net10.0, keine Browser-Bezüge
    Stl/StlReader.cs           Binär- und ASCII-STL lesen
    Stl/StlWriter.cs           Binär-STL schreiben
    Mesh/Welder.cs             Vertex-Verschweißung, Half-Edge, Manifold-Prüfung
    Mesh/RegionGrower.cs       Ebene Regionen
    Mesh/LoopExtractor.cs      Randschleifen, außen vs. Löcher
    Mesh/PrimitiveFitter.cs    Zylinder-/Kegelerkennung (Stufe 2)
    Brep/BrepRecipe.cs         Interop-Datenmodell C# → Worker
    Brep/EdgeGraph.cs          Interop-Datenmodell Worker → C#
    Brep/ChainPropagator.cs    Kantenkette ab Klick
    History/FeatureStore.cs    Feature-Liste, Selektoren, Replay, Caching
    History/EdgeSelector.cs    Geometrischer Fingerabdruck + Auflösung
  TinkerFillet.App/            Blazor WebAssembly standalone, PWA
    Program.cs
    Pages/Editor.razor
    Components/FeatureList, RadiusDialog, ProgressPanel (je .razor/.razor.cs/.razor.css)
    Interop/OccBridge.cs       [JSImport] auf occ-bridge.js
    Interop/ViewportBridge.cs  [JSImport] auf viewport.js
    wwwroot/
      lib/occt/                vendored: occt-wasm 5.0.0 dist (22,2 MB .wasm)
      lib/three/               vendored: three.module.js
      js/occ-bridge.js         Main-Thread-Seite, startet und überwacht Worker
      js/occ-worker.js         Worker: OCC laden, Recipe bauen, Fillet, Tessellieren
      js/viewport.js           Three.js-Szene, Kamera, ID-Puffer-Picking
      manifest.webmanifest, service-worker.js, .nojekyll
tests/
  TinkerFillet.Core.Tests/     xUnit, läuft auf Desktop-.NET
  occ/                         node --test, testet occ-worker.js direkt
```

### Konventionen

`code-styleguide.md` ist verbindlich. Die wichtigsten Punkte, weil sie das
Layout der Dateien bestimmen:

- **Kein `@code`-Block.** Jede Komponente ist `Name.razor` (nur Markup) plus
  `Name.razor.cs` (partielle Klasse) plus `Name.razor.css`.
- **`var`, wo der Typ schon auf der Zeile steht**, sonst ausgeschrieben:
  `var faces = new List<RecipeFace>()` gegen
  `IndexedMesh mesh = Welder.Weld(…)` — dort sagt nichts, was zurückkommt.
- **Eine Klasse pro Datei**, Dateiname gleich Klassenname.
- **CSS beim Bauteil**, nicht in `app.css`. Dort steht nur, was wirklich
  mehrere Komponenten teilen: die Farbvariablen, `.hint`, und die Blazor-
  eigene Ladeanzeige, die in `index.html` steht und zu keiner Komponente
  gehört.
- Kein Inline-CSS. Der Fortschrittsbalken ist deshalb ein `<progress>`: sein
  Wert ist Inhalt, kein Stil.

Durchgesetzt wird das vom Build (`EnforceCodeStyleInBuild`) gegen die Regeln in
`.editorconfig`, nicht von der Durchsicht.

### Interop-Schnitt

Bewusst **datenzentriert**, nicht objektzentriert: über die Grenze gehen nur
serialisierbare Beschreibungen, keine Handles auf OCC-Objekte. Damit ist jede
Seite für sich testbar und der Worker jederzeit wegwerfbar.

| Richtung | Nutzlast | Inhalt |
|---|---|---|
| C# → Worker | `BrepRecipe` | je Fläche: Trägerflächentyp (`plane`/`cylinder`/`cone`) + Parameter, Loops als Punktfolgen |
| Worker → C# | `EdgeGraph` | je Kante: Id, Mittelpunkt, Tangente, beide Flächennormalen, Länge, Dihedralwinkel, konvex/konkav, Endknoten |
| Worker → C# | `MeshBuffers` | Positionen, Normalen, Indizes, Kantenpolylinien mit Kanten-Id |
| C# → Worker | `FilletRequest` | Kanten-Ids + Radius → neuer `EdgeGraph` + `MeshBuffers` |

Struktur als JSON, Koordinaten als `Float64Array`/`Int32Array` daneben.
Aufruf über `[JSImport]`/`[JSExport]` (.NET 7+), nicht über den älteren
`IJSUnmarshalledRuntime` — der ist abgekündigt.

Der STL-Export geht **nicht** über OCC: C# schreibt ihn direkt aus
`MeshBuffers`. Spart den Umweg über das virtuelle Dateisystem von WASM.

### Nebenläufigkeit und Ausfallsicherheit

`occ-worker.js` läuft in einem dedizierten Web Worker. Begründung ist **nicht
nur** Reaktionsfähigkeit: OpenCASCADE kann bei problematischer Geometrie hart
abbrechen und den Worker mitreißen.

Daraus folgt der Zustandsschnitt: aller wiederherstellbare Zustand
(Ausgangsmesh, `BrepRecipe`, Feature-Liste) liegt in C# im Haupt-Thread.
Stirbt der Worker, startet `occ-bridge.js` ihn neu und C# spielt die Historie
ab. Nichts geht verloren.

Dieser Schnitt passt zu Blazor sogar besser als zum TypeScript-Entwurf: der
Zustand liegt ohnehin schon in verwaltetem Speicher getrennt vom Worker.

---

## Zustand und Historie

```
BrepRecipe (unveränderlich, aus der Rekonstruktion)
+ geordnete Liste von Features { Id, Selektor, Radius, Status }
```

Die aktuelle Form ist immer das Ergebnis des Abspielens dieser Liste. Es gibt
keinen zweiten, davon abweichenden Zustand. Undo/Redo ist ein Zeiger in einen
Stapel von Listenversionen (unveränderliche Listen, strukturelle Teilung).
Der Worker cacht Zwischenformen je Schritt; eine Änderung an Schritt `k` spielt
nur `k..n` neu ab.

### Kantenidentität (Topological Naming)

Nach jedem Fillet ändern sich sämtliche Kanten-Ids. Ein Selektor ist deshalb
keine Id, sondern ein geometrischer Fingerabdruck der **Startkante der Kette**:

```
{ P: Mittelpunkt, T: Tangente, N1, N2: Nachbarflächen-Normalen, L: Länge }
```

Aufgelöst wird gegen den `EdgeGraph` **dieses** Schritts beim Abspielen. Da das
Abspielen deterministisch ist, trifft ein unveränderter Schritt exakt die
ursprüngliche Auswahl.

Metrik, normiert auf `D` (Bounding-Box-Diagonale), `N1`/`N2` in beiden
Zuordnungen geprüft, bessere gewinnt:

```
score = |dP|/D + 0.5*(1 - |T·T'|) + 0.25*(1 - |N1·N1'|)
              + 0.25*(1 - |N2·N2'|) + 0.25*|dL|/D
```

Angenommen wird der beste Treffer, wenn `score < 0.05` **und** der zweitbeste
mindestens doppelt so schlecht ist. Sonst: Selektor nicht auflösbar.

Das ist reine Rechnung auf dem `EdgeGraph` — also C#, also mit xUnit testbar
ohne Browser und ohne OCC.

### Fehlgeschlagene Schritte

Selektor nicht auflösbar oder Fillet schlägt beim Abspielen fehl → Schritt
bleibt mit Status `Failed` in der Liste, wird übersprungen, alle übrigen werden
normal gerechnet, in der Oberfläche orange markiert. Neu anklicken belebt ihn.

---

## Toleranzen (Defaults)

`D` = Diagonale der Bounding-Box.

| Name | Default | Verwendung |
|---|---|---|
| `TolWeld` | `max(1e-6, 1e-5·D)` | Vertex-Verschweißung |
| `TolPlaneAngle` | 0.5° | Regionenwachstum, Normalenabweichung |
| `TolPlaneDist` | `1e-4·D` | Regionenwachstum, Ebenenabstand |
| `TolCollinear` | 0.2° | Kollineare Loop-Punkte entfernen |
| `FeatureAngle` | 30°, einstellbar 5–90 | Ab wann eine Kante Feature ist |
| `ChainKinkAngle` | 30° | Abbruch der Kettenpropagierung |
| `MinCylFacets` | 12, einstellbar | Ab wie vielen Streifen ein Fächer Zylinder ist |
| `TolSew` | `1e-4·D` | Vernähen |

---

## Stufe 1 — Ebene Flächen ✅ abgeschlossen

Ziel: Quader, Platten und Prismen sind vollständig filletfähig. **Erreicht.**

Am laufenden Programm im Browser nachgewiesen:

| Prüfung | Ergebnis |
|---|---|
| Würfel laden | 192 Dreiecke → 6 Flächen |
| Prisma laden | 80 Dreiecke → 22 Flächen (20 Mantel + 2 Deckel) |
| Klick auf Randkante des Prismas | **20 Segmente** in einem Zug ausgewählt |
| Klick auf Facettenfuge (18° Knick) | nur sie selbst — korrekt unterhalb des Schwellwerts |
| Fillet r=3,5 auf Würfelkante | Volumen 7947,114 gegen analytisch 7947,423 (rel. 4e-5) |
| Fillet r=2 auf 20-Segment-Kette | gebaut, 80 → 716 Dreiecke |
| Radius nachträglich ändern | Modell neu gerechnet, Volumen folgt |
| Undo | Feature-Liste leer, 76 Dreiecke = unverrundetes Prisma |
| STL-Export | 35884 Bytes, 716 Dreiecke, Länge exakt 84 + n·50 |

Tests: 103 xUnit (Desktop-.NET) + 20 node (Kernel), alle grün.

### 1.1 Interop-Durchstich — zuerst, vor allem anderen

Wegwerfbarer Durchstich, der die neue Kette beweist, bevor irgendetwas darauf
aufbaut:

Blazor-WASM-Projekt → `[JSImport]` → `occ-bridge.js` → Worker →
`opencascade.js` lädt → Würfel bauen → eine Kante filleten → Volumen per
`BRepGProp` messen → Zahl zurück nach C# → in der Seite anzeigen.

Beantwortet in einem Aufwasch die beiden offenen Risiken:

- Funktioniert die Kette .NET ↔ JS ↔ Worker ↔ Emscripten überhaupt, und wie
  teuer ist die Grenze bei großen Arrays?
- Werden OCC-Fehler als JavaScript-Exception sichtbar, oder bricht das Modul
  hart ab? Die Meldungsqualität der gesamten Fehlerbehandlung hängt daran.

Ergebnis wird notiert, der Code wird verworfen.

### 1.2 Projektgerüst
Repo unter `E:\Web\TinkerFillet`, `git init`. Solution mit `TinkerFillet.Core`,
`TinkerFillet.App` (Blazor-WASM-PWA-Vorlage), `TinkerFillet.Core.Tests`.
`opencascade.js` und Three.js nach `wwwroot/lib/` vendoren.
`InvariantGlobalization` an, Trimming an.

### 1.3 `Stl/StlReader.cs`, `Stl/StlWriter.cs`
Binär- und ASCII-STL lesen (Header-Heuristik zur Unterscheidung), binär
schreiben. Einheiten: STL ist einheitenlos, alles wird als Millimeter
behandelt.

### 1.4 `Mesh/Welder.cs`
Räumliches Hash-Gitter, Zellgröße `TolWeld`. Vertices innerhalb `TolWeld`
zusammenlegen. Indiziertes Mesh + Half-Edge: jede gerichtete Kante `(a,b)` mit
`(b,a)` gepaart.

Prüfungen, gesammelt gemeldet statt still verschluckt:
- gerichtete Kante ohne Gegenstück → offene Kante (Loch im Mesh)
- gerichtete Kante mehrfach → nicht-manifold

### 1.5 `Mesh/RegionGrower.cs`
Breitensuche über Dreiecke. Startdreieck definiert die Ebene. Nachbardreieck
wird aufgenommen, wenn Winkel(Dreiecksnormale, Regionsnormale) <
`TolPlaneAngle` **und** Abstand(Dreiecksschwerpunkt, Regionsebene) <
`TolPlaneDist`.

Regionsebene flächengewichtet inkrementell nachführen, alle 32 Aufnahmen neu
ausgleichen, damit sich über große Flächen kein Drift aufbaut.

### 1.6 `Mesh/LoopExtractor.cs`
Randkanten = gerichtete Kanten, deren Gegenstück zu einer anderen Region
gehört. Über Vertex-Nachbarschaft zu geschlossenen Schleifen verketten.
Klassifikation: in die Regionsebene projizieren, vorzeichenbehaftete Fläche —
betragsgrößte ist Außenrand, alle weiteren sind Löcher. Punkte entfernen, deren
beide anliegenden Segmente innerhalb `TolCollinear` kollinear sind.

Ergebnis ist der `BrepRecipe` mit Trägerflächentyp `plane`.

### 1.7 `wwwroot/js/occ-worker.js` — B-Rep-Aufbau
Aus dem `BrepRecipe`: je Fläche `Geom_Plane` + Wires aus den Loops (Segmente
sind Geraden). Dann `BRepBuilderAPI_Sewing` mit `TolSew`,
`BRepBuilderAPI_MakeSolid`, `ShapeFix_Solid`, Geschlossenheits- und
Orientierungsprüfung.

**Wichtig:** OpenCASCADEs `ShapeUpgrade_UnifySameDomain` wird bewusst **nicht**
als Ersatz für 1.5 verwendet. Es verschmilzt nur Flächen mit exakt gleicher
Trägergeometrie, nicht annähernd koplanare — STL-Dreiecke aus Tinkercad sind
durch Float-Rundung nie exakt koplanar.

### 1.8 `occ-worker.js` — Kantengraph
Je Kante die zwei Nachbarflächen, Dihedralwinkel aus den Flächennormalen am
Kantenmittelpunkt, konvex/konkav aus dem Vorzeichen des Kreuzprodukts gegen die
Kantentangente. Wird als `EdgeGraph` vollständig nach C# gereicht — die
Auswahl-Logik bleibt damit in C#.

### 1.9 `Brep/ChainPropagator.cs`
Ab der angeklickten Kante an beiden Enden. Fortsetzung, solange die Nachbarkante
Feature ist (Dihedralwinkel > `FeatureAngle`), die Tangentenrichtung innerhalb
`ChainKinkAngle` fortsetzt und dasselbe Flächenpaar begleitet (oder
tangentenstetig anschließt). Abbruch an Knoten mit ≥3 Feature-Kanten — dort ist
die Fortsetzung mehrdeutig.

Reine Graphrechnung auf dem `EdgeGraph`, also xUnit-testbar mit
handgeschriebenen Graphen ohne OCC.

### 1.10 `occ-worker.js` — Fillet und Tessellierung
`BRepFilletAPI_MakeFillet`, alle Kanten der Kette mit demselben Radius, dann
`Build()` + `IsDone()`. In try/catch; bei hartem Abbruch greift der
Worker-Neustart.

`BRepMesh_IncrementalMesh`, lineare Abweichung 0,02 mm, Winkelabweichung 12°
(0,209 rad) — fest verdrahtet, für 3D-Druck deutlich feiner als jede Düse.
Aus `Poly_Triangulation` je Fläche die Dreieckspuffer, aus
`Poly_PolygonOnTriangulation` je Kante die Linien für Anzeige und Picking.

### 1.11 `History/FeatureStore.cs`, `History/EdgeSelector.cs`
Zustandsmodell, Selektoren, Replay mit Schritt-Cache, Undo/Redo,
`Failed`-Behandlung.

### 1.12 Oberfläche
Eine Seite, kein Navigieren.
- Laden per Drag-and-drop oder Dateiauswahl, Fortschrittsanzeige während der
  Rekonstruktion (bei 100k Dreiecken spürbar)
- Viewport: Orbit/Pan/Zoom. Modell grau, Feature-Kanten dünn dunkel,
  überfahrene Kette hellblau, gewählte Kette orange
- Seitenleiste: Feature-Liste mit editierbarem Radius je Zeile, Löschen,
  orange Markierung bei `Failed`, Undo/Redo
- Toolbar: Laden, Regler `FeatureAngle`, STL exportieren
- Tastatur: `Esc` bricht Auswahl ab, `Cmd/Ctrl+Z`, `Cmd/Ctrl+Shift+Z`

**Picking** in `viewport.js`: Kanten zusätzlich als dicke Linien in einen
Offscreen-Puffer rendern, jede in eindeutiger ID-Farbe. Klick liest ein Pixel →
Kanten-Id, die an C# gemeldet wird. Unabhängig vom Blickwinkel, kein Raycast
gegen dünne Linien.

**Kein Live-Preview im Radius-Dialog.** Dialog nimmt die Zahl, rechnet, zeigt
das Ergebnis. Nachjustiert wird über das Radius-Feld in der Feature-Liste.
Damit ist die Vorschauschleife derselbe Mechanismus wie das nachträgliche
Editieren, kein zweiter daneben.

Zustände: leer → lädt → rekonstruiert → bereit → Kette gewählt → Dialog →
rechnet → bereit, dazu die Fehlerzustände.

### 1.13 Fehlerbehandlung
- **Mesh nicht sauber:** offene/nicht-manifolde Stellen zählen und im Viewport
  markieren, Reparaturversuch `ShapeFix_Shell`. Scheitert er, klare Meldung mit
  Anzahl und Position — kein unbrauchbares Ergebnis produzieren.
- **Nicht CAD-artig:** >30 % so viele Regionen wie Dreiecke → Warnung, dass die
  Fillet-Qualität schlecht wird. Fortfahren bleibt erlaubt.
- **Fillet nicht ausführbar:** Meldung, in Stufe 3 plus Radius-Vorschlag.
- **Worker-Absturz:** Neustart durch `occ-bridge.js`, Replay aus C#, für den
  Nutzer nur kurze Rechenpause.

---

## Stufe 2 — Primitiv-Erkennung ✅ abgeschlossen

Ziel: Lochränder und Zylinderkanten exakt statt facettiert. **Erreicht**, Kegel
inklusive.

Am laufenden Programm nachgewiesen:

| Modell | Vorher (Stufe 1) | Nachher |
|---|---|---|
| Scheibe mit Bohrung, 192 Dreiecke | 50 Flächen | **4 Flächen, 2 Zylinder** |
| Prisma 20-seitig, 80 Dreiecke | 22 Flächen | **3 Flächen, 1 Zylinder** |
| Kegel, 48 Dreiecke | 26 Flächen | **2 Flächen, 1 Kegel** |
| Kegelstumpf, 96 Dreiecke | 27 Flächen | **3 Flächen, 1 Kegel** |
| Würfel, 192 Dreiecke | 6 Flächen | 6 Flächen (unverändert) |

Ein Klick auf einen Rand wählt jetzt **2 Segmente** statt 20 oder 24 — der Rand
ist eine echte Kreiskante, die der Kernel nur an der Flächennaht teilt.
Verrundung am Außenrand der Scheibe: entfernt 59,557 gegen Pappus-Vorhersage
59,661.

Tests: 146 xUnit + 42 node, alle grün.

### Vier Fehler, die diese Stufe aufgedeckt hat

1. **Konvexität war falsch berechnet** — sie maß die Richtung zum
   Flächenschwerpunkt, der bei einem Kreisring aber mitten im Loch liegt. Jeder
   Bohrungsrand kam als konkav heraus, obwohl alle konvex sind. Zwei Tests
   trugen denselben Denkfehler mit und waren deshalb grün. Jetzt wird gemessen,
   wie viel Material die Kante umgibt.
2. **Knickmessung nutzte die Sehne** zum Kantenmittelpunkt. Bei einer Geraden
   ist das die Tangente, bei einem Dreiviertelbogen 90° daneben — Ketten
   brachen an jeder Naht ab. Kanten melden jetzt ihre Endtangenten.
3. **Der Kegel-Fitter fraß die Kugel.** Jede ringweise tessellierte
   Rotationsfläche zerfällt in Kegelstümpfe, weil ein Kegel durch zwei Kreise
   deren Ecken exakt trifft. Ein Kegel muss jetzt an einer echten Kante enden.
4. **Zwei Typen namens `JSException`.** `[JSImport]` wirft den aus
   `System.Runtime.InteropServices.JavaScript`, gefangen wurde der aus
   `Microsoft.JSInterop`. Kompiliert sauber, fängt nichts — jeder abgelehnte
   Fillet flog als ungefangene Ausnahme durch, statt den Schritt zu markieren.

### Bekannte Grenzen

- Nur **volle** Zylinder und Kegel (360°). Teilfächer — eine verrundete Ecke,
  eine durch eine Fläche unterbrochene Wand — bleiben die Ebenen, die sie sind.
- Kugeln und Tori werden nicht erkannt.
- Ein Sechskantprisma und ein grob tessellierter Zylinder sind in der Datei
  nicht unterscheidbar. Der Schwellwert steht als Regler in der Toolbar.

---

## Nachtrag zu Stufe 2 — Laufzeit und Fortschrittsanzeige ✅

Gemeldetes Symptom: „Ich starte die App und lade ein STL-File. Aber nichts
passiert."

Es lag nicht an der Anzeige. Alle Testmodelle bis hierher hatten zwischen 6 und
50 Flächen, und in dieser Größe ist jedes Verfahren schnell genug.

### Ursache

`edgeGraph` entschied konvex/konkav, indem es einen Ring aus 16 Punkten um jede
Kante legte und den Kernel fragte, welche davon im Körper liegen. Ein
Punkt-im-Körper-Test kostet auf einem Körper mit 228 Flächen **11,8 ms**. Bei
1844 Kanten sind das 29.504 Tests — gemessen **587 Sekunden** für ein Modell,
das der Nutzer als „lädt nicht" erlebt. Alles übrige in `edgeGraph` zusammen:
55 ms.

Zweiter Fund an derselben Stelle: die Normale einer gekrümmten Fläche wurde
durch Absuchen des Parameterbereichs bestimmt — grobes Raster plus acht
Halbierungsrunden, **113 Flächenauswertungen pro Normale**. Auf einer Platte
mit 100 Bohrungen waren das 50 der 60 Sekunden, die danach noch übrig waren.

### Behebung

Konvexität wird jetzt lokal gerechnet statt erfragt: Die Richtung, in die sich
die erste Fläche von der Kante weg erstreckt, ist `Normale × Laufrichtung` —
und ob die zweite Fläche darunter zurückweicht oder darüber hinaussteht, sagt
das Vorzeichen gegen deren Normale. Die Normalen allein können es nicht sagen;
ein konvexer und ein konkaver rechter Winkel messen beide 90°.

Dazu: `uvFromPoint` statt Parametersuche, und `shapeOrientation` statt eines
Punkt-im-Körper-Tests pro Fläche (der ohnehin jedes Mal dasselbe antwortete —
`surfaceNormal` berücksichtigt die Orientierung bereits).

| Messung | vorher | nachher |
|---|---|---|
| `edgeGraph`, Platte mit 64 Bohrungen (332 Kanten) | 31.136 ms | **326 ms** |
| `edgeGraph`, Körper mit 1344 Flächen (14.584 Kanten) | > 13 min, nie beendet | **1,2 s** |
| Scheibe mit 32.768 Dreiecken, komplett, Release | — | **1,05 s** |
| dieselbe im Debug-Dev-Server | — | **4,2 s** |

`tests/occ/scale.test.mjs` hält das fest: eine Platte mit 64 Bohrungen muss in
unter 5 Sekunden beschrieben sein, und keine ihrer Kanten darf konkav heißen.

### Fortschrittsanzeige

Eine Anzeige allein hätte das Problem nicht behoben, aber sie fehlte
tatsächlich. WebAssembly läuft auf demselben Thread, mit dem der Browser malt —
`Task.Yield` kommt nur bis ans Ende der Microtask-Queue, also **vor** jedes
Zeichnen. Deshalb meldet `Reconstructor.ReconstructAsync` jeden Schritt über
einen Haken, der ein `Task` zurückgibt, und die Oberfläche wartet dazwischen
einen Timer ab. Erst dadurch erscheint die Anzeige überhaupt.

Zehn Schritte: Datei lesen, die sieben der Rekonstruktion, Körper bauen,
darstellen. Kein Prozentbalken über die Gesamtdauer — welcher Schritt dominiert,
hängt vom Modell ab, ein gleichmäßig laufender Balken wäre erfunden. Der Spinner
dreht per `transform`, damit er auch dann weiterläuft, wenn ein Schritt den
Thread sekundenlang besetzt.

### Nebenbefund

Das Testmodell, mit dem das Problem zuerst reproduziert wurde — eine Platte mit
einem Raster runder Löcher — war **selbst kaputt**: 88 offene und 25.600
nicht-mannigfaltige Kanten, weil die Fächer-Triangulierung zwischen Lochrand und
Zellrand nicht aufgeht. Die App hat das korrekt gemeldet. Die Laufzeitzahlen
dieses Modells sind deshalb Zahlen für ein defektes Netz und stehen oben nur
dort, wo sie als solche gekennzeichnet sind. Das Maßstabsmodell ist jetzt eine
fein tessellierte Scheibe (`washer-fine.stl`, 32.768 Dreiecke, 4 Flächen).

---

## Erster echter Export — Befund ✅

`MagnetConnector Male_10x3.stl`, 5550 Dreiecke, 278 KB. Gemeldetes Symptom: die
App hängt bei Schritt 7 von 10.

### Was das Modell ist

| Messung | Wert |
|---|---|
| Dreiecke nach dem Verschweißen | 5545 (5 entartete verworfen) |
| Regionen | **2547** — im Schnitt 2,2 Dreiecke je Region |
| davon mit genau 1 Dreieck | 1222 |
| davon mit genau 2 Dreiecken | 1223 |
| Zylinder erkannt | 0 |
| Kegel erkannt | 0 |
| offene Kanten | 3 |
| Flächenwinkel unter 0,5° | 36,8 % — der Rest verteilt sich auf 0,5–10° |

Die Fläche ist fast überall gerundet. Regionenwachstum kann darauf nichts
zusammenfassen, und die Primitiv-Erkennung findet nichts, weil nichts da ist.
**Das Modell liegt außerhalb dessen, wofür das Werkzeug gebaut ist** — und der
Kernel bestätigt das unabhängig: `buildSolid` scheitert an diesen 2547 Flächen
mit `makeFace: CONSTRUCTION_FAILED`. Selbst ohne jeden Hänger käme kein Körper
heraus.

### Warum es hing

Drei Ursachen, alle im selben Bereich:

1. **`BoundingBoxDiagonal()` lief über alle Vertices — bei jedem Aufruf.** Jede
   Toleranz im Projekt ist ein Bruchteil davon, also fragen die Fitter innerhalb
   ihrer innersten Schleifen danach. Auf einem Würfel unsichtbar, hier der
   größte Einzelposten. Jetzt einmal berechnet und gemerkt.
2. **Der Kegel-Fitter probierte jedes Paar von Junctions jeder Region.** Zwei
   beliebige Kanten einer ebenen Region schneiden sich immer irgendwo, also
   schlägt eine Region mit einem Dutzend Nachbarn rund siebzig Spitzen vor und
   durchläuft für jede das Netz. Gemessen: **193.744 Paare, 191.982 Netzläufe,
   1,75 Mio. besuchte Regionen — und null Treffer.** Jetzt muss erst der
   Nachbar den Fächer fortsetzen, bevor überhaupt gelaufen wird.
3. **Drei Sammlungen je Suchlauf.** Liste, Menge und Warteschlange wurden
   zehntausendfach neu angelegt, für Antworten von zwei bis drei Regionen
   Länge. Jetzt wiederverwendet — wobei ein angenommener Fund seine Regionsliste
   kopiert, sonst würde der nächste Lauf sie unter ihm wegziehen.

| Messung (Desktop, Release) | vorher | nachher |
|---|---|---|
| Kegel-Erkennung | 5922 ms | **~330 ms** |
| Zylinder-Erkennung | 442 ms | **~350 ms** |
| gesamte Rekonstruktion | 6,5 s | **~0,9 s** |

`ReconstructorScaleTests` hält das fest: eine Kugel aus 5000 Dreiecken, die zu
2500 Regionen zerfällt, muss in unter 2 Sekunden rekonstruiert sein — mit einem
zweiten Test, der belegt, dass die Kugel wirklich der harte Fall ist und nicht
etwa doch zusammenfällt.

### Zwei Fehler, die dabei sichtbar wurden

- **Der Kernel meldet `CONSTRUCTION_FAILED` für alles, was er nicht bauen
  konnte** — der Code bedeutet je nach Anfrage etwas völlig anderes. Ein Modell,
  das es nie bis zu einem Körper schaffte, wurde dem Nutzer als „Radius zu groß"
  gemeldet, für einen Radius, den er nie eingegeben hatte. Reset und Fillet
  haben jetzt getrennte Formulierungen.
- **Die Netz-Diagnosen erschienen auf Englisch**, mitten zwischen deutschen
  Meldungen. Die Kernbibliothek stellt weiterhin auf Englisch fest, *was* sie
  gefunden hat; wie das dem Nutzer gesagt wird, entscheidet jetzt die App.

### Was der Nutzer jetzt sieht

Statt eines Hängers: vier Meldungen, die zusammen erklären, woran es liegt —
nicht geschlossen, fast nichts zusammengefallen, sieht nach gerundetem Modell
aus, kein Körper bildbar.

### Offen

Der Debug-Dev-Server bleibt für solche Modelle zäh (Faktor 20–50 gegenüber
Desktop-Release, und stark schwankend je nach Rechnerlast). Für CAD-artige
Modelle spielt das keine Rolle — `washer-fine.stl` mit 32.768 Dreiecken braucht
dort 4,2 s und im Release 1,05 s.

---

## Zweiter echter Export — funktioniert ✅

`obj_1_Corpo5.stl`, 6228 Dreiecke, 20,25 × 58,68 × 6 mm. Eine flache Platte mit
erhabener Schrift und Schraffur — daher 1462 Flächen bei nur 6228 Dreiecken.

Anders als die erste Datei ist das **CAD-Geometrie**: geschlossen, größte Region
687 Dreiecke, 50,8 % der Flächenwinkel unter 0,5°. Trotzdem scheiterte sie
zunächst zweimal.

### Absturz in Schritt 5

`LoopExtractor` warf `InvalidOperationException: the outline of region 836
revisits vertex 1045` — in der App eine ungefangene Ausnahme, also der rote
Blazor-Balken statt einer Erklärung.

Ursache: **zwei Kanten tragen je vier Dreiecke** statt zwei, dort wo sich zwei
Features berühren. Der Rand der Fläche daneben läuft dadurch zweimal durch
denselben Eckpunkt. Der Tracer merkte sich aber nur *einen* Ausgang je Eckpunkt
(`successor[vertex] = halfEdge`), verlor damit eine ganze Schleife und riss den
Rest auseinander.

Behoben: mehrere Ausgänge je Eckpunkt, einzeln verbraucht. Ein Rand, der sich
selbst in einem Punkt berührt, zerfällt damit von selbst in die richtigen
geschlossenen Schleifen. Lässt er sich trotzdem nicht schließen, wird die
Region gemeldet (`DiagnosticKind.UntraceableFaces`) und übersprungen —
**niemals mehr geworfen**. Eine Ausnahme an dieser Stelle kostet den Nutzer
genau die Diagnose, die sein Modell erklärt hätte.

### 165 Flächen, die der Kernel ablehnte

Danach lief die Rekonstruktion durch, aber `makeFace` verweigerte 165 der 1462
Flächen. Gemessen:

| Flächen | Abweichung von der eigenen Ebene |
|---|---|
| angenommen (1296) | ≤ 1,3e-14 — exakt eben |
| abgelehnt (165) | 1,7e-6 bis **3,6e-3** |

Regionenwachstum nimmt ein Dreieck auf, dessen Schwerpunkt innerhalb `1e-4·D`
(hier 6,2e-3) an der Ebene liegt. Die Eckpunkte wurden dann **roh aus dem Netz**
in die Beschreibung kopiert — also *fast* eben. Der Kernel will für eine ebene
Fläche einen exakt ebenen Rand und lehnt alles andere ab.

Behoben: die Randpunkte werden auf die Ebene der Region projiziert. Das ist
gefahrlos, weil die Verschiebung durch dieselbe Toleranz begrenzt ist, mit der
die Flächen danach vernäht werden — ein Eckpunkt, den sich zwei Flächen teilen,
wird zu beiden Ebenen gezogen und trifft sich trotzdem innerhalb der Naht.

### Ergebnis

| | |
|---|---|
| abgelehnte Flächen | 165 → **0** |
| Körper | **schließt sich**, Volumen 3809,65 mm³ |
| Kanten | 3477, davon 2011 scharf (1158 konvex, 853 konkav) |
| Verrundung | gebaut und Material entfernt, größter tragfähiger Radius 0,98 mm |
| gesamte Rekonstruktion | ~1,2 s Desktop-Release |

`PinchedOutlineTests` hält das fest, mit `MeshFixtures.PlateWithTouchingHoles` —
einer Platte, deren zwei Löcher sich in einer Ecke berühren und damit genau den
Vier-Dreiecke-Defekt erzeugen.

Verbleibend: 57 Kanten ohne Flächenpaar, Rest der acht nicht-mannigfaltigen
Kanten. Die App meldet sie, der Körper entsteht trotzdem.

---

## Drei weitere echte Exporte — und was sie aufdeckten ✅

| Datei | Dreiecke | Größe | Ergebnis |
|---|---|---|---|
| `Epic Migelo (1).stl` | 28 | 66 × 38 × 22 | Körper, 11 Flächen, 24 Kanten, **0 freie** |
| `Epic Migelo.stl` | 1054 | 34 × 33 × 34 | Körper, 258 Flächen, 640 Kanten, **0 freie** |
| `Orca Klotür.stl` | 1108 | 54,5 × 12,5 × 14 | Körper, 280 Flächen, 828 Kanten, **0 freie** |

Alle drei geschlossen, keine nicht-mannigfaltigen Kanten, Rekonstruktion unter
250 ms. Zwei davon kamen aber erst nach zwei Korrekturen so heraus.

### Ein schlecht sitzendes Primitiv zerlegte das Modell

`Epic Migelo.stl` ergab **keinen Körper**, sondern ein Compound aus losen
Flächen — 131 Kanten ohne Partner. Ursache war ein einzelner erkannter Kegel:

| | |
|---|---|
| Abstand des Randes von der gefitteten Kegelfläche | **4,7e-2 mm** |
| Toleranz, innerhalb derer ein Rand als Kreis erkannt wird | 6,8e-6 mm |
| Kreis-Schleifen in der Beschreibung | **1** — nur die des Kegels selbst |

Der Fitter akzeptierte den Kegel (sein eigenes Residuum lag in der Toleranz),
aber **keine Nachbarfläche erkannte ihren Rand darauf**. Die exakte Kegelwand
stand damit neben Polygonzügen, die nur ungefähr dort liegen. Ohne Kegel
entstand sofort ein sauberer Körper aus 258 Flächen.

Neue Regel: **ein erkannter Zylinder oder Kegel wird nur verwendet, wenn seine
Ränder auch gefunden wurden.** Ein Zylinder braucht zwei, ein voller Kegel
einen, ein Kegelstumpf zwei. Sonst zählt er nicht und seine Facetten bleiben
die Ebenen, die sie sind. Das ist genau das Risiko, das für Stufe 2.3 notiert
war — nur dass „die Schleifen liegen exakt auf der Trägerfläche" eben auch
geprüft werden muss und nicht bloß gehofft.

Wirkung: `Epic Migelo.stl` 131 freie Kanten → **0**, `Orca Klotür.stl` 326 → **0**.

### Vernähen scheitert nicht, es liefert Bruchstücke

`buildSolidFromFaces` verweigert nichts. Bekommt es Flächen, die sich nicht
treffen, gibt es ein Compound zurück — das aussieht wie eine Form, sich zeichnen
und anklicken lässt, und auf dem **nie ein Fillet baut**. `buildSolid` prüft
jetzt `isSolid` und wirft sonst `NOT_A_SOLID`. Ein Modell, das auseinanderfällt,
muss das dort sagen, wo es passiert.

### Falscher Alarm bei kleinen Modellen

`Epic Migelo (1).stl`: 28 Dreiecke, 11 Flächen — und die Warnung „sieht nach
einem gescannten Modell aus". Die Kennzahl ist der Anteil Flächen an Dreiecken,
Schwelle 30 %. **Ein nackter Quader liegt bei 50 %.** Die Zahl sagt erst etwas,
wenn es überhaupt genug Flächen gibt, dass Zusammenfassen eine Chance hatte;
Untergrenze jetzt 50 Flächen.

### Beobachtung ohne Fehler

`Epic Migelo.stl` ist ein Low-Poly-Modell: 258 exakt ebene Facetten, 256 konvexe
und 128 konkave scharfe Kanten. Eine Verrundung einer einzelnen Kante trägt
OpenCASCADE dort über die Facettierung weiter und **vergrößert** das Volumen,
weil sie über konkave Kanten mitläuft. Das ist Verhalten des Kernels, kein
Fehler der App — aber Verrundungen auf Low-Poly-Modellen tun nicht unbedingt
das, was man erwartet.

---

## Kante anklicken — ging auf skalierten Bildschirmen gar nicht ✅

Gemeldet: „Ich sehe das Objekt, kann aber keine Kante selektieren."

### Die Trefferzone saß woanders als das Bild

`resize()` setzte die Pick-Textur in **CSS-Pixeln**, `pick()` rechnet die
Cursorposition aber mit dem Pixelverhältnis in Pufferkoordinaten um. Bei 100 %
Anzeigeskalierung ist beides dasselbe und nichts fällt auf. Gemessen bei 150 %:

| | |
|---|---|
| Zeichenpuffer | 1056 × 1072 |
| Pick-Textur | **704 × 715** |
| Folge | Suche landet bei **2/3** der sichtbaren Position |

Der Nutzer zielt auf die Kante und der Code schaut links oben daneben. Ein
Raster über die ganze Leinwand fand 191 Treffer statt 434, und alle an der
falschen Stelle. Die Textur wird jetzt wie die Leinwand in Puffer-Pixeln
angelegt.

Windows läuft üblicherweise auf 125 % oder 150 %. Auf meinem Prüfpanel stand
`devicePixelRatio` auf 1 — deshalb ist es bei allen bisherigen Tests nie
aufgefallen, und deshalb wurde es erst durch ein erzwungenes 1,5 beweisbar.

### Die Toleranzzone war zu klein und schrumpfte mit der Skalierung

WebGL zeichnet Linien immer einen Pixel breit — `linewidth` wird ignoriert, der
„dicke Linien"-Teil des Entwurfs war nie umsetzbar. Die Suche um den Cursor
*ist* deshalb die Toleranzzone, und sie war in Gerätepixeln angegeben: 6 Pixel,
auf einem 150 %-Bildschirm also 4 CSS-Pixel. Jetzt 9 CSS-Pixel, hochgerechnet.

### Beim Überfahren leuchtete eine Kante statt der Kette

Der Plan sagt „überfahrene Kette hellblau". Gebaut war: **eine** Kante hellblau.
An einem gerundeten Rand ist das einer von zwanzig Bögen — der Nutzer sieht
einen Strich aufleuchten und weiß nicht, was ein Klick tut. Die Kette wird jetzt
schon beim Überfahren propagiert und vollständig hervorgehoben; an der getesteten
Stelle sind das **51 Segmente statt einem**.

---

## Stufe 2 — Umsetzung

Ohne diesen Schritt besteht ein Tinkercad-Lochrand aus N Einzelkanten; der
Fillet darauf ist langsam, fragil und facettiert. Mit Erkennung ist der
Lochrand **eine** Kreiskante und der Fillet ein exakter Torus.

### 2.1 `Mesh/PrimitiveFitter.cs` — Zylinder
Kandidat: benachbarte Streifenregionen mit parallelen gemeinsamen Kanten.
Achsrichtung aus deren Mittel. Jede Streifenebene ist Tangentialebene, Abstand
Achse→Ebene ist `r`. Achslage (2 Freiheitsgrade senkrecht zur Achse) und `r`
per kleinster Quadrate.

Annahme nur wenn Streifenanzahl ≥ `MinCylFacets` **und** maximales Residuum
< 0,5 % von `r`.

**Bekannte Mehrdeutigkeit:** Ein sechseckiges Prisma und ein aus sechs Facetten
tessellierter Zylinder sind im STL dieselbe Geometrie — es gibt kein Signal,
das sie unterscheidet. `MinCylFacets` ist die bewusst gewählte, für den Nutzer
sichtbare und einstellbare Grenze. Nicht behoben, nur abgefedert.

### 2.2 `Mesh/PrimitiveFitter.cs` — Kegel
Analog, aber die gemeinsamen Kanten konvergieren zu einer Spitze. Achse aus der
Ausgleichsgeraden der Kantenrichtungen, Halbwinkel und Apex per kleinster
Quadrate.

Kugel ist **nicht** Teil dieser Stufe: Kugeln haben keine harten Kanten und
sind damit selten Fillet-Ziel.

### 2.3 `BrepRecipe` und `occ-worker.js` erweitern
Trägerflächentypen `cylinder` und `cone` im Recipe; im Worker
`Geom_CylindricalSurface` und `Geom_ConicalSurface`, Randschleifen als
`Geom_Circle`-Kanten. Die Schleifen müssen exakt auf der Trägerfläche liegen,
sonst scheitert das Vernähen.

---

## Stufe 3 — Feinschliff

### 3.1 Größten möglichen Radius vorschlagen
Schlägt der Fillet fehl, Binärsuche zwischen 0 und dem angefragten Radius,
6 Iterationen. Im Worker mit Fortschrittsanzeige, abbrechbar.

### 3.2 Auslieferungsgröße prüfen
Ersetzt den ursprünglich geplanten Custom-WASM-Build. `occt-wasm` ist bereits
ein kuratierter Build mit 22,2 MB, ein eigener Emscripten-Build wäre erheblicher
Aufwand für unklaren Gewinn — und würde die Docker/Emscripten-Toolchain wieder
einschleppen, die Stufe 1 und 2 bewusst nicht brauchen.

Stattdessen: messen, was Brotli-Kompression und der Service-Worker-Cache real
bringen, und erst dann entscheiden, ob mehr nötig ist.

### 3.3 PWA / Offline / Deployment
Blazor-PWA-Vorlage, Service Worker cacht .NET-Runtime, App und OCC-WASM.
Statisches Deployment auf GitHub Pages.

Drei bekannte Stolperfallen, die eingeplant sind:
- Das Repo muss **öffentlich** sein. GitHub Free liefert keine Pages aus
  privaten Repos; ein auf privat gestelltes Repo depubliziert eine bestehende
  Seite automatisch.
- `.nojekyll` im Publish-Verzeichnis, sonst ignoriert GitHub Pages die
  `_framework`-Ordner
- `index.html` darf **nach** dem Publish nicht von Hand geändert werden — die
  Integritätsprüfung über `service-worker-assets.js` bricht sonst. Das
  `<base href>` wird über den Build gesetzt, nicht nachträglich editiert.

Die vendorte OCC-`.wasm` (~35 MB) liegt damit im öffentlichen Repo. Unter dem
100-MB-Limit von GitHub, wird selten aktualisiert — Git LFS ist nicht nötig.

Modelle werden nie hochgeladen, die gesamte Rechnung passiert im Browser.

---

## Verifikation

Testgetriebene Entwicklung.

### Ebene 1 — `TinkerFillet.Core.Tests`, xUnit auf Desktop-.NET
Der Großteil. Kein Browser, kein WASM, kein OCC, normaler Debugger.

Fixtures synthetisch erzeugt, nicht als Binärdateien eingecheckt: Würfel mit
unterteilten Flächen (jede Seite 4×4 Dreieckspaare) · Platte mit einem
Durchgangsloch · Zylinder mit 20 Seiten · sechseckiges Prisma (Abgrenzung gegen
Zylindererkennung) · nicht-manifoldes Mesh.

Konkrete Zahlen, nicht "läuft durch":
- `Welder`: erwartete Vertexanzahl, jede gerichtete Kante genau einmal gepaart
- `RegionGrower`: unterteilter Würfel ergibt **genau 6** Regionen
- `LoopExtractor`: Platte mit Loch ergibt **einen** Außen- und **einen**
  Innenloop, kollineare Punkte entfernt
- `PrimitiveFitter`: 20-seitiger Zylinder wird **eine** Zylinderfläche, Radius
  und Achse innerhalb Toleranz
- `PrimitiveFitter`: sechseckiges Prisma wird bei `MinCylFacets = 12`
  **nicht** als Zylinder erkannt
- `ChainPropagator`: handgeschriebener `EdgeGraph` — Kette um einen Lochrand
  läuft vollständig um; Kette bricht am Knoten mit drei Feature-Kanten ab
- `EdgeSelector`: Fingerabdruck trifft die richtige Kante wieder; bei zwei
  fast gleichen Kandidaten wird korrekt als nicht auflösbar gemeldet
- `FeatureStore`: Radius ändern und zurückändern → identischer Zustand;
  Schritt so ändern, dass ein späterer Selektor bricht → genau dieser wird
  `Failed`, alle anderen werden gerechnet

### Ebene 2 — `tests/occ`, `node --test`
Nur die JavaScript-Kernelschicht. Nutzt Nodes eingebauten Test-Runner und
importiert die vendorte `opencascade.js` direkt — **keine npm-Pakete, kein
`node_modules`**.

Der wichtigste Test, weil nicht tautologisch: Ein Fillet mit Radius `r` auf
eine Würfelkante der Länge `L` entfernt analytisch exakt

```
ΔV = (1 − π/4) · r² · L
```

Volumen vor und nach dem Fillet per `BRepGProp` messen, gegen die Formel prüfen
(relative Toleranz 1e-3). Für den Fillet an einem runden Lochrand analog über
die Torus-Formel.

Damit ist belegt, dass tatsächlich gerundet wird — nicht nur, dass irgendein
plausibel aussehendes Ergebnis entsteht.

Dazu: `BrepRecipe` mit bekannter Geometrie hinein, `EdgeGraph` heraus, und
prüfen, dass Kantenanzahl und Dihedralwinkel stimmen.

`scale.test.mjs` misst zusätzlich die Laufzeit, weil jeder andere Test hier auf
so wenigen Flächen läuft, dass jedes Verfahren schnell genug wirkt. Eine Platte
mit 64 Bohrungen muss in unter 5 Sekunden beschrieben sein — weit über dem, was
die Arbeit kostet, und weit unter dem, was ein Punkt-im-Körper-Test pro Kante
kostet, damit die Grenze eindeutig und nicht knapp ist.

### Ebene 3 — Durchstich
Playwright: Anwendung starten, STL laden, Kante klicken, Radius eingeben,
exportieren, die exportierte Datei parsen und ihr Volumen prüfen.

### Manuell am Ende jeder Stufe
Echte Tinkercad-Exporte durchschieben — nach Stufe 1 eine Platte mit
abgesetzter Kante, nach Stufe 2 eine Platte mit Durchgangsloch.

---

## Offene Risiken

1. ~~**Interop-Kette .NET ↔ JS ↔ Worker ↔ Emscripten**~~ — **erledigt in 1.1.**
   8–12 ms für 7,2 MB durch die ganze Kette. Kein Engpass.
2. ~~**Verhalten bei OCC-Exceptions**~~ — **erledigt in 1.1.** Fangbare
   `OcctError` mit Fehlercode, kein hartes Abbrechen, Kernel danach weiter
   benutzbar.
2a. **Versionsdrift von `occt-wasm`** ist das neue Risiko an dieser Stelle.
   Zwischen 4.3.1 und 5.0.0 lagen drei Wochen. Version exakt pinnen, Updates
   nur bewusst und gegen die Testsuite.
3. **B-Rep-Aufbau gekrümmter Flächen (2.3)** ist der fiddligste Teil.
   Randschleifen erkannter Zylinder müssen exakt auf der Trägerfläche liegen,
   sonst scheitert das Vernähen. *Mitigation:* Stufe 1 liefert bereits ein
   nutzbares Werkzeug, Stufe 2 ist davon sauber getrennt.
4. **Laufzeit bei großen Modellen.** ⚠️ **eingetreten und behoben** — siehe
   „Nachtrag zu Stufe 2". Die Ursache war nicht die Mesh-Analyse, auf die
   dieser Punkt zielte, sondern der Kantengraph: ein Punkt-im-Körper-Test pro
   Kante. Stand jetzt: 32.768 Dreiecke in 1,05 s (Release) bzw. 4,2 s
   (Debug-Dev-Server), für ein Modell, das zu vier Flächen zusammenfällt.

   Der zweite Teil - viele Flächen aus der Rekonstruktion - ist inzwischen
   ebenfalls eingetreten und behoben, siehe „Erster echter Export". Ein echtes
   Modell erreicht diesen Fall, und zwar genau dann, wenn es gerundet ist.
5. **Prisma/Zylinder-Mehrdeutigkeit (2.1)** ist prinzipiell nicht auflösbar.
6. **Downloadgröße**: OCC-WASM 22,2 MB unkomprimiert dominiert, .NET-Runtime
   kommt mit ~2–3 MB komprimiert obendrauf. Durch Service Worker nur einmalig.
   Deutlich kleiner als die ursprünglich angenommenen 35 MB.

---

## Quellen

- [Blazor PWA](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0) und [Standalone-Hosting inkl. GitHub Pages](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly/?view=aspnetcore-10.0)
- [`[JSImport]`/`[JSExport]`-Interop](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0)
- [Service-Worker-Verbesserungen für Hosting ohne eigenen Server](https://github.com/dotnet/aspnetcore/issues/65083)
- [Blazor WASM auf GitHub Pages veröffentlichen](https://dev.to/j_sakamoto/the-easier-way-to-publish-your-blazor-webassembly-app-for-github-pages-319l)
- [opencascade.js](https://github.com/donalffons/opencascade.js/)
- [BRepFilletAPI_MakeFillet](https://dev.opencascade.org/doc/refman/html/class_b_rep_fillet_a_p_i___make_fillet.html)
- [ShapeUpgrade_UnifySameDomain](https://dev.opencascade.org/doc/refman/html/class_shape_upgrade___unify_same_domain.html) (und dessen Grenzen)
- [stl2step](https://github.com/BlinkingSun/stl2step) und [mesh2step](https://github.com/tommasobbianchi/mesh2step) — Referenzpipelines
- [Fusion 360 Fillet](https://help.autodesk.com/view/fusion360/ENU/?guid=SLD-FILLET-SOLID) — Referenzverhalten
- [ButterySpace Round STL](https://butteryspace.com/guides/how-to-round-stl-edges/) — vorhandene, unzureichende Alternative
