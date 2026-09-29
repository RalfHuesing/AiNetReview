# Roadmap: Review-Analyse Non-ASCII Identifiers (`non-ascii-identifiers`)

- [x] **Punkt 1 — Core-Analyse `NonAsciiIdentifiersAnalysis` und FastTests implementieren**
  - Intention: Implementierung von `NonAsciiIdentifiersAnalysis` (`IReviewAnalysis`) in `AiNetReview.Core` mit `ReviewAnalysisDescriptor` (`analysisId`: `"non-ascii-identifiers"`, `defaultEnabled: true`, keine Optionen), Syntax-Prüfung für Namespaces, Typen, Typ-Mitglieder, Parameter und lokale Funktionen/Variablen, Verbatim-Handling (`@`), Erzeugung deterministischer `FindingDraft`-Objekte und Ignorieren von Testprojekten sowie generierten Dateien via `ReviewSourceClassifier`.
  - Nicht: Keine Host-Registrierung oder Doku-Änderungen in diesem Schritt.
  - Abnahme: `NonAsciiIdentifiersAnalysisTests` in `AiNetReview.FastTests` prüft alle Bezeichner-Typen, Sonderzeichen/Umlaute, Verbatim-Bezeichner und Ausschlussfilter erfolgreich.

- [x] **Punkt 2 — Host-Registrierung, Root-Konfiguration und Reporting anbinden**
  - Intention: Registrierung der Analyse in `ServiceRegistration.AddAiNetReviewAnalyses`, Eintrag in das Repo-Root `ainetreview.json` (`"enabled": true` nach Regel 08), Anpassung/Prüfung von `DefaultReviewConfigGenerator`, `ReviewConfigValidator` und Signal-Darstellung in `MarkdownReportWriter`.
  - Nicht: Keine Änderungen an der Analyse-Kernlogik.
  - Abnahme: Alle Konfigurations-, Validator- und Reporting-Tests in `AiNetReview.FastTests` laufen erfolgreich durch; Root-`ainetreview.json` ist valide.

- [x] **Punkt 3 — Dokumentation aktualisieren**
  - Intention: Dokumentation der neuen Analyse in `docs/` (`docs/review/findings.md`, `docs/configuration/file-format.md`, `docs/architecture/overview.md`, `docs/README.md`, `docs/interfaces/cli.md`) gemäß Regel 02.
  - Nicht: Keine Code-Änderungen.
  - Abnahme: Dokumentation spiegelt alle vier registrierten Produktionsanalysen exakt wider.

- [ ] **Punkt 4 — Audit & Gesamtabnahme**
  - Intention: Vollständige Qualitäts- und Regelprüfung (Regeln 01–08), Ausführung aller Tests und Bereinigung.
  - Nicht: Keine Funktionserweiterungen.
  - Abnahme: `dotnet test` auf der Solution läuft vollständig ohne Fehler durch; Audit bestätigt Konformität zu [Konzept.md](Konzept.md).
