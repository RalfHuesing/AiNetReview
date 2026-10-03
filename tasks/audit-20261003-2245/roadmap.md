# Roadmap: Audit-Befunde (Audit 20261003-2245)

Basierend auf dem Audit-Bericht [`audit-report.md`](audit-report.md) und den Systembelegen in [`system-evidence.md`](system-evidence.md). Diese Roadmap dient der schrittweisen, nachvollziehbaren Abarbeitung und Dokumentation aller identifizierten Befunde und Härtungspunkte.

Die Punkte sind strikt nach Priorität und empfohlener Umsetzungsreihenfolge gegliedert. Jeder Teilschritt kann einzeln abgehakt (`[x]`) werden.

## Selected implementation scope (2026-10-03)

Only F-001, F-003, and the configuration-aware executable selection in F-004 are authorized for this implementation. F-002, release pipeline changes (4.2), release-script alignment (4.3), and P3 follow-ups remain outside this scope. Existing unchecked items below retain their original broader audit recommendations.

- [ ] F-001: restore cross-compilation static test edges and verify the self-review regression.
- [x] F-003: clean up interrupted configuration bootstrap and verify retry/preservation.
- [ ] F-004 (4.1 only): select the current Debug/Release host artifact and verify Release execution.
- [ ] Final verification and independent GPT-6.1-Sol/medium audit; Luna fix rounds if required.

Implementation baseline: `4f7a0a741aba4b3416852fdaa3507657d37e2366`; working tree clean before changes. Implementation agents: GPT-6-Luna/high. The orchestrator owns this checklist and task-slice commits.

F-003 verification: the new cancellation regression failed against the original direct write (final file remained). After the fix, 19 ZeroConfig tests and a focused 20-test bootstrap/cancellation selection passed. Same-directory temporary publication preserves existing configuration and permits a successful retry; cleanup is restricted to the owned temporary file. Broad gates and the final audit follow after all selected fixes.

---

## Übersicht & Priorisierung

| ID | Priorität | Bereich | Kernproblem |
|---|---|---|---|
| **F-001** | **P1** | Core / MissingTestEvidence | Gebundene FastTests→Core-Aufrufe gehen beim Symbol-Lookup verloren; getesteter Code wird als ungetestet gemeldet |
| **F-003** | **P2** | Host / CLI Bootstrap | Abgebrochener oder fehlgeschlagener Bootstrap hinterlässt leere `ainetreview.json`, die alle Folgestarts blockiert |
| **F-002** | **P2** | Core / StructuralDuplicateDetector | Vollständige Normalisierung & String-Materialisierung redundanter Teilfragmente führt zu kubischem Aufwand / Allokationsexplosion |
| **F-004** | **P2** | Tests / CI Release-Workflow | Host-Prozesstest verlangt zwingend Debug-Binary; GitHub-Tag-Workflow baut Release ohne Smoke-/Test-Gates |
| **P3-Klärungen** | **P3** | Core / Reporting / Config | 5 unbestätigte Randfälle / Härtungshypothesen (Symlink-Wurzel, Peer-Links, Lookalikes, Rekursion) |
| **Gesamtabnahme** | **Gate** | Repo-weit | Vollständige Verifikation, Regressionstests & Self-Review-Abgleich |

---

## 1. Priorität 1 (P1): F-001 — Gebundene Testaufrufe gehen am Symbol-Lookup verloren

**Ziel:** `MissingTestEvidenceSemanticGraphBuilder` muss Aufrufe über Projekt- und Kompiliationsgrenzen hinweg (speziell `FastTests` → `Core`) korrekt dem geladenen Quellknoten zuordnen, sodass aktiv getestete Methoden nicht als fehlerhafte Missing-Test-Findings emittiert werden.

- [ ] **1.1 Reproduktions- und Charakterisierungs-Test aufsetzen**
  - [ ] Echten MSBuildWorkspace-Testfall mit separatem Test- und Produktionsprojekt erstellen (analog zu `analyses/GraphEdgeProbe/Program.cs`).
  - [ ] Nachweisen, dass Aufrufe von `CodeLineMetricsTests` auf `CodeLineMetrics.CountExecutableDeclaration` bei aktuellem Stand im Graphen verworfen werden.
  - [ ] Dokumentieren, dass `SymbolEqualityComparer.Default.Equals` trotz identischer Assembly-Identität, Documentation-ID und Quellort zwischen den Kompilationen `false` zurückgibt.

