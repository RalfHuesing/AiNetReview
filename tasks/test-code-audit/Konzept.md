---
status: draft
---

# Testcode als regulärer Bestandteil des Audits

## Intention

Tests sind relevanter Code: Sie dokumentieren Verhalten, begründen das Vertrauen in Änderungen und müssen selbst verständlich und wartbar bleiben. Ein von einem Agenten geschriebener Test mit mehreren tausend Zeilen darf nicht allein aufgrund seines Testprojekts aus dem Audit verschwinden. AiNetReview soll Auffälligkeiten in Testcode ebenso sichtbar machen wie in Produktionscode und den auditierenden Agenten zur kontextbezogenen Untersuchung anleiten.

Das Ergebnis ist ein gemeinsamer Auditauftrag mit erkennbarer Herkunft der Findings und passender Interpretation für Tests. Die Analyse liefert überprüfbare Signale; sie behauptet weder Testqualität noch Fehlerhaftigkeit und fordert keine automatische Umgestaltung.

Dieses Dokument beschreibt einen Vorschlag für zukünftiges Verhalten. Es ändert keine Analyse und startet weder Roadmap noch Umsetzung.

## Verifizierter Ist-Stand

Geprüfte Ausgangsbasis: Commit `193a4f1d9d38cc6861975e4cf88f4e570395aba8`. Die folgenden Aussagen wurden an Dokumentation, Implementierung und vorhandenen Testfällen gelesen; für diese Konzeptarbeit wurden keine Tests ausgeführt.

### Wo Testprojekte als Analyseziel ausgeschlossen werden

| Analyse | Aktueller Ausschluss | Konsequenz für Testcode |
| --- | --- | --- |
| `code-size-candidates` | `CodeSizeCandidatesAnalysis.ExecuteAsync` überspringt erkannte Testprojekte vor allen drei Collectors. | Keine Größen-Findings für Methoden, Klassen oder Dateien. |
| `method-control-flow-outliers` | `MethodControlFlowOutliersAnalysis.ExecuteAsync` überspringt Testprojekte vor der projektweisen Messung. | Keine Findings zu auffälligen Entscheidungen oder Verschachtelung. |
| `duplicate-code-candidates` | `DuplicateCodeDetector.CollectAsync` filtert Testprojekte aus. | Keine Cluster innerhalb von Tests oder zwischen Tests und Produktionscode. |
| `structural-duplication-candidates` | `StructuralDuplicateDetector.ScanAsync` filtert Testprojekte aus. | Keine wiederholten Fragmente in Tests oder zwischen beiden Bereichen. |
| `indirection-drift-candidates` | `TransparentForwardingClassifier.ClassifyProjectAsync` liefert für Testprojekte eine leere Sammlung. | Keine Weiterleitungsketten in Testinfrastruktur. |
| `dead-code-candidates` | `DeadCodeCandidatesAnalysis.ExecuteAsync` wählt nur Produktionsprojekte als Kandidatenquelle. | Keine unreferenzierten Typen oder gewöhnlichen Methoden aus Testprojekten. |
| `non-ascii-identifiers` | `NonAsciiIdentifiersAnalysis.ExecuteAsync` überspringt Testprojekte. | Keine Findings zu Testbezeichnern. |
| `missing-test-evidence-candidates` | `MissingTestEvidenceCandidateSelector.SelectAsync` wählt nur Produktionsfunktionen. | Testcode ist Belegquelle, aber kein Ziel der Frage nach einem statischen Testpfad. |

Damit schließen sieben Analysen Testcode als eigenständiges Wartbarkeitsziel aus. Die achte hat eine andere fachliche Zielrichtung; diese Unterscheidung muss erhalten bleiben.

Primäre Implementierungsbelege:

- [CodeSizeCandidatesAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/CodeSizeCandidates/CodeSizeCandidatesAnalysis.cs)
- [MethodControlFlowOutliersAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/MethodControlFlowOutliers/MethodControlFlowOutliersAnalysis.cs)
- [DuplicateCodeDetector](../../src/AiNetReview.Core/Analysis/DuplicateCodeDetector.cs)
- [StructuralDuplicateDetector](../../src/AiNetReview.Core/Analysis/StructuralDuplicateDetector.cs)
- [TransparentForwardingClassifier](../../src/AiNetReview.Core/ReviewAnalyses/IndirectionDriftCandidates/TransparentForwardingClassifier.cs)
- [DeadCodeCandidatesAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/DeadCodeCandidates/DeadCodeCandidatesAnalysis.cs)
- [NonAsciiIdentifiersAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/NonAsciiIdentifiers/NonAsciiIdentifiersAnalysis.cs)
- [MissingTestEvidenceCandidateSelector](../../src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceCandidateSelector.cs)

### Wo Tests bereits berücksichtigt werden

