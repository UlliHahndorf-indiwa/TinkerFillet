# TinkerFillet

Rundet Kanten an STL-Modellen ab — im Browser, ohne Upload, ohne Installation.

**→ [tinkerfillet.steinpilz42.de](https://tinkerfillet.steinpilz42.de)**

STL laden · Kante anklicken · Radius in mm eingeben · STL exportieren.

---

## Wofür

Tinkercad kann alles, was man zum Modellieren für den 3D-Druck braucht — außer
Fillets: weiche Abrundungen an harten Kanten. Der übliche Ausweg ist Fusion 360,
ein sehr großes Programm für eine einzige fehlende Funktion, mit erzwungenen
Updates, langem Start und einer Personal-Use-Lizenz, die regelmäßig neu
bestätigt werden will.

TinkerFillet macht genau diese eine Sache, als Lesezeichen neben Tinkercad.

## Wie es funktioniert

STL ist Dreieckssuppe ohne Topologie — eine „Kante" gibt es darin nur implizit,
als Kette von Dreieckskanten mit großem Winkel dazwischen. Ein echter Fillet
braucht aber eine B-Rep-Darstellung. Die wird deshalb zuerst rekonstruiert:

```
STL → Vertices verschweißen → koplanare Regionen → Randschleifen
    → Zylinder und Kegel erkennen → Flächenbeschreibung
    → OpenCASCADE: Flächen bauen, vernähen, Solid
    → Klick: Kantenkette → Fillet → Tessellierung → STL
```

Das Ergebnis ist ein exakter Fillet auf echter Geometrie, keine nachträglich
geglättete Dreiecksfläche.

## Was es kann und was nicht

Gedacht für **CAD-artige STLs** im Stil von Tinkercad: ebene Flächen, Zylinder,
Kegel. Damit sind Quader, Platten, Prismen, Bohrungen und Lochränder
filletfähig.

Nicht gedacht für organische oder gescannte Modelle. Ein Modell, das sich nicht
in saubere Flächen zerlegen lässt, wird nicht stillschweigend schlecht
verrundet — die Anwendung sagt, dass sie es nicht kann.

Ebenfalls nicht enthalten: Fasen, über die Kante veränderliche Radien,
Boolesche Operationen, STEP-Export.

## Datenschutz

Die Datei verlässt den Rechner nicht. Rekonstruktion, CAD-Kernel und Export
laufen vollständig im Browser; es gibt keinen Server, an den etwas ginge. Nach
dem ersten Laden funktioniert die Anwendung offline.

## Entwicklung

Gebraucht werden das .NET-SDK 10 mit der Workload `wasm-tools` und Node 24.
Keine npm-Pakete — die beiden JavaScript-Abhängigkeiten liegen als Dateien im
Repo.

```bash
dotnet run --project src/TinkerFillet.App    # Entwicklungsserver
dotnet test                                  # Algorithmenkern, xUnit
node --test "tests/occ/*.test.mjs"           # CAD-Kernel, gegen Formeln geprüft
node tools/make-test-stl.mjs                 # Beispielmodelle nach wwwroot/dev
```

Die Kernel-Tests sind die interessanten: sie messen das Volumen vor und nach
einem Fillet und vergleichen es mit dem analytisch berechneten Wert. Damit ist
belegt, dass tatsächlich gerundet wird — nicht nur, dass ein plausibel
aussehendes Ergebnis entsteht.

Der Aufbau, die Toleranzen und die Begründungen hinter den Entscheidungen
stehen in [PLAN.md](PLAN.md).

### Veröffentlichen

`.github/workflows/deploy.yml` testet, veröffentlicht und stellt auf GitHub
Pages bereit — bei jedem Push auf `main`. Ein lokal erzeugtes Publish-Verzeichnis
lässt sich vorher so ausliefern, wie ein statischer Host es tut:

```bash
node tools/serve-publish.mjs artifacts/pages/wwwroot
```

## Verwendete Bibliotheken

| | |
|---|---|
| [occt-wasm](https://www.npmjs.com/package/occt-wasm) 5.0.0 | OpenCASCADE als WebAssembly · MIT oder Apache-2.0 |
| [three.js](https://threejs.org) 0.186.0 | 3D-Anzeige · MIT |

Beide liegen fest eingebunden unter `src/TinkerFillet.App/wwwroot/lib/`, jeweils
mit einer `VENDORED.md`, die Herkunft, Version und Änderungen festhält.

---

© 2026 by [Porcino3D](https://makerworld.com/de/@ulli_hahndorf)
