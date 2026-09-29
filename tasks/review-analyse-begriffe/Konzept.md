---
status: ready
---

# Begriffe und Grundvertrag für Review-Analysen

## Intention

AiNetReview soll weitere fachliche Auswertungen aufnehmen können, ohne dass deren Namen einen Gesetzesverstoß, einen Defekt oder einen automatischen Änderungsauftrag behaupten. Ein konsistenter Wortschatz soll Entwicklern und Review-Agenten zeigen, was konfiguriert wird, was im Bericht erscheint und welche Entscheidung erst nach Prüfung des Kontexts fällt.

## Ausgangspunkt

- Die ausführbaren Einheiten heißen im aktuellen Code `IReviewRule`, `RuleDescriptor` und `RuleRegistry`; die Konfiguration verwendet `rules`, und Berichte liegen unter `rules/`.
- Die drei registrierten Einheiten suchen Kontrollfluss-Ausreißer, mögliche ungenutzte Deklarationen und ähnliche Codekörper. Ihre Treffer sind Review-Kandidaten, keine bewiesenen Verstöße. Siehe `docs/review/findings.md`.
- Ein Bericht trennt bereits die Einheit mit Zweck und Review-Fragen von einzelnen `FindingDraft`-Fundstellen; die Tabelle nennt die Beobachtung an einer Fundstelle `Signal`.
- Die Repository-Anweisungen unter `.agents/rules/` sind tatsächlich verbindliche Arbeitsregeln. Produktterminologie und Agentenanweisungen sollten nicht verwechselt werden.

## Begriffsmodell

| Ebene | Empfohlener Begriff | Bedeutung |
| --- | --- | --- |
| Konfigurierbare, wiederholbare Auswertung | **Review-Analyse** (`review analysis`) | Definiert Auswahl, Messung, Optionen, Grenzen und Review-Fragen; ihr Lauf kann null oder mehrere Fundstellen ergeben. |
| Einzelnes Ergebnis im aktuellen Lauf | **Fundstelle** (`finding`) | Verweist auf überprüfbaren Code und Evidenz; ist noch kein Defekturteil. |
| Beobachtung an der Fundstelle | **Signal** (`signal`) | Beschreibt, weshalb die Stelle zur Prüfung vorgeschlagen wird; ist kein Schweregrad und keine Handlungsanweisung. |
| Entscheidung nach Kontextprüfung | **Review-Entscheidung** | Änderung oder begründete Akzeptanz liegt beim prüfenden Menschen bzw. Agenten mit Nutzerkontext. |

**Begründung:** „Rule“ passt zum technischen Muster einer ausführbaren Auswertung, klingt für Nutzer und Agenten aber nach einer verbindlichen Norm. „Hint“ ist zu unverbindlich und kann wie ein beiläufiger Tipp wirken. „Signal“ beschreibt den beobachteten Anlass gut, verwechselt als Name der konfigurierbaren Einheit jedoch Ursache und Ergebnis. „Check“ kann ein Bestehen oder Scheitern nahelegen. „Review-Analyse“ benennt die Tätigkeit neutral und lässt sowohl statistische Ausreißer als auch künftige Kontextanalysen zu.

## Alt → Neu: Namen und Bedeutung

| Bisher | Zielname | Ort / Bemerkung |
| --- | --- | --- |
| `AiNetReview.Core.Rules` | `AiNetReview.Core.ReviewAnalyses` | Namespace und Ordner für ausführbare Produktanalysen; `Core.Analysis` für Lade-/Laufinfrastruktur bleibt eigenständig. |
| `IReviewRule` | `IReviewAnalysis` | Schnittstelle der ausführbaren Einheit. |
| `RuleDescriptor` | `ReviewAnalysisDescriptor` | Metadaten und Review-Fragen. |
| `RuleOptionDescriptor` | `ReviewAnalysisOptionDescriptor` | Definition einer Konfigurationsoption. |
| `RuleOptions` | `ReviewAnalysisOptions` | Effektive Optionen eines Laufs. |
| `RuleResult` | `ReviewAnalysisResult` | Fundstellen einer ausgeführten Analyse. |
| `RuleRegistry` | `ReviewAnalysisRegistry` | Explizit registrierte Analysen. |
| `MethodControlFlowOutliersRule` | `MethodControlFlowOutliersAnalysis` | Erste Produktionsanalyse; Ordner bleibt fachlich benannt. |
| `DeadCodeCandidatesRule` | `DeadCodeCandidatesAnalysis` | Zweite Produktionsanalyse. |
| `DuplicateCodeCandidatesRule` | `DuplicateCodeCandidatesAnalysis` | Dritte Produktionsanalyse. |
| `ConfiguredRule` | `ConfiguredReviewAnalysis` | Validierte, aktive Analyse samt Optionen. |
| `RuleRunResult` | `ReviewAnalysisRunResult` | Ergebnis einer Analyse innerhalb eines Laufs. |
| `RuleId`, `ruleId` | `AnalysisId`, `analysisId` | Deskriptor, Konfiguration, Laufresultat, Fundstellenvalidierung und lokale Variablen. Die **Werte** der drei produktiven IDs bleiben gleich. |
| `ReviewConfig.Rules`, `ReviewRunResult.Rules`, `RuleRegistry.Rules` | jeweils `Analyses` | Sammlungen ausführbarer Analysen. |
| `AddAiNetReviewRules` | `AddAiNetReviewAnalyses` | Registrierung im Host und in Tests. |
| `FixtureFindingRule`, `ReportRule` und weitere Test-Dummys | `FixtureFindingAnalysis`, `ReportAnalysis` usw. | Testnamen und Testordner ebenfalls umbenennen. |
| JSON `rules` | JSON `analyses` | Projektkonfiguration und manuelle Audit-Profile. `schemaVersion: 1` bleibt; alle vorhandenen JSON-Dateien werden geändert. Alte `rules`-Dateien sind ungültig. |
| Berichtspfad `rules/<id>.md` | `analyses/<id>.md` | Neu erzeugte Berichte, Links und Berichtstests. Frühere lokale Altberichte wurden entfernt. |
| „Rules with open findings“, „all rules are disabled“ | „Analyses with open findings“, „all analyses are disabled“ | Berichtstexte und zugehörige Tests. `Signal` als Spaltenüberschrift bleibt erhalten. |
| `docs/development/adding-rules.md` | `docs/development/adding-review-analyses.md` | Anleitung samt Link im Dokumentationsindex. |