- `SolutionLoader` lädt und prüft auch C#-Testprojekte. Sie werden nicht generell aus der Lösung entfernt. Die Sonderbehandlung entfernt nur generierte Testprojekt-Dokumente außerhalb der erlaubten Projektwurzel vor der Pfadprüfung.
- `SourceSnapshotLoader` erfasst nichtgenerierte C#-Dokumente aller geladenen C#-Projekte. Testdateien gehören damit bereits zur Baseline.
- `SolutionReferenceIndex` erfasst Referenzen aus Produktions-, Test- und generiertem C#-Code. Ein Aufruf aus einem Test kann einen Produktionssymbol-Kandidaten bei `dead-code-candidates` verhindern.
- `DeadCodeIndirectUsageIndex` durchsucht alle C#-Projekte sowie generierte Dokumente nach den vorhandenen indirekten Bindungen. Sein fester Framework-Attributschutz betrifft MVC-Einstiegspunkte, nicht einen vollständigen Testframework-Lebenszyklus.
- `MissingTestEvidenceSemanticGraphBuilder` verarbeitet Funktionen aus Produktions- und Testprojekten und verwendet erkannte aktive Testmethoden als Wurzeln. `TestFrameworkClassifier` unterscheidet aktive Testwurzeln von übersprungenen oder expliziten Tests. Das ist keine vollständige Runner-Modellierung von Setup, Teardown, Fixtures und Datenlieferanten.
- Die `changed-files`-Auswahl für `missing-test-evidence-candidates` berücksichtigt Änderungen am gesamten C#-Snapshot, einschließlich Testdateien. Eine Teständerung kann daher Findings für unveränderten Produktionscode sichtbar machen.
- Findings-Validierung und ReportWriter haben keinen generellen Testprojekt-Ausschluss. Die Findings fehlen bereits in den Analysen. Die vorhandenen Finding-/ReviewFinding-Modelle besitzen allerdings keine explizite Herkunftsklassifikation für die Darstellung.

Belege: [SolutionLoader](../../src/AiNetReview.Core/Analysis/SolutionLoader.cs), [SourceSnapshotLoader](../../src/AiNetReview.Core/Analysis/SourceSnapshotLoader.cs), [SolutionReferenceIndex](../../src/AiNetReview.Core/Analysis/SolutionReferenceIndex.cs), [DeadCodeIndirectUsageIndex](../../src/AiNetReview.Core/ReviewAnalyses/DeadCodeCandidates/DeadCodeIndirectUsageIndex.cs), [TestFrameworkClassifier](../../src/AiNetReview.Core/Analysis/TestFrameworkClassifier.cs), [Testpfadgraph](../../src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceSemanticGraphBuilder.cs), [ReviewFindingBuilder](../../src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs), [MarkdownReportWriter](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs).

### Der Ausschluss ist auch dokumentiert und abgesichert

[Adding a review analysis](../../docs/development/adding-review-analyses.md) fordert derzeit ausdrücklich, Testprojekte über den gemeinsamen Classifier zu überspringen. Das würde die Lücke auch in neue Analysen übertragen. [Current findings](../../docs/review/findings.md) dokumentiert die jeweiligen Produktionscode-Grenzen. Mehrere vorhandene Tests sichern den Ausschluss bewusst ab, beispielsweise `ExecuteAsync_ExcludesTestProjectsGeneratedDocumentsAndGeneratedSymbols` für Größen-Findings und `ExecuteAsync_ExcludesTestProjectsFromProductionComparisons` für Kontrollfluss.

Diese bestehenden Erwartungen müssen bei einer späteren Umsetzung fachlich ersetzt werden. Der Schutz tatsächlich generierten Codes muss dabei erhalten bleiben.

### Erkennung und Grenzen

[ReviewSourceClassifier](../../src/AiNetReview.Core/Analysis/ReviewSourceClassifier.cs) erkennt Testprojekte über bekannte Assembly-Referenzen, Namens-/Dateisuffixe wie `Tests`, `FastTests`, `TestKit` oder `Specs` und exakte Pfadsegmente wie `tests`. Die Klassifikation ist projektweise, nicht eine Erkennung der Aufgabe jeder einzelnen Methode. Sie berücksichtigt derzeit nicht direkt die MSBuild-Eigenschaft `IsTestProject`.

Ein unbekanntes Testprojekt ohne diese Merkmale gilt als Produktionsprojekt. Umgekehrt kann ein Projekt mit gemischten Verantwortlichkeiten als Testprojekt eingeordnet werden. Nach diesem Vorhaben darf eine falsche Einordnung die sieben Wartbarkeitsanalysen nicht mehr vollständig verhindern; sie kann weiterhin die Kennzeichnung und die spezielle Testpfadsemantik beeinflussen. Die Reportbezeichnung muss deshalb die erkannte Projektrolle ausdrücken und darf keine garantierte Testentdeckung versprechen.

„Von einem Agenten geschrieben“ bedeutet nicht automatisch „generierter Code“ im bestehenden Classifier. Ausgeschlossen werden definierte Dateimarker, Header und Attribute. Ein großes Rohstring-Testfixture bleibt eine gewöhnliche C#-Quelldatei; der im String enthaltene Code ist aber kein ausführbarer C#-Member dieser Lösung.

## Empfohlenes Zielbild

### Analysepolitik

