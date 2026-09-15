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
| CAD-Kern | opencascade.js über JS-Interop, im Web Worker | Es gibt keinen .NET-B-Rep-Kernel mit Fillet — geprüft |
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
- **Entfällt:** npm und `node_modules`. `opencascade.js` und Three.js werden
  als fertige Dateien nach `wwwroot/lib/` gelegt.
- **Wird besser als im TS-Entwurf:** Die Kerntests laufen als xUnit auf
  Desktop-.NET — schneller, kein Browser, normaler Debugger.

---

## Umgebung (geprüft)

- .NET SDK 10.0.400, dazu 9.0.308 und 8.0.424
- Workload `wasm-tools` bereits installiert
- node 24.0.0 (nur für die OCC-Tests nötig, siehe Verifikation — **ohne** npm-Pakete)
- git 2.41.0

Nichts nachzuinstallieren.

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
    Components/FeatureList.razor, RadiusDialog.razor, Toolbar.razor
    Interop/OccBridge.cs       [JSImport] auf occ-bridge.js
    Interop/ViewportBridge.cs  [JSImport] auf viewport.js
    wwwroot/
      lib/opencascade/         vendored: opencascade.full.js + .wasm
      lib/three/               vendored: three.module.js
      js/occ-bridge.js         Main-Thread-Seite, startet und überwacht Worker
      js/occ-worker.js         Worker: OCC laden, Recipe bauen, Fillet, Tessellieren
      js/viewport.js           Three.js-Szene, Kamera, ID-Puffer-Picking
      manifest.webmanifest, service-worker.js, .nojekyll
tests/
  TinkerFillet.Core.Tests/     xUnit, läuft auf Desktop-.NET
  occ/                         node --test, testet occ-worker.js direkt
```

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

## Stufe 1 — Ebene Flächen

Ziel: Quader, Platten und Prismen sind vollständig filletfähig.

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

## Stufe 2 — Primitiv-Erkennung

Ziel: Lochränder und Zylinderkanten exakt statt facettiert.

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

### 3.2 Custom-WASM-Build von OpenCASCADE
Nur `BRep`, `BRepBuilderAPI`, `BRepFilletAPI`, `BRepMesh`, `BRepGProp`,
`ShapeFix`, `ShapeAnalysis`, `Geom`, `TopExp`, `TopoDS` einkompilieren.
Erwartung: von ~35 MB auf 5–10 MB.

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

### Ebene 3 — Durchstich
Playwright: Anwendung starten, STL laden, Kante klicken, Radius eingeben,
exportieren, die exportierte Datei parsen und ihr Volumen prüfen.

### Manuell am Ende jeder Stufe
Echte Tinkercad-Exporte durchschieben — nach Stufe 1 eine Platte mit
abgesetzter Kante, nach Stufe 2 eine Platte mit Durchgangsloch.

---

## Offene Risiken

1. **Interop-Kette .NET ↔ JS ↔ Worker ↔ Emscripten** ist der neue, im
   TypeScript-Entwurf nicht vorhandene Risikopunkt. Insbesondere die Kosten für
   große Arrays über zwei Hops. *Mitigation:* Durchstich 1.1 vor allem anderen;
   die Grenze wird einmal pro Operation überquert, nicht pro Frame.
2. **Verhalten von opencascade.js bei OCC-Exceptions**: sichtbar als
   JavaScript-Exception, oder hartes Abbrechen des Moduls? Der Worker-Neustart
   deckt beide Fälle ab, aber die Meldungsqualität hängt daran. Wird in 1.1
   mitbeantwortet.
3. **B-Rep-Aufbau gekrümmter Flächen (2.3)** ist der fiddligste Teil.
   Randschleifen erkannter Zylinder müssen exakt auf der Trägerfläche liegen,
   sonst scheitert das Vernähen. *Mitigation:* Stufe 1 liefert bereits ein
   nutzbares Werkzeug, Stufe 2 ist davon sauber getrennt.
4. **Laufzeit bei großen Modellen.** Richtwert 100k Dreiecke: Mesh-Analyse
   < 2 s, B-Rep-Aufbau < 3 s, Fillet < 2 s. Deutliche Verfehlung → Regionen-
   bildung auf `Span<T>`/Arrays statt Objektlisten umstellen.
5. **Prisma/Zylinder-Mehrdeutigkeit (2.1)** ist prinzipiell nicht auflösbar.
6. **Downloadgröße**: OCC-WASM ~35 MB dominiert, .NET-Runtime kommt mit ~2–3 MB
   komprimiert obendrauf. Durch Service Worker nur einmalig, durch Custom-Build
   in 3.2 deutlich reduzierbar.

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