`FindingDraft`, `FindingEvidence`, `CurrentFindingValidator`, `ReviewRunner`, `ReviewRunResult` und `MarkdownReportWriter` behalten ihren Namen: Sie bezeichnen Fundstellen oder den gesamten Review-Lauf, keine einzelne „Rule“. Ihre `Rule...`-Parameter, Eigenschaften, Meldungen und Hilfsmethoden werden trotzdem umbenannt.

## Vollständige Änderungsflächen für die spätere Umsetzung

Die Liste beschreibt die betroffenen Flächen, keine Arbeit in diesem Konzeptschritt. Eine Suche über das gesamte Repository nach Produktverwendungen von `rule`, `Rule`, `rules/` und `ruleId` ist die abschließende Vollständigkeitskontrolle. Treffer, die die **verbindlichen Agentenregeln** oder die **echten AiNetLinter-Regeln** meinen, bleiben bewusst erhalten.

1. **Core-Typen und Dateipfade:** `src/AiNetReview.Core/Rules/**` einschließlich aller drei Produktionsanalysen und ihrer internen Dead-Code-Helfer; Namespaces, `using`, Typen, Parameter, Fehlermeldungen und Deskriptor-Properties. Die fachlichen IDs und ihre Auswahlsemantik bleiben stabil.
2. **Core-Lauf und Konfiguration:** `src/AiNetReview.Core/Analysis/{ReviewRunner,SolutionLoader,LoadedSolution,ReviewSourceClassifier}.cs`, `src/AiNetReview.Core/Findings/CurrentFindingValidator.cs`, `src/AiNetReview.Core/Configuration/{ReviewConfig,ReviewConfigValidator,DefaultReviewConfigGenerator}.cs` und `src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs`. Dazu gehören Registrierungszugriffe, bedingtes Laden von Markup, Identitätsschlüssel, JSON-Validierung und -Erzeugung, Dateipfade, Indextext, Optionen und Fehlertexte.
3. **Host:** `src/AiNetReview/{Program.cs,Bootstrap/ServiceRegistration.cs,Cli/ReviewCommand.cs}`; Registrierung und Protokolltexte.
4. **Gespeicherte Konfiguration:** `ainetreview.json`, alle vorhandenen `audit-targets/*.json`-Profile und die in Tests eingebetteten JSON-Beispiele werden direkt auf `analyses` umgestellt. Die Audit-Profile sind derzeit lokal und Git-ignoriert; sie dürfen nicht als vermeintlich irrelevante Dateien übersehen werden. `schemaVersion: 1` bleibt in Projektkonfigurationen und Generator erhalten: Dieses Greenfield-Vorhaben ersetzt den noch nicht zu bewahrenden v1-Vertrag. Der Validator akzeptiert nur `analyses`, nicht `rules`; es gibt keinen Legacy-Leser, Alias, Migrationspfad oder automatisch konvertierende CLI.
5. **Tests und Fixtures:** alle produktbezogenen Tests unter `tests/AiNetReview.FastTests/**` und `tests/AiNetReview.IntegrationTests/**`, besonders die bisherigen `Rules/`-Tests, `RuleServiceRegistrationTests`, `FixtureRules/`, Konfigurations- und Generator-Tests, `ReviewRunnerTests`, Host-/Zero-Config-/Audit-Tests sowie Markdown-Publikations- und Linktests. Test-Dateinamen, Namespaces, Fixture-Typen, Beispiel-IDs, Erwartungstexte und `rules/`-Pfade werden mitgezogen. Die manuellen Audit-Profile selbst lösen keine reguläre Testsuite aus.
6. **Aktuelle Produktdokumentation und Einstieg:** `README.md`, `docs/README.md`, `docs/architecture/{overview,dependencies}.md`, `docs/configuration/file-format.md`, `docs/interfaces/cli.md`, `docs/review/findings.md`, `docs/development/{adding-rules,build-and-tests}.md`; Inhaltsverweise und Beispiele werden konsistent.
7. **Repo-Anweisungen und Aufgabenbestand:** In `.agents/rules/03-product-boundaries.mdc` werden Aussagen über **Produktanalysen** angepasst. `.agents/rules/08-production-rule-configuration.mdc` wird in `08-production-review-analysis-configuration.mdc` umbenannt und inhaltlich aktualisiert; `.agents/rules/README.md` und alle Verweise auf die Datei werden mitgezogen. `AGENTS.md` und die übrigen Agent-Dateien werden auf Produktbezüge durchsucht. `.agents/rules/` als Ort verbindlicher Agentenregeln bleibt. Produktbezüge in `tasks/ideen/{erste-fachliche-review-signale,wenige-starke-audit-signale,checksum-gebundene-ignore-liste,ainetlinter-regeln}.md` sowie `tasks/integrations-vereinfachung/Konzept.md` werden angepasst. Bezeichnungen und Zitate der externen AiNetLinter-Regeln bleiben erhalten.
8. **Berichte:** Der Generator verwendet `analyses/` und neue Überschriften; Quelllinks und Indexlinks neuer Läufe müssen zusammenpassen. Die früheren lokalen Altberichte unter `audit-reporting/` wurden vom Nutzer entfernt; es gibt keinen Berichtsmigrations- oder Bereinigungsschritt.