- [ ] **1.2 Cross-Compilation-Symbolauflösung im Graph-Builder implementieren**
  - [ ] `src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceSemanticGraphBuilder.cs`:
    - Normalisierung und Lookup in `GraphCollector.AddEdge` (`nodes.TryGetValue`) so erweitern, dass Compiler-Symbole aus referenzierenden Projekten anhand von Assembly-Identität, Deklarations-ID / Signatur und Quellort eindeutig dem geladenen Zielknoten zugeordnet werden.
    - Bestehende Muster aus `SolutionReferenceIndex` und `TypeDependencyGraphBuilder` für deterministische Ownership nutzen.
  - [ ] Sicherstellen, dass keine uneindeutigen Symbole oder reine Namensgleichheiten geraten werden (Fail-Safe: keine falschen Kanten).
  - [ ] Verhindern, dass externe Metadaten-Ziele versehentlich zu Quellknoten transformiert werden.

- [ ] **1.3 Erhaltung von Sonderfällen und Randbedingungen prüfen**
  - [ ] Überladungen (Overloads) bleiben strikt getrennt (z. B. `CountTokenStartLines(SyntaxNode)` vs. `CountTokenStartLines(IEnumerable<SyntaxToken>)`).
  - [ ] Gleichnamige Typen und Methoden in unterschiedlichen Projekten/Namespaces bleiben disjunkt.
  - [ ] Generische Typen/Methoden und partielle Klassen (`partial`) werden korrekt aufgelöst.
  - [ ] Generierte Zwischenknoten und Unsicherheitsfortpflanzung (Virtual/Interface-Uncertainty) bleiben erhalten.
  - [ ] Bestehende AdhocWorkspace-Tests behalten volle Gültigkeit.

- [ ] **1.4 Regression & Review-Lauf verifizieren**
  - [ ] Prüfen: Direkter `[Fact]`-Aufruf erzeugt die Kante FastTests→Core und unterdrückt das fehlerhafte Finding `finding-cda6805d589b65b5091d41ed`.
  - [ ] Produktions-Zwischenknoten behalten den dokumentierten indirekten Testpfad.
  - [ ] Ungetestete Methoden bleiben weiterhin korrekt als `NoPath` klassifiziert.
  - [ ] Frischen Self-Review (`review .`) ausführen und bestätigen, dass berechtigte Findings erhalten bleiben und Falsch-Positive verschwinden.

---

## 2. Priorität 2 (P2): F-003 — Abgebrochener Konfigurations-Bootstrap blockiert Folgestarts

**Ziel:** Das Anlegen einer Standardkonfiguration bei fehlender `ainetreview.json` muss atomar und ausfallsicher sein. Tritt beim Schreiben ein Abbruch (`OperationCanceledException`) oder ein I/O-Fehler auf, darf keine unvollständige oder 0-Byte-Datei zurückbleiben.

- [ ] **2.1 Deterministischen Integrationstest für Bootstrap-Abbruch erstellen**
  - [ ] In `tests/AiNetReview.IntegrationTests/HostProcessIntegrationTests.cs` (oder neuer dedizierter Testklasse):
    - Abbruch während `CreateConfigFileAsync` simulieren (Token storniert nach Erstellung des Handles / vor Abschluss des Streams).
    - Prüfen, dass der Folgestart nicht mit `INVALID_INPUT: Configuration is not valid JSON.` scheitert, sondern die Konfiguration sauber neu erzeugt.

- [ ] **2.2 Atomaren Schreib- und Bereinigungs-Lebenszyklus implementieren**
  - [ ] `src/AiNetReview/Cli/ReviewCommand.cs:232-254` (`CreateConfigFileAsync`):
    - Entweder: Konfiguration zunächst in eine temporäre Datei im selben Verzeichnis schreiben und nach erfolgreichem Flush via atomarem Move/Replace an den Zielort verschieben.
    - Oder: Im `catch (Exception)` / `catch (OperationCanceledException)`-Block eine nachweislich exklusiv neu angelegte Datei deterministisch wieder entfernen (`File.Delete`), falls `streamCreated == true` und der Schreibvorgang nicht vollständig war.
  - [ ] Sicherstellen, dass konkurrierende Prozesse, die das Rennen gewinnen und eine valide Konfiguration schreiben, niemals überschrieben oder fälschlicherweise gelöscht werden (`FileMode.CreateNew`-Schutz erhalten).

- [ ] **2.3 Host- und Fehlerverhalten absichern**
  - [ ] Bei Abbruch bleibt der CLI-Exit-Code `130` / `CANCELLED` erhalten.
  - [ ] Valide Benutzerkonfigurationen werden unter keinen Umständen angetastet.
  - [ ] Vollständiger regulärer Bootstrap erzeugt weiterhin exakt die registrierten Standardwerte und das identische JSON-Format.

