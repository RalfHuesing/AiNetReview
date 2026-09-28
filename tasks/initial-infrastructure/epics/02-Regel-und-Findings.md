# Epic 2 – Regeln und aktuelle Findings

Dieses Epic definiert den Vertrag für C#-Review-Regeln und ihre Ergebnisse **innerhalb eines einzelnen vollständigen Laufs**. Eingaben stehen in [Epic 1](01-Eingaben-und-Host.md), Markdown-Berichte in [Epic 3](03-Storage-und-Berichte.md). Zwischen Läufen gibt es keine Finding-Identität und keinen Review-Zustand.

## Regelvertrag

`IReviewRule` liefert einen `RuleDescriptor` mit stabiler Regel-ID, Titel, positiver ganzzahliger `behaviorVersion`, Optionsschema mit Defaults und Validierung sowie Zweck, Messung und Review-Fragen. Die Regel-ID ist zugleich ein sicherer einzelner Dateinamenbestandteil für `rules/<ruleId>.md`; Pfadseparatoren, Traversal und auf Windows ungültige oder reservierte Namen werden abgewiesen. Der Deskriptor ist die einzige Quelle für Regeloptionen und die im Bericht gezeigte Regelbeschreibung. `behaviorVersion` beschreibt die fachliche Bedeutung der Regel für Leser des Berichts; sie steuert keinen Vergleich mit früheren Läufen.

`ExecuteAsync(ReviewContext, RuleOptions, CancellationToken)` gibt erst nach vollständiger Analyse ein unveränderliches `RuleResult` zurück. Regelobjekte halten keinen veränderlichen Zustand zwischen Läufen. `ReviewContext` enthält die einmal geladene Roslyn-`Solution`, die validierte Projektwurzel und gemeinsam nutzbare Analysezugriffe. Der Runner ruft aktivierte Regeln in aufsteigender Regel-ID nacheinander auf. Ausnahme, Abbruch oder unvollständige Analyse lassen den ganzen Lauf scheitern; eine leere Ergebnisliste bedeutet ausschließlich „vollständig analysiert und kein Treffer“.

Eine Regel liefert Finding-Entwürfe mit `projectPath`, `sourcePath`, `subjectId`, `discriminator`, 1-basierter `startLine`, nichtleerem Begründungstext, einer Map endlicher numerischer Metriken und mindestens einem Evidenz-Eintrag. Evidenz enthält `sourcePath`, 1-basierte `line`, `label`, `detail` und `snippet` als Strings; `snippet` ist ein **aktueller** Codeausschnitt aus diesem Lauf. Pfade sind relativ zur Projektwurzel und verwenden `/`. Die Regel bestimmt Aussage, Metriken, Evidenz und Umfang der aktuellen Ausschnitte. Sie schreibt weder Dateien noch Markdown und verwaltet keine früheren Findings.

Der Runner validiert die Entwürfe, zählt und sortiert sie und übergibt vollständige Resultate generisch an den Berichtsgenerator. Er speichert weder Entwürfe noch Code über den Prozess hinaus. Für jedes Finding ist das Tupel `(ruleId, projectPath, sourcePath, subjectId, discriminator)` innerhalb eines Laufs eindeutig. Alle Bestandteile sind nichtleer. `projectPath` bezeichnet die besitzende `.csproj`, `sourcePath` eine analysierte `.cs`; doppelte Tupel oder ungültige Pfade sind `ANALYSIS_FAILED`. Für symbolbezogene Regeln ist Roslyns `DocumentationCommentId` eine geeignete `subjectId`; dateibezogene Regeln können `file` verwenden. Die Regel dokumentiert die Wahl. Das Tupel dient nur der Eindeutigkeit im aktuellen Ergebnis; es ist keine gespeicherte oder über Läufe hinweg stabile Finding-ID.

Eine neue fachliche Regel benötigt ihren Regelordner, gezielte Tests und eine explizite Registrierung im Host. Runner, CLI und Berichtsgenerator werden dafür nicht geändert. Es gibt keine zentrale Liste von Sonderfällen pro Regel, kein Assembly-Scanning und kein dynamisches Nachladen.

## Startregel und Test-Fixture

Die einzige produktiv registrierte Regel heißt `template-noop`, hat `behaviorVersion = 1` und das leere Optionsschema `{}`. Sie liefert unabhängig vom Quelltext ein vollständiges leeres `RuleResult`. Sie demonstriert Registrierung, Konfiguration und einen vollständigen Null-Finding-Lauf; sie ist keine fachliche Qualitätsregel.

Die IntegrationTests registrieren eine ausschließlich dort definierte `FixtureFindingRule` mit ID `fixture-finding`, `behaviorVersion = 1` und einer nichtleeren String-Option `scenario` mit Default `base`. Sie liefert aus einer kleinen, kontrollierten C#-Test-Solution mindestens zwei reproduzierbare, voneinander unabhängige Findings mit aktuellen Evidenz-Ausschnitten. Tests verändern Quelltext oder Optionen und belegen, dass jeder Lauf ausschließlich den dann aktuellen Befund berichtet. Die produktive EXE lädt weder Test-Assemblies noch Testregeln dynamisch.

## Kein Verlauf

Jedes von einer aktivierten Regel aktuell gelieferte Finding erscheint im Bericht dieses Laufs. Ein früherer Bericht oder eine frühere Entscheidung wird nicht eingelesen. Deshalb gibt es keine Zustände wie `accepted`, `false-positive`, `updated`, `reopened` oder `resolved`, keine Fingerprints, Vergleichs-Checksummen, Quellcode-Snapshots und keine Unterdrückung unveränderter Findings. Eine deaktivierte Regel liefert in diesem Lauf keinen Regelbericht und keine Findings; frühere Berichte bleiben als vom Nutzer verwaltete Dateien bestehen.