| Analyse | Zielverhalten für Testcode | Interpretation im Audit |
| --- | --- | --- |
| Größe | Ausführbare Member, Klassen und Dateien in beiden Projektrollen messen. Gleiche Optionen und unabhängige Extremgrenzen. | Szenarien, Assertions, Testdaten und Setup nachvollziehen; Größe allein verlangt kein Aufteilen. |
| Kontrollfluss | Auch Methoden in Testprojekten messen; Perzentile weiterhin pro Projekt bilden. | Entscheidungen können Verifikationspfade verschleiern oder komplizierte Vorbereitung begründen. Nachprüfen. |
| Ähnliche Methoden | Test-Test- und Test-Produktionscode-Paare in den vorhandenen Vergleich einbeziehen. | Wiederholung kann Szenarien lesbar halten. Kopierte Produktionslogik kann ein abhängiges Erwartungsergebnis erzeugen. Beides verlangt Kontext. |
| Identische Fragmente | Fragmentgruppen über beide Projektrollen mit den bestehenden Grenzen bilden. | Gemeinsames Setup, unabhängige Erwartungsbildung und notwendige explizite Assertions unterscheiden. |
| Weiterleitungsketten | Auch innerhalb von Testprojekten nach den vorhandenen projektinternen Kriterien suchen. | Jede Fixture-/Helper-Schicht auf ihren Beitrag zu Isolation, Lebenszyklus und verständlichen Szenarien prüfen. |
| Ungenutzter Code | Testtypen und gewöhnliche Methoden als Kandidaten aufnehmen; Framework-Einstiegspunkte und Bindungsunsicherheit berücksichtigen. | Veraltete Helpers können relevant sein. Fehlende direkte Aufrufer beweisen nicht, dass ein Test oder Hook ungenutzt ist. |
| Bezeichner | Den vorhandenen ASCII-Signalvertrag auf beide Projektrollen anwenden. | Testnamen können bewusst fachliche Sprache verwenden. Repository-Konventionen entscheiden über die Bewertung. |
| Fehlende Testpfade | Weiterhin Produktionsfunktionen als Ziel und Testcode als Belegquelle behandeln. | Keine rekursive Forderung, jede Testmethode müsse selbst einen Unit-Test besitzen. |

Testcode schließt das gesamte nichtgenerierte C# in erkannten Testprojekten ein: Tests, Fixtures, Setup, Teardown, Datenlieferanten, Fakes, Mocks, Builders und Helpers. Die Wartbarkeitsanalysen hängen nicht von einem erkannten Testattribut oder einem aktiven Teststatus ab. Auch übersprungene Tests bleiben Analyseziele.

Es entstehen keine neuen Analyse-IDs nur für Tests. Die sieben bestehenden Analysen erweitern ihren Scope bei vorhandener Aktivierung automatisch; Tests brauchen kein neues Opt-in. Das bestehende `enabled: false` einer Analyse bleibt wirksam. Die geänderte Semantik wird über die vorhandene `BehaviorVersion` kenntlich gemacht.

### Messwerte und Vergleichsgruppen

Die vorhandenen Messdefinitionen und Schwellen bleiben bestehen. Produktionscode und Tests werden nicht zu einer lösungsweiten Perzentilpopulation zusammengelegt; die bereits projektweisen Populationen bleiben projektweise. Eine Testrolle erzeugt weder höhere Freigrenzen noch einen pauschalen Rabatt. Neue, speziell kalibrierte Testschwellen gehören nicht zu diesem Vorhaben.

Für `code-size-candidates` gelten weiterhin die Standardwerte 300 Member-Codezeilen, 800 Typ-Codezeilen und 1.000 physische Dateizeilen als unabhängige Extremgrenzen. Member-/Typ-Codezeilen zählen Token-Anfänge und sind nicht mit physischen Zeilen gleichzusetzen. Eine Methode mit 1.000 ausführbaren Codezeilen wird deshalb unabhängig von ihrem Kontrollfluss gemeldet; 1.000 Zeilen innerhalb eines Rohstrings garantieren kein Member-Finding. Eine nichtgenerierte Testdatei mit mehr als 3.000 physischen Zeilen muss bei Standardoptionen als Datei-Finding erscheinen.

Beide Duplikationsanalysen dürfen ihre vorhandenen Vergleichsalgorithmen auf beide Projektrollen anwenden. Gruppen werden nicht nachträglich aufgeteilt, begrenzt oder nach Rolle unterdrückt. Ein gemischter Cluster ist ein einzelnes Finding mit allen Vorkommen. Ein Zusammenhang über Cluster-Kanten behauptet weiterhin weder paarweise Identität aller Methoden noch semantische Gleichheit.

Weiterleitungsketten bleiben auf ein Projekt beschränkt. Dieses Vorhaben erweitert ihre Kandidatenquelle, nicht die Graphdefinition über Projektgrenzen hinweg.

### Ungenutzter Testcode braucht einen eigenen Schutzvertrag

Nur den Testfilter zu entfernen wäre unzureichend. Der vorhandene aktive Testwurzel-Classifier ist für Testpfade gedacht. Bei ungenutztem Code müssen auch übersprungene Tests geschützt bleiben: Deaktivierung ist kein Beweis, dass eine deklarierte Testmethode gelöscht werden kann.

Für die bereits im Testpfad-Classifier unterstützten Frameworkfamilien xUnit, NUnit und MSTest gilt der folgende begrenzte Schutzkatalog. Er beschreibt Schutz vor einem Unused-Finding, keine Zusicherung, dass der Runner jede markierte Deklaration tatsächlich ausführt.