---

## 3. Priorität 2 (P2): F-002 — Redundante Teilfragment-Normalisierung optimieren

**Ziel:** `StructuralDuplicateDetector` soll zusammenhängende Duplikate effizient identifizieren, ohne für alle verschachtelten Sub-Intervalle vollständige Syntax-Traversierungen und Strings zu materialisieren, die anschließend ohnehin unterdrückt werden.

- [ ] **3.1 Reproduzierbare Last-/Performance-Charakterisierung aufsetzen**
  - [ ] Isolierten Test-Harness basierend auf `tasks/audit-20261003-2245/performance/Program.cs` bereitstellen.
  - [ ] Baseline für synthetische Methoden mit 100, 200 und 400 Statements dokumentieren (Allokationen und Laufzeit).

- [ ] **3.2 Normalisierungs- und Gruppierungspfad umstrukturieren**
  - [ ] `src/AiNetReview.Core/Analysis/StructuralDuplicateDetector.cs`:
    - `NormalizeFragment` (`:478-500`) und `CollectFromList` (`:238-302`): Wiederverwendbare Sub-Repräsentationen oder Streaming-Normalisierung nutzen, statt für jedes Teilintervall wiederholt von vorne vollständige Strings zu erzeugen.
    - `BuildGroups` (`:305-347`): Intervalle, die nachweislich echte Teilmengen größerer Übereinstimmungen ohne zusätzliche externe Vorkommen sind, frühzeitig verwerfen (Pruning vor Vollmaterialisierung).
  - [ ] Kompakte Hash-Fingerprints dürfen semantische Exaktheit unterstützen, aber nicht die exakte Token-/Strukturprüfung ersetzen.

- [ ] **3.3 Vollständige Erhaltung der semantischen Analyse-Ergebnisse absichern**
  - [ ] Alle Vorkommen (`Occurrences`) und exakten Koordinaten (start- und end-exklusiv) bleiben unverändert.
  - [ ] Exakte Operator-, Literal- und Typunterschiede werden weiterhin strikt beachtet.
  - [ ] Lokale Variablen- und Parameternamen-Normalisierung bleibt konsistent.
  - [ ] Gruppen mit echten zusätzlichen Vorkommen eines Teilfragments bleiben sichtbar (kein Over-Pruning).
  - [ ] Rollen- und projektübergreifende Gruppen werden weiterhin korrekt erfasst.

- [ ] **3.4 Performance- und Regressions-Nachweis erbringen**
  - [ ] Nachweisen, dass bei 100, 200 und 400 Statements identische Findings entstehen.
  - [ ] Dokumentieren, dass die kumulativen GC-Allokationen (zuvor ~37 GB bei n=400) und Laufzeiten drastisch sinken.
  - [ ] Test `tests/AiNetReview.FastTests/ReviewAnalyses/StructuralDuplicationCandidatesAnalysisTests.cs` (inkl. 100-Statement-Test) bleibt grün.

---

## 4. Priorität 2 (P2): F-004 — Release-Verifikation und CI-Gate härten

**Ziel:** Release-Builds müssen unabhängig von bestehenden Debug-Artefakten vollständig verifizierbar sein. Der GitHub-Release-Workflow muss vor der Veröffentlichung automatisierte Tests und Smoke-Prüfungen des eigenständigen Pakets erzwingen.

- [ ] **4.1 Host-Executable-Ermittlung in Integrationstests dynamisch gestalten**
  - [ ] `tests/AiNetReview.IntegrationTests/HostProcessIntegrationTests.cs:104-114`:
    - Hardcodierten Pfad `src/AiNetReview/bin/Debug/net10.0/AiNetReview.exe` ablösen.
    - Executable-Pfad abhängig von der aktuellen Build-Konfiguration auflösen (unterstützt `Debug` und `Release`), z. B. über Assembly-Konfiguration oder Fallback auf vorhandene Konfiguration.
  - [ ] Verifizieren, dass `dotnet test -c Release --filter FullyQualifiedName~ProcessInvocation_WithRepositoryConfigurationPublishesAnIgnoredTimestampedRun` auf einem sauberen Release-Stand ohne vorherigen Debug-Build erfolgreich durchläuft.