Es gibt derzeit keine `Rule`-Bezeichnung in den drei produktiven ID-Werten `method-control-flow-outliers`, `dead-code-candidates` und `duplicate-code-candidates`; ein Umbenennen dieser IDs wäre ein eigener Identitätsbruch ohne terminologischen Nutzen.

## Scope

### Muss

- Einen konsistenten Produktwortschatz für konfigurierbare Auswertung, einzelne Fundstelle, beobachtetes Signal und anschließende Review-Entscheidung festlegen.
- Den Bedeutungsrahmen für weitere Review-Analysen beschreiben: Eine Fundstelle braucht nachvollziehbare Evidenz und eine konkrete Review-Frage; sie ist für sich weder Defektbeweis noch Refactoring-Auftrag oder Build-Fehler.
- Die Produktbenennung an allen oben aufgeführten Flächen ändern: Code, JSON-Vertrag, neue Berichte, Tests, aktuelle Dokumentation und produktbezogene Repo-Anweisungen. Keine dauerhafte Mischung aus `Rule` und `ReviewAnalysis` für denselben Produktbegriff.
- Bestehende Projektkonfigurationen und Audit-Profile auf `analyses` umstellen. Generator und Validator verwenden ausschließlich `analyses` im Schema v1; `rules` wird als unbekanntes Feld abgelehnt. Keine Kompatibilitäts- oder Migrationslogik.

### Nicht

- Neue fachliche Review-Analysen, Auswahlmetriken oder Schweregrade entwerfen.
- Review-Fundstellen zu Build-Fehlern machen oder Code automatisch ändern.
- Die verbindlichen Agentenanweisungen unter `.agents/rules/` umbenennen.
- In diesem Konzeptschritt Code, Konfiguration oder aktuelle Produktdokumentation ändern.

## Verifikation

- Der definierte Wortschatz lässt sich auf alle drei vorhandenen Produktanalysen und auf einen Lauf ohne Fundstellen widerspruchsfrei anwenden.
- Konfiguration, öffentliche Texte, neue Berichte, Dokumentation, Agent-Dateien mit Produktbezug und interne Typnamen verwenden die neue Benennung ohne Legacy-Alias.
- Ein neuer Review-Agent kann aus dem Konzept erkennen, welche Aussage eine Fundstelle erlaubt und welche Entscheidung Kontextprüfung verlangt.
- Tests prüfen Konfigurationsgenerierung/-validierung mit `analyses` bei `schemaVersion: 1`, Ablehnung von `rules`, Registrierung, alle drei Analysen, Berichtspfade/-links und die beiden Leerfälle (aktive Analyse ohne Fundstellen; keine aktive Analyse).
- Eine vollständige Repository-Suche nach alten Produktbegriffen erklärt jeden verbleibenden Treffer als echten Agentenregel- oder AiNetLinter-Bezug oder als Alt-Text in der Alt→Neu-Tabelle dieses Konzepts.
