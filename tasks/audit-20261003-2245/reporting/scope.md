# Reporting-Teilreview: Scope und Nachweise

## Beauftragung

ReviewRunner, CurrentFindingValidator, ReviewFindingBuilder, ReviewMapBuilder, Findings-Verträge sowie Markdown-, Projektkarten- und Auditkarten-Berichte. Read-only; alle Arbeitsergebnisse nur in diesem Verzeichnis.

## Hauptquellen

- `src/AiNetReview.Core/Analysis/ReviewRunner.cs:14` — validierter Analyseablauf, Sortierung und vollständiger Lauf.
- `src/AiNetReview.Core/Findings/CurrentFindingValidator.cs:13` — Pfad-, Snapshot-, Evidenz-, Symbol- und Identitätsvalidierung.
- `src/AiNetReview.Core/Findings/FindingDraft.cs:8` und `src/AiNetReview.Core/Analysis/ReviewFinding.cs:8` — Draft-, Occurrence-, Subject- und Referenzverträge.
- `src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs:8` — cross-analysis Symbolgruppen und Herkunftsrollen.
- `src/AiNetReview.Core/Analysis/ReviewMapBuilder.cs:16` sowie `src/AiNetReview.Core/Analysis/ReviewMaps.cs:6` — deterministische Snapshot-Maps und Payload.
- `src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs:60` — temporäre Komplettpublikation; `:351` Rollenrouting; `:824` Related-Routen; `:1145` Code-Spans/Escaping.
- `src/AiNetReview.Core/Reporting/MapsReportWriter.cs:14` und `src/AiNetReview.Core/Reporting/AuditMapReportWriter.cs:14` — Navigationskarten und Audit-Routing.
- `src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceCandidatesAnalysis.cs:151` — shortest-path Related-Symbole versus Subject-Symbole.
- `docs/review/findings.md` — veröffentlichte Erwartungen zu Findings, Rollen, Pfadkontext und Karten.

## Geprüfte Tests (statisch, nicht ausgeführt)

- `tests/AiNetReview.IntegrationTests/Analysis/ReviewRunnerTests.cs`: vollständige Ergebnisse, Snapshot-Evidenz, Cross-Analysis-Beziehungen, Rollen und Occurrence-Identität, Fehler/Cancellation.
- `tests/AiNetReview.FastTests/Analysis/ReviewMapBuilderTests.cs`: Namespaces, partielle/nested Typen, Dateibytes, direkte Rollenkanten, Cancellation.
- `tests/AiNetReview.FastTests/Reporting/MarkdownReportWriterTests.cs`: deterministisches Rendering, Escaping, Routing, leere Bereiche, Maps, Related-Links und Occurrences.
- `tests/AiNetReview.IntegrationTests/Reporting/MarkdownReportWriterPublicationTests.cs`: konkurrierende Runs, Windows-Lock-Wiederholung, Cleanup, Cancellation und Schreibfehler.

## Nachgewiesene Signalbeispiele

- `audit-reporting/20261003T204611Z-69b83ba6/production/type-dependency-cycle-candidates.md:22-35` zeigt den Reporting-Zyklus und die konkreten MemberUse-Kanten.
- `audit-reporting/20261003T204611Z-69b83ba6/production/missing-test-evidence-candidates.md:24-109` zeigt 171 Finding-Kandidaten und bis zu 20 gleichanalysebezogene IDs unter `Related` für Findings, die den selben indirekten Testpfad teilen.