| Familie | Als indirekter Einstiegspunkt oder Bindung zu schützen |
| --- | --- |
| xUnit v2/v3 | `Fact`-/`Theory`-Methoden einschließlich abgeleiteter Attribute und v3-`IFactAttribute`-Implementierungen, unabhängig von Skip/Explicit. Zugehörige Testtypen; Fixture-Typen über `IClassFixture<T>`, `ICollectionFixture<T>`, Collection-Definitionen/-Zuordnungen und v3-Assembly-Fixture-Attribute. Lifecycle-Verträge `IAsyncLifetime`, `IDisposable` und v3-`IAsyncDisposable`. Statisch benannte `MemberData`-Quellen einschließlich angegebenem MemberType und geerbter Member; über `ClassData` angegebene Provider-Typen und ihre Vertragsimplementierungen. |
| NUnit | Methoden mit `Test`, `TestCase`, `TestCaseSource` oder `Theory` einschließlich abgeleiteter Frameworkattribute; zugehörige Fixture-Typen. `TestFixture`, `TestFixtureSource` und `SetUpFixture`; Hooks mit `SetUp`, `TearDown`, `OneTimeSetUp`, `OneTimeTearDown` sowie vorhandenen Legacy-Synonymen. Benannte Quellen von `TestCaseSource`, `TestFixtureSource` und `ValueSource`, auch in angegebenen Quelltypen; Provider-Typen bei Type-basierten Quellen. Für `Theory` deklarierte `Datapoint`-/`DatapointSource`-Member erhalten denselben Bindungsschutz. Ignore/Explicit entfernen diesen Schutz nicht. |
| MSTest | Methoden mit `TestMethod`/`DataTestMethod` einschließlich abgeleiteter Frameworkattribute und zugehöriger `TestClass`-Typen. Hooks mit `AssemblyInitialize`/`AssemblyCleanup`, `ClassInitialize`/`ClassCleanup`, `TestInitialize`/`TestCleanup`, `GlobalTestInitialize`/`GlobalTestCleanup`. Statisch benannte `DynamicData`-Quellen einschließlich angegebenem Quelltyp und benannten DisplayName-Callbacks; Typbindungen über vorhandene `AssemblyFixtureProvider`-Attribute. Dispose-Vertragsimplementierungen bleiben geschützt. Ignore und bedingte Deaktivierung entfernen diesen Schutz nicht. |