- [ ] **4.2 GitHub Release-Workflow (`release.yml`) absichern**
  - [ ] `.github/workflows/release.yml`:
    - Vor dem Schritt `Publish win-x64` Test-Jobs für FastTests und reguläre Integrationstests einbinden.
    - Smoke-Test auf das publizierte, selbständige `win-x64`-Binary (`AiNetReview.exe --help` sowie ein minimaler Trockenlauf) vor der Erstellung des GitHub-Releases ausführen.
  - [ ] Sicherstellen, dass ein fehlschlagender Test das Publishing und die Erstellung des Release-Assets abbricht.

- [ ] **4.3 Abgleich mit `scripts/release.ps1` und Dokumentation**
  - [ ] `scripts/release.ps1`: Prüfen, ob der lokale Verifikationsablauf mit den CI-Gates übereinstimmt.
  - [ ] `docs/development/build-and-tests.md`: Dokumentation bezüglich Debug- vs. Release-Testausführung aktualisieren.

---

## 5. Priorität 3 (P3): Nachgelagerte Klärungen & Härtungen

**Ziel:** Klärung und Absicherung der im Audit identifizierten Randhypothesen und offenen Fragestellungen, die keinen akuten Ausfall verursachen.

- [ ] **5.1 F-001-Variantengrenzen und SDK-Multi-Projekt-Verhalten analysieren**
  - [ ] Prüfen, ob neben `FastTests` weitere Projektkombinationen (z. B. abweichende FrameworkReferences oder TargetFrameworks) zu uneinheitlichen Symbol-Instanzen führen.

- [ ] **5.2 Konfigurations-Symlink als Pfadanker klären**
  - [ ] `ReviewConfigValidator.Load`: Verhalten bei symbolischen Links auf `ainetreview.json` definieren.
  - [ ] Klären, ob das CLI-Aufrufverzeichnis oder das aufgelöste Ziel der Konfigurationsdatei als Projektwurzel für relative Pfade gelten soll; gezielten Test ergänzen.

- [ ] **5.3 Related-Link-Dichte bei gemeinsamen Testpfaden prüfen**
  - [ ] `MissingTestEvidenceCandidatesAnalysis` / `ReviewFindingBuilder`:
    - Untersuchen, ob Kontextsymbole indirekter Testpfade (shortest path) gleichrangige Peer-Links zu bis zu 20 anderen Findings erzeugen sollen oder ob Subject- und Pfad-Beziehungen im Markdown-Bericht getrennt dargestellt werden sollten.

- [ ] **5.4 Test-Framework-Klassifikation gegen Metadaten-Lookalikes absichern**
  - [ ] `src/AiNetReview.Core/Analysis/TestFrameworkClassifier.cs:124-140`:
    - Prüfen, ob fremde Metadaten-Assemblies mit identischen Typnamen (z. B. gefälschte `Xunit.FactAttribute`) fälschlich als Testwurzeln akzeptiert werden.
    - Assembly-Identitätsprüfung analog zu `DeadCodeCandidatesAnalysis` nachrüsten.

- [ ] **5.5 Stacktiefe im Tarjan-Zyklus-Selector prüfen**
  - [ ] Rekursive Tiefensuche in `TypeDependencyGraphBuilder` evaluieren; prüfen, ob eine iterative Implementierung für pathologisch tiefe Typgraphen erforderlich ist.

---

## 6. Gesamtabnahme & Abschluss

- [ ] **6.1 Gesamte Testsuite verifizieren**
  - [ ] `dotnet build -c Debug` und `dotnet build -c Release` ohne Fehler und Warnungen.
  - [ ] Alle FastTests erfolgreich: `dotnet test tests/AiNetReview.FastTests`.
  - [ ] Alle IntegrationTests erfolgreich: `dotnet test tests/AiNetReview.IntegrationTests`.
  - [ ] Gezielter Release-Test erfolgreich: `dotnet test -c Release --filter FullyQualifiedName~HostProcessIntegrationTests`.

- [ ] **6.2 Vollständigen Review-Lauf (Self-Review) ausführen**
  - [ ] `AiNetReview.exe review .` ausführen.
  - [ ] Ergebnis mit Baseline `audit-reporting/20261003T204611Z-69b83ba6/` abgleichen.
  - [ ] Bestätigen: F-001 (CodeLineMetrics-Befund) ist behoben; keine unerwarteten neuen Findings; atomare Reports und Maps vollständig veröffentlicht.

- [ ] **6.3 Dokumentations- und Regel-Integrität prüfen**
  - [ ] Alle geänderten Verträge in `docs/` nachgeführt.
  - [ ] Arbeitsbaum sauber; keine ungetrackten temporären Artefakte im Repository.