Die Frameworkmechanismen wurden gegen die primären Dokumentationen geprüft: [xUnit Shared Context](https://xunit.net/docs/shared-context), [MemberData](https://api.xunit.net/v3/1.1.0/Xunit.MemberDataAttribute.html), [ClassData](https://api.xunit.net/v3/1.1.0/Xunit.ClassDataAttribute.html), [NUnit Attribute](https://docs.nunit.org/articles/nunit/writing-tests/attributes.html), [TestCaseSource](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html), [TestFixtureSource](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testfixturesource.html), [ValueSource](https://docs.nunit.org/articles/nunit/writing-tests/attributes/valuesource.html), [MSTest Lifecycle](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-lifecycle) und [DynamicData](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-data-driven). Schutzmarker gelten, wenn die entsprechende Metadatenidentität im geladenen Projekt vorhanden ist; AiNetReview verlangt kein Paketupgrade im Zielprojekt.

Abgeleitete Frameworkattribute und geerbte Hooks dürfen nicht durch bloßen Namensvergleich verlorengehen. Anerkennung setzt eine semantisch belegte Framework-Basis, einen Framework-Vertrag oder ein bekanntes Frameworkattribut aus Metadaten voraus. Gleichnamige lokale Lookalike-Attribute sind kein Frameworkbeleg. Test-/Hook-Schutz schützt den zugehörigen Typ und erforderliche umschließende Typen vor einem gruppierten Type-Finding. Constructors sind weiterhin keine eigenständigen Dead-Code-Kandidaten; die Typschutzfrage bleibt trotzdem relevant.

Ein Testattribut schützt nicht pauschal jede gewöhnliche Methode derselben Klasse. Ein tatsächlich unreferenzierter privater Helper neben einem gültigen Test bleibt Kandidat. Semantische Referenzen, bestehende indirekte Bindungen, Interface-/Override-Verträge und Unsicherheitsschutz gelten weiterhin. Ein über String oder `nameof` aufgelöster Provider-Member schützt den Member und seinen Typ; Field-/Property-Provider erzeugen keine neuen Dead-Code-Kandidatenarten, aber ihre Typbindung und gewöhnliche Methoden-Vertragsimplementierungen müssen berücksichtigt werden.

Für mehrdeutige benannte Quellen werden alle plausiblen Member des bekannten Quelltyps als unsicher behandelt. Ist der Quelltyp bekannt, aber der Name statisch unbekannt, betrifft Unsicherheit die plausiblen Provider-Member dieses Typs einschließlich geerbter Quellen. Unbeteiligte Helpers und andere Projekte bleiben Kandidaten. Ist bei einem erkannten Datenattribut der Quelltyp überhaupt nicht eingrenzbar, werden Dead-Code-Kandidaten im referenziell erreichbaren C#-Projektbereich konservativ als unsicher behandelt; dieser breite Ausschluss muss in der Scope-Übersicht mit betroffenem Projektbereich und Bindungsgrund sichtbar sein. Ein nicht unterstütztes Custom-Runner-/Datenattribut wird nicht als erfolgreich aufgelöste Bindung ausgewiesen; der Report erklärt die begrenzte Frameworkunterstützung und verlangt bei Dead-Code-Findings weiterhin die manuelle Prüfung indirekter Verwendung.

Die bestehende `apiSurface`-Option gilt weiterhin für beide Projektrollen. `external_library` schützt öffentlich sichtbare APIs auch in Testhilfsbibliotheken; `closed_solution` bezieht sie ein. Der Report muss diese Grenze erklären. Es gibt keinen stillen rollenabhängigen Wechsel zu `closed_solution`. Testprojektstatus beweist nicht, dass eine öffentlich angebotene Testhilfsbibliothek keine externen Verbraucher hat.

Dieser Schutz wird für die Dead-Code-Analyse verwendet. Er ersetzt nicht die bestehende Definition einer aktiven Testwurzel für `missing-test-evidence-candidates` und behauptet keine neue Testpfad-Kante.

## Reporting für einen auditierenden Agenten

### Empfehlung: getrennte Arbeitsbereiche innerhalb eines gemeinsamen Reportlaufs

Für gesonderte agentische Audit-Schritte erhält jeder Reportlauf eine zusätzliche Ebene für die Herkunft seiner Findings. Die empfohlenen Namen sind `production`, `tests` und `mixed`. Ausgeschriebene Namen machen den Auftrag deutlicher als `prod`/`test`; `mixed` bezeichnet ausschließlich Findings, die Produktions- und Testcode als Analysegegenstand zusammen enthalten, nicht allgemein wiederverwendbare Helpers.

```text
<output-directory>/
  baseline.json
  <run-id>/
    index.md
    production/
      changed-files/index.md
      all-findings/index.md
    tests/
      changed-files/index.md
      all-findings/index.md
    mixed/
      changed-files/index.md
      all-findings/index.md
```

Nichtleere Analyseberichte liegen neben dem jeweiligen View-Index, beispielsweise `tests/changed-files/code-size-candidates.md`. Alle sechs View-Indizes werden erzeugt, auch wenn ihr Bereich leer ist. Sie benennen dann den leeren Arbeitsumfang, ohne künstliche Findings oder leere Analyseberichte anzulegen. Der Root-Index unterscheidet weiterhin einen Lauf ohne aktive Analysen von einem ausgeführten Lauf ohne Findings.

Diese Verzeichnisse liegen innerhalb des bestehenden unveränderlichen Run-Verzeichnisses. Es entstehen weder mehrere Analyseausführungen noch getrennte Snapshots, Baselines oder Publikationen. Der CLI-Verweis bleibt der gemeinsame `<run-id>/index.md`; alle Bereiche werden gemeinsam atomar veröffentlicht. Bestehende veröffentlichte Läufe bleiben unverändert. Neue Läufe erzeugen die bisher direkt unter `<run-id>/` liegenden beiden Views nicht zusätzlich als doppelte Berichtskopien.

Innerhalb eines Bereichs bleibt die Navigation nach Analyse, Projekt und Datei mit deterministischer Sortierung erhalten. Jedes Finding erscheint pro View genau einmal, entweder unter `production`, `tests` oder `mixed`. Die Gesamtsumme und die drei Bereichssummen müssen je View übereinstimmen; `all-findings` und `changed-files` werden nicht miteinander addiert. Globale Projekt-/Dateizahlen bleiben die eindeutigen repräsentativen Projekt-/Dateipaare gemäß bestehendem Zählvertrag; Bereichszahlen dürfen nicht unbesehen addiert werden. Die Zusammenfassung weist diese Bedeutung aus.

Die Herkunft wird aus der zentralen Klassifikation der geladenen Projekte bereitgestellt, nicht aus einer erneuten Namensinterpretation im ReportWriter. Projektüberschriften kennzeichnen ihre erkannte Rolle. Bei Gruppen erhält jedes beteiligte Vorkommen seine Projektrolle. Die Zuordnung nach `mixed` wird aus den tatsächlich repräsentierten Symbolen/Vorkommen bestimmt, nicht aus beliebigen Belegdateien: Ein Produktions-Finding zu einem fehlenden Testpfad bleibt unter `production`, auch wenn ein Testpfad als Evidenz enthalten ist. Ein Test-Test-Duplikationscluster gehört nach `tests`; ein Cluster mit Test- und Produktionsmitgliedern gehört vollständig und einmal nach `mixed`. Seine repräsentative Datei bestimmt nicht allein die Zuordnung.

Der Root-Index ist der Einstieg in den Auditauftrag. Er zeigt die drei Arbeitsbereiche mit Findingzahlen getrennt für beide Views und verlinkt zuerst die drei `changed-files`-Indizes als gemeinsamen normalen Arbeitsumfang. Jeder Bereichs-/Analysebericht verlinkt zur gemeinsamen Review-Anleitung. Seine `all-findings`-Policy erfordert weiterhin einen ausdrücklich beauftragten Voll-Audit. Verweise und Related-Angaben funktionieren über Bereichsgrenzen hinweg; „in dieser View sichtbar“ bezieht sich auf alle drei Bereiche derselben View. Ein Related-Finding in einem anderen Bereich gehört damit nicht automatisch zu `all-findings`.

Test-Findings sind gleichwertiger Bestandteil eines normalen unbeschränkten Audits. Der Root-Index darf nicht nur einen Produktionsauftrag vermitteln und die übrigen Bereiche als Zusatzprüfung erscheinen lassen.

### Getrennte Audit-Schritte und gemeinsame Zusammenhänge

Die Struktur unterstützt unterschiedliche beauftragte Arbeitspakete: Produktionscode auf Verhalten und Verantwortlichkeiten prüfen, Testcode auf die Verständlichkeit der Szenarien und Aussagekraft der Verifikation prüfen und gemischte Findings im Zusammenhang beurteilen. Sie schreibt keine zwingende Reihenfolge vor. Ein übergreifendes Finding wird einmal als Ganzes untersucht; sein Ergebnis muss in eine gemeinsame Abschlussbewertung eingehen.

Ein Agent darf für jeden Auftrag relevante Quellen aus allen Bereichen als Kontext lesen. „Nur Test-Findings bearbeiten“ bedeutet deshalb nicht, dass er den getesteten Produktionscode ignorieren muss. Ebenso kann ein Produktionsaudit Testquellen benötigen, ohne damit einen vollständigen Audit aller Test-Findings zu starten. Der beauftragte Findingumfang und der zum Verstehen nötige Quellkontext sind unterschiedliche Grenzen.

Wenn der Nutzer ausdrücklich nur einen Bereich beauftragt, dokumentiert der Agent diese Beschränkung. Er erklärt einen solchen Teilauftrag nicht als vollständigen Audit der Lösung. Ein unbeschränkter Auftrag umfasst dagegen alle drei Bereiche der gewählten View. Fehlende oder ausgelassene Bereichsbearbeitung darf nicht als abgeschlossen gelten. `mixed` ist kein nachrangiger Restordner: Seine Einbeziehung muss beim normalen Audit sichtbar sein; bei einem auf `production` oder `tests` beschränkten Auftrag ist es als nicht bearbeiteter eigener Umfang auszuweisen.

Für die spätere Umsetzung ist das eine Reporting- und Review-Anleitungsanforderung. Neue Agentenrollen, Workflowdateien, automatische Delegation, Bearbeitungsstatus-Dateien oder Scheduler werden in diesem Vorhaben nicht eingeführt.

Eine knappe Scope-Übersicht im Root-Index nennt die geladenen C#-Projekte, ihre erkannte Rolle mit Klassifikationsgrund sowie die aktivierten Analysen und ihre Anwendbarkeit auf Testcode. Sie ist auch ohne Findings verfügbar. So kann ein Agent „Testcode einbezogen, kein Signal“ von „Analyse deaktiviert“ oder „Analyse fachlich nur für Produktionscode“ unterscheiden. Diese Übersicht behauptet keine vollständige Statement-, Runner- oder Laufzeitabdeckung; vorhandene Beschränkungen der Kandidatenarten und generierten Quellen bleiben ausdrücklich sichtbar.

Die Baseline-Semantik gilt gleichberechtigt für beide Projektrollen. Veränderungen irgendeines beteiligten Testvorkommens wählen auch einen gemischten Duplikationscluster aus. Partial-Type-Evidenz und Related-Verweise bleiben vollständig. Tests erhöhen nicht automatisch den Auftrag auf alle Findings: Die vorhandene Grenze zwischen normalem `changed-files`-Audit und ausdrücklich beauftragtem Voll-Audit bleibt bestehen. Bei `missing-test-evidence-candidates` bleibt die vorhandene Snapshot-Auswahl erhalten.

Bereits vorhandene Baselines enthalten Testdateien. Nach der Scope-Erweiterung können Findings aus unverändertem Testcode daher zunächst nur in `all-findings` stehen. Der Root-Index erklärt allgemein: `changed-files` bezieht sich auf Dateiänderungen, nicht auf neue Analysefähigkeiten. Ein vollständiger erster Audit nach einer Scope-Erweiterung braucht einen ausdrücklichen Auftrag; Findings werden nicht als Dateiänderungen ausgegeben.

### Nutzen, Kosten und Alternative

Die zusätzliche Verzeichnisebene macht begrenzte Agentenaufträge konkret referenzierbar, hält Einzelberichte kleiner und verhindert, dass viele Test-Findings Produktions-Findings in denselben Dateien überlagern. Unterschiedliche Review-Fragen lassen sich pro Bereich deutlicher formulieren. Die Ebene Herkunft vor der Ebene View unterstützt besonders die vorgeschlagene Bearbeitung nach Codebereich; die Root-Anleitung muss dafür die normale `changed-files`-Grenze besonders klar vermitteln.

Die Kosten sind sechs statt zwei View-Indizes, neue Berichtspfade und zusätzliche Navigation. Integrationsprüfungen, Dokumentation und direkte Pfadannahmen müssen angepasst werden. Ein Agent kann durch die Trennung Zusammenhänge übersehen oder zu früh Abschluss melden; dagegen helfen der gemeinsame Einstieg, vollständige gemischte Findings und bereichsübergreifende Related-Verweise. Produktions- und Testbestand isoliert zu analysieren würde dagegen Testpfade, Referenzen und gemischte Duplikate verlieren und gehört nicht zum Ergebnis.

Die Alternative wären gemeinsame Analyseberichte mit drei Abschnitten innerhalb der bisherigen Views. Das hält Navigation und Pfade einfacher, unterstützt aber separate Arbeitspakete weniger deutlich und kann deutlich größere Berichte erzeugen. Für die vom Nutzer erwogenen gesonderten Audit-Schritte empfiehlt dieser Draft die Verzeichnisvariante. Sie ist noch kein freigegebener Vertrag.

### Was ein Agent erwarten soll

| Situation | Gute Unterstützung durch den Report | Schlechte Konsequenz, die die Anleitung vermeiden muss |
| --- | --- | --- |
| Sehr langer Test | Messwert, Auswahlgrund, genaue Position und verwandte Signale; Szenarien, Vorbereitung und erwartetes Verhalten untersuchen. | Helpers extrahieren, bis eine Zeilengrenze unterschritten ist, obwohl mehr Kontextsprünge entstehen. |
| Große Fixture-Datei | Physische Dateigröße von ausführbarem Memberumfang unterscheiden; Testdaten und zu prüfenden Code auseinanderhalten. | Einen langen Rohstring als komplexe ausgeführte Methode behandeln oder Testdaten mechanisch zerstückeln. |
| Ähnliche Tests | Alle Fälle und relevanten Unterschiede lesen; Unabhängigkeit und Lesbarkeit gegen gemeinsamen Setup-Aufwand abwägen. | Szenarien zusammenführen und aussagekräftige Fehlerlokalisierung verlieren. |
| Gleiche Logik in Test und Produkt | Prüfen, ob das Erwartungsergebnis unabhängig vom geprüften Algorithmus entsteht. | Produkt und Test auf denselben Algorithmus umstellen und dadurch die Aussagekraft des Tests schwächen. |
| Komplexe Testlogik | Prüfen, ob Schleifen/Bedingungen Fälle verdecken, Assertions umgehen oder Fehler schwer zuordnen lassen. | Aus der Zahl der Entscheidungen unmittelbar ableiten, dass der Test falsch ist. |
| Helper-Kette | Zustandsbesitz, Isolation und Lebenszyklus verstehen; die Verantwortung jeder Schicht prüfen. | Fixtures und Builders pauschal als unnötige Indirektion entfernen. |
| Fehlende direkte Referenz | Frameworkentdeckung, Hooks, Datenquellen und externe Verwendung prüfen. | Testmethoden oder deaktivierte Tests wegen fehlender direkter Aufrufer löschen. |
| Kein Test-Finding | Scope und Kandidatengrenzen erkennen. | Daraus gute Assertions, ausreichende Abdeckung oder korrekte Testentdeckung ableiten. |

Die Review-Anleitung fragt bei Test-Findings nach überprüftem Verhalten, aussagekräftigen Assertions, unabhängigen Erwartungen, Isolation, verständlicher Fehlerlokalisierung und der Rolle der Testinfrastruktur. Diese Fragen sind manuelle Prüfperspektiven zu vorhandenen Signalen, keine neuen automatischen Analysen oder Qualitätsbehauptungen.

Auch im Testbereich gelten die bestehenden begründeten Klassifikationen: false positive, acceptable design, needs clarification oder actionable. Ein korrekt gemessener großer Test kann akzeptables Design sein. Pauschales Verwerfen als „nur Testcode“ ist keine Begründung; pauschales Aufteilen ist ebenfalls keine.

## Scope

### Muss

- Die sieben Wartbarkeitsanalysen beziehen nichtgenerierten Code beider Projektrollen bei bestehender Aktivierung ein, einschließlich Testinfrastruktur und deaktivierter Tests.
- `missing-test-evidence-candidates` behält Produktionsfunktionen als Ziel und Testcode als Belegquelle; vorhandene Pfad- und Unsicherheitsgrenzen bleiben ehrlich beschrieben.
- Die zentrale Klassifikation wird als Herkunft genutzt und transparent erklärt. Sie darf für die sieben Analysen kein pauschaler Ausschluss mehr sein.
- Der definierte Dead-Code-Schutzkatalog für Testframework-Einstiegspunkte, Lebenszyklus und Datenbindungen einschließlich lokaler/breiter Unsicherheit wird automatisiert abgesichert.
- Reports trennen `production`, `tests` und `mixed` innerhalb eines gemeinsamen Reportlaufs mit je beiden Views. Der Root-Index macht alle drei Bereiche zum regulären Auditumfang; Gruppen erscheinen je View vollständig und einmal. Baseline, Related-Verträge, Sortierung, Zählsemantik und atomare Publikation bleiben erhalten.
- Die Rolle von AI-geschriebenem gewöhnlichem Code wird von tatsächlich generierten Artefakten unterschieden; bestehende Generated-/Pfadgrenzen bleiben erhalten.
- Die spätere Umsetzung aktualisiert die betroffenen aktuellen Dokumente, Entwickleranleitung, Analysedeskriptoren und Reporttexte und ersetzt bestehende Ausschlusstests durch passende Einschluss-/Schutztests. Implementierte Aussagen werden erst dann in `docs/` geändert.

### Nicht

- Neue automatische Analysen für Assertions, Flakiness, Sleeps, Mocks, Coverage, Mutation Testing oder Testqualität.
- Laufzeitausführung, Runner-Entdeckung oder das Versprechen, alle denkbaren Frameworkbindungen aufzulösen.
- Separate Test-Baseline, separater CLI-Befehl, Test-Opt-in, rollenbasierte Unterdrückung oder begrenzte Findingzahl.
- Getrennte Analyseausführungen für Produktions- und Testbestand, doppelte flache Berichtskopien, Änderung alter Reportläufe oder Einführung neuer Agentenworkflows/-rollen.
- Neue Testschwellen, lösungsweite Perzentile oder Änderung bestehender Größen-/Kontrollflussdefinitionen.
- Neue Kandidatenarten nur für Tests, etwa zusätzliche Lambda-/Local-Function-Größenmessung; die bisherigen unterstützten Deklarationsarten bleiben maßgeblich.
- Projektexterne/unbeladene Tests analysieren oder Projektrollen durch ein neues konfigurierbares Klassifikationssystem ersetzen. Gemischte Projekte werden als Klassifikationsgrenze erklärt.
- Testframework-Lebenszyklus im vorhandenen Testpfadgraphen neu modellieren oder „Tests für jeden Test“ verlangen.
- Generierte Quellen als Wartbarkeitsziele aufnehmen, Projektgrenzen lockern oder String-Fixtures als weitere C#-Projekte kompilieren.
- Automatische Refactorings, Löschen von Tests oder Buildfehler aufgrund von Review-Signalen.
- Roadmap, Produktionscode oder Start des nächsten Workflowschritts während dieser Konzeptarbeit.

## Verifikation des späteren Ergebnisses

Die Abnahme prüft beobachtbares Analyse- und Reportverhalten; vorhandene Tests allein sind kein Beleg für den neuen Scope.

1. **Konkreter Ausgangsfall:** Bei Standardoptionen und aktiver Größenanalyse erscheinen eine nichtgenerierte Testdatei mit mehr als 3.000 physischen Zeilen und eine Testmethode mit mehr als 1.000 Token-Anfangszeilen als die passenden Datei-/Member-Findings. Ein großer Rohstring erzeugt den nachvollziehbaren Datei-Befund, ohne falsche Membergrößenbehauptung.
2. **Alle sieben Analysen:** Kleine deterministische Lösungen mit jeweils qualifizierendem Code in Testprojekten liefern die erwarteten Findings, auch für Helpers und deaktivierte Tests. Ohne qualifizierendes Signal entsteht kein künstliches Test-Finding.
3. **Statistik:** Eine identische Änderung an einer Testprojektpopulation verändert nicht die Perzentile eines Produktionsprojekts. Bestehende Grenzwerte, inklusive Gleichstände und unabhängige Größengrenzen, bleiben wirksam.
4. **Duplikate:** Test-Test-, Produktions-Produktions- und gemischte Gruppen werden vollständig und einmal berichtet. Unterschiede gemäß vorhandener Syntax-/Tokenverträge bleiben bestehen; eine Änderung nur am Testvorkommen wählt die ganze Gruppe im normalen Arbeitsumfang aus.
5. **Dead Code:** Der definierte Schutzkatalog wird für alle drei Frameworkfamilien einschließlich abgeleiteter Attribute, geerbter Hooks, deaktivierter Tests, Fixture-/Provider-Typen und String-/Type-Datenbindungen geprüft. Lokale Lookalike-Attribute schützen keine gewöhnlichen Helpers. Ein unreferenzierter privater Helper neben einem gültigen Test bleibt Kandidat. Beide API-Modi und lokale/breite Unsicherheit halten ihren definierten Vertrag ein; breite Ausschlüsse sind im Report sichtbar.
6. **Rollen und Grenzen:** Klassifikation über Referenz, Namen und Pfad ist in der Scope-Übersicht nachvollziehbar. Ohne Testmarker bleibt Code trotzdem Wartbarkeitsziel. Generierte Dateien/Symbole und externe generierte Testquellen behalten ihre bisherigen Grenzen.
7. **Reporting:** Der gemeinsame Root-Index und die sechs View-Indizes zeigen die drei Bereiche einschließlich leerer Arbeitsumfänge. Nichtleere Analyseberichte liegen ausschließlich im passenden Bereich und in der passenden View; Summen, repräsentative Projekt-/Dateizahlen, Vorkommensrollen, bereichsübergreifende Related-Verweise und Sortierung bleiben widerspruchsfrei. Ein Produktions-Finding mit Testevidenz bleibt unter `production`; ein gemischter Duplikationscluster steht vollständig und einmal pro View unter `mixed`. Relative Links zur Root-Anleitung, zwischen Bereichen und zu den Full-Audit-Views funktionieren. Eine aktive Analyse ohne Findings und eine deaktivierte Analyse sind in der Scope-Übersicht unterscheidbar.
8. **Baseline:** Ohne Baseline enthalten beide Views alle aktuellen Findings. Testdateiänderungen funktionieren mit bestehender Baseline; unveränderter Testcode wird bei einem gewöhnlichen Datei-basierten Finding nicht künstlich als geändert markiert. Die spezielle Snapshot-Auswahl für fehlende Testpfade bleibt erhalten.
9. **Produktgrenzen:** Der Reviewlauf bleibt ein Signalbericht; Findings erzeugen weder Refactoring noch Buildfehler. Fehler oder Abbruch veröffentlichen keinen partiellen Report.
10. **Praktischer Agentenaudit:** Eine repräsentative Lösung mit langen Tests, String-Fixtures, nachvollziehbaren Wiederholungen und Testinfrastruktur wird ausdrücklich als Voll-Audit beauftragt. Der Root-Auftrag umfasst `production`, `tests` und `mixed`; auch bei getrennter Bearbeitung gehen ihre Ergebnisse in die Abschlussbewertung ein. Ein gesonderter Teilauftrag behauptet keinen Voll-Audit und darf relevante Quellen anderer Bereiche als Kontext einbeziehen. Jede Signalart wird anhand konkreter Quellen als nützlich, akzeptables Design, Fehlalarm oder klärungsbedürftig bewertet. Laufzeit und Findingumfang werden zum Erkennen von Vergleichs-/Reportproblemen festgehalten; größere Testbestände dürfen nicht durch stille Top-N-Filter verschwinden. Das prüft Verständlichkeit und Interpretationsrisiken, verspricht aber keine objektive Quote „guter“ Tests.

## Offene Entscheidungen (nur Draft)

1. **Reportgliederung:** Nach der zusätzlichen Nutzeridee empfiehlt dieser Draft `production`, `tests` und `mixed` mit je `changed-files` und `all-findings` innerhalb desselben Reportlaufs. Die Zustimmung zu dieser Variante einschließlich des dritten Bereichs für gemischte Findings steht aus.

`status: ready` erfolgt erst nach Schließen dieser Punkte und ausdrücklicher Nutzerfreigabe gemäß [Konzeptrolle](../../.agents/agent-workflow/01-konzept-planung.md). Bis dahin ist dies ein diskutierbarer, persistierter Entwurf.
