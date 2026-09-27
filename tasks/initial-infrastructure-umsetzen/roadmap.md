# Roadmap: Initial-Infrastruktur umsetzen

Diese Roadmap ist der Ausführungsplan zum [Konzept](Konzept.md). Produktverträge und Abnahme stehen ausschließlich im [ursprünglichen Konzept](../initial-infrastructure/AiNetReview-Konzept.md) und in [Epic 1](../initial-infrastructure/epics/01-Eingaben-und-Host.md), [Epic 2](../initial-infrastructure/epics/02-Regel-und-Findings.md), [Epic 3](../initial-infrastructure/epics/03-Storage-und-Berichte.md) und [Epic 4](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md). Die [ursprüngliche Roadmap](../initial-infrastructure/roadmap.md) bleibt die grobe Fortschrittsübersicht und wird bei späterer Umsetzung mit dieser Liste abgeglichen. Die Reihenfolge hier folgt Abhängigkeiten; deshalb wird Epic 1 erst nach Epic 2 und 3 vollständig abgenommen.

Das [Konzept](Konzept.md) ist vom Nutzer freigegeben (`status: ready`). Die Implementierung beginnt erst mit dem gesonderten Aufruf von [Schritt 3](../../.agents/agent-workflow/03-orchestrierte-umsetzung.md). Bis dahin bleiben alle Checkboxen offen.

Für jeden Implementierungspunkt gilt: Ist-Stand und betroffene Repo-Regeln prüfen; vor einem passenden Infrastrukturbaustein [AiNetLinter-Implementierung und Tests](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#ainetlinter-als-referenz) nur lesend auswerten; relevante Verträge und Fehlerfälle automatisiert prüfen; betroffene `docs/`-Seiten erst für verifiziertes Verhalten aktualisieren; [Build- und Test-Gates](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#testebenen) ausführen; nur eigene Dateien und Checkboxen atomar committen. Kein Punkt erweitert die Produktgrenzen aus dem [Konzept](Konzept.md#nicht).

Jeder Audit-Punkt beauftragt **genau einen lesenden Subagenten** mit Modell `gpt-6-sol`, Reasoning `medium`. Er liest das gesamte ursprüngliche Konzept, alle vier Epics, die aktuelle Anwendung, relevante Tests und Doku sowie den bisherigen Roadmap-Stand. Er prüft Architektur, Verträge, Datenfluss und Integration im Zusammenhang, nicht nur den letzten Diff. Noch nicht fällige spätere Roadmap-Punkte bewertet er als geplante Arbeit; ein Finding braucht eine Abweichung im erreichten Stand oder eine belegte Gefahr für die weitere Umsetzung. Er meldet Findings mit Schwere, Fundstelle und Begründung an den Orchestrator; auch „keine Findings“ wird ausdrücklich gemeldet. Der Orchestrator priorisiert nach Auswirkung auf Vertrag, Abnahme und weitere Arbeit. Für diesen Task sind nach einem Audit **bis zu drei sequenzielle Korrekturschritte** zulässig; damit gilt die vom Nutzer freigegebene Ausnahme zur Ein-Korrektur-Grenze aus [Schritt 3](../../.agents/agent-workflow/03-orchestrierte-umsetzung.md#audit). Jeder Korrekturschritt wird geprüft und einzeln committet; es gibt keinen automatischen Audit-Loop. Ein Audit-Haken bestätigt den geprüften Bericht, die Korrekturentscheidungen und die Dokumentation verbleibender Findings.

Verbleibende Findings werden mit Fundstelle, Auswirkung, Status, Begründung und nächstem Schritt in `audit.md` dieses Task-Verzeichnisses notiert. Die Datei wird erst beim ersten Audit mit Findings angelegt. Nicht blockierende Hinweise dürfen offen bleiben, während der Orchestrator die nächste unabhängig bearbeitbare Checkbox übernimmt. Eine Checkbox mit nicht erfüllter Abnahme bleibt offen; das [Produkt-DoD](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#definition-of-done) wird nicht abgeschwächt. Falls der erste offene Punkt nach den Korrekturen nicht bearbeitbar ist, darf der Orchestrator für diesen Task einen späteren unabhängigen Punkt vorziehen und hält die Abhängigkeit beim offenen Punkt fest. Erst wenn keine unabhängige Arbeit mehr möglich ist, stoppt er und nennt dem Nutzer den harten Blocker, die bisherigen Versuche und die erforderliche Entscheidung. Vorhandene fremde Änderungen werden gemäß [Git-Regel](../../.agents/rules/05-git.mdc) nicht mitcommittet; bei Bedarf wird ein isolierter Worktree verwendet.

## M1 — Regel- und Finding-Kern (Epic 2 abschließen)

- [x] **M1-T1 — Grundlage und Host-Logging herstellen.**
  - Intention: Das vorhandene Skelett für die folgenden Core- und Host-Arbeiten nutzbar machen.
  - Scope: Offene Infrastrukturänderungen der [ursprünglichen M0](../initial-infrastructure/roadmap.md#m0--grundlage) abschließen; Build und FastTests prüfen; Serilog vom Host-Start an nach [Epic 1: Logging](../initial-infrastructure/epics/01-Eingaben-und-Host.md#logging) einrichten.
  - Nicht: Noch keine CLI-/MCP-Fachfunktionen und kein Finding-Store.
  - Abnahme: Skript-Gates bestehen; Prozesstests belegen EXE-relativen Dateisink, Fehler bei nicht beschreibbarem Log und freies stdout/stderr gemäß Vertrag.
- [x] **M1-T2 — Regelvertrag, Registrierung und Katalogkern.**
  - Intention: Regeln ohne Änderungen an generischen Komponenten ergänzbar machen.
  - Scope: [Epic 2: Regelvertrag und Startregel](../initial-infrastructure/epics/02-Regel-und-Findings.md#regelvertrag) sowie [Epic 4: DI und Regel-Erweiterung](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#di-und-regel-erweiterung) umsetzen; produktiv nur `template-noop` registrieren; Katalog und JSON-Vorlage aus Deskriptoren erzeugen.
  - Nicht: Keine fachliche Review-Regel, keine dynamische Rule-Suche und noch kein produktiver Verdict-Speicher.
  - Abnahme: Tests belegen Deskriptorvalidierung, doppelte IDs, Defaults, Katalog-Determinismus und vollständige leere Regelresultate; die Fixture-Regel bleibt im Testprojekt.
- [x] **M1-T3 — Konfiguration und Solution-Laden.**
  - Intention: Einen gemeinsamen, validierten Analyse-Eingang schaffen.
  - Scope: [Epic 1: Konfiguration und Projektwurzel](../initial-infrastructure/epics/01-Eingaben-und-Host.md#konfiguration-und-projektwurzel) einschließlich JSON, Regeloptionen aus der Registry, Pfadgrenzen, Git-Unabhängigkeit, Solution-Lader, Compilations und Fehlerzuordnung umsetzen.
  - Nicht: Keine zusätzlichen Konfigurationsmodi, keine Rule-Overrides und keine CLI-/MCP-Adapter.
  - Abnahme: Fast- und IntegrationTests belegen gültige `.sln`/`.slnx`, ungültige Keys, unbekannte Regeln und Pfade, Quellflucht, fehlende Referenzen und unvollständige Analyse als Fehler statt Null-Finding-Erfolg.
- [x] **M1-T4 — Finding-Abgleich und Runner.**
  - Intention: Findings unabhängig von späteren Schnittstellen und Speicherdateien korrekt einordnen.
  - Scope: [Epic 2: Identität, Fingerprint und Zustandsautomat](../initial-infrastructure/epics/02-Regel-und-Findings.md#identität) samt generischem Runner, Quell-Hashes und testgebundener `FixtureFindingRule` umsetzen. Einen vorübergehenden Store nur gemäß [Epic 4: Umsetzung](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#umsetzung) verwenden.
  - Nicht: Keine produktive Entscheidung ohne dauerhafte Speicherung bestätigen; keine Markdown-Berichte oder CLI-/MCP-Verdicts in diesem Punkt.
  - Abnahme: Tests prüfen unabhängige Findings, Byteformat des Fingerprints, jeden Zustandsübergang, Regelreihenfolge, Abbruch und Analysefehler; `template-noop` liefert nur nach vollständigem Lauf null Findings.
- [x] **M1-A — Gesamtaudit nach Epic 2.**
  - Auftrag: Epic 2 samt M1-Grundlagen gegen alle Produktverträge und den tatsächlichen Code prüfen; insbesondere Erweiterbarkeit, vollständige Analyse und die Trennung von Test- und Produktregel.
  - Abnahme: Lesender `gpt-6-sol`-Subagent mit Reasoning `medium` berichtet dem Orchestrator belegte Findings oder explizit keine; Korrekturen und Restbefunde sind gemäß Audit-Regel festgehalten.

## M2 — Speicherung und Berichte (Epic 3 abschließen)

- [ ] **M2-T1 — Store-Schemata und dauerhafte Ereignisse.**
  - Intention: Vollständige Runs und Urteile versionierbar erhalten.
  - Scope: [Epic 3: Grundform, Manifest, Finding-Ereignis, Snapshot und Entscheidung](../initial-infrastructure/epics/03-Storage-und-Berichte.md#grundform-und-ids) umsetzen; echten JSON-Store an den Runner anschließen.
  - Nicht: Keine automatische Retention, kein Statistik-Dashboard und keine Speicherung vollständiger Quellpfade.
  - Abnahme: FastTests prüfen Schemata, ID- und Referenzbeziehungen, Snapshot-Bytes, idempotente Urteile, Urteilskorrekturen und Laden nach Neustart.
- [ ] **M2-T2 — Deterministische Markdown-Berichte.**
  - Intention: Vollständige Läufe als nachvollziehbare Review-Wegweiser ausgeben.
  - Scope: [Epic 3: Markdown-Berichte](../initial-infrastructure/epics/03-Storage-und-Berichte.md#markdown-berichte) mit Index, Regeldateien, Zählwerten, Sortierung, relativen Links und Snapshot-Verweisen umsetzen.
  - Nicht: Keine automatische Refactoring-Anweisung und kein vollständiger Quellcode-Dump im Bericht.
  - Abnahme: Tests prüfen Bytes und Linkziele auch für Sonderzeichen, leere Regelresultate, offene versus akzeptierte Findings und deterministische Reihenfolge.
- [ ] **M2-T3 — Atomare Veröffentlichung und Speicherkonflikte.**
  - Intention: Fehlgeschlagene oder konkurrierende Läufe dürfen keinen gültigen Verlauf vortäuschen.
  - Scope: [Epic 3: Veröffentlichung und Konflikte](../initial-infrastructure/epics/03-Storage-und-Berichte.md#veröffentlichung-konflikte-und-retention) für Store und fertige Berichte umsetzen; Lock-Mechanik aus [Epic 1](../initial-infrastructure/epics/01-Eingaben-und-Host.md#lock-und-abbruch) für Scan und Entscheidung integrieren.
  - Nicht: Keine stille Konfliktauflösung, kein Warten auf belegte Locks und keine automatische Datenlöschung.
  - Abnahme: Tests belegen Commit-Punkt, Quelländerung vor Veröffentlichung, Abbruch/Prozessabsturz, verwaiste Berichte, verzweigte Ereignisketten und unveränderten gültigen Zustand bei Fehlern.
- [ ] **M2-T4 — Persistierten Review-Zyklus integrieren.**
  - Intention: Kern, Store und Berichte gemeinsam gegen die spezifizierte Zustandsfolge prüfen.
  - Scope: Die Test-Fixture aus [Epic 2](../initial-infrastructure/epics/02-Regel-und-Findings.md#startregel-und-test-fixierung) durch vollständige Scans und Entscheidungen mit dem echten Store führen; [Epic 3](../initial-infrastructure/epics/03-Storage-und-Berichte.md) als Ganzes prüfen.
  - Nicht: Noch keine vollständigen CLI-/MCP-Protokolltests.
  - Abnahme: IntegrationTests belegen `new → accepted/false-positive → reopened/updated → resolved`, unveränderte Unterdrückung, Snapshots, Berichte und dauerhaftes Urteil; der produktive Runner verwendet keinen No-op-Store mehr.
- [ ] **M2-A — Gesamtaudit nach Epic 3.**
  - Auftrag: Epic 3 im Zusammenhang mit Epic 1 und 2, dem Review-Zyklus und dem tatsächlichen Datenfluss prüfen; atomare Veröffentlichung und Konfliktverhalten besonders berücksichtigen.
  - Abnahme: Lesender `gpt-6-sol`-Subagent mit Reasoning `medium` berichtet dem Orchestrator belegte Findings oder explizit keine; Korrekturen und Restbefunde sind gemäß Audit-Regel festgehalten.

## M3 — Host-Schnittstellen (Epic 1 abschließen)

- [ ] **M3-T1 — CLI an den gemeinsamen Kern anschließen.**
  - Intention: `review` und `catalog` mit dem vereinbarten Prozessvertrag nutzbar machen.
  - Scope: [Epic 1: CLI](../initial-infrastructure/epics/01-Eingaben-und-Host.md#cli) mit `System.CommandLine`, produktiver Composition Root und echtem Runner/Store umsetzen.
  - Nicht: Keine zweite Analysepipeline, keine zusätzlichen CLI-Optionen und keine Testregel in der produktiven EXE.
  - Abnahme: Prozesstests prüfen JSON-Zeilen, Exit-Codes, stdout/stderr, Null-Finding-Run, Katalogdateien und unveränderte `ainetreview.json`.
- [ ] **M3-T2 — MCP-Tools und Operationsverwaltung.**
  - Intention: Dieselbe Analyse und dauerhafte Urteile über den lokalen Server anbieten.
  - Scope: [Epic 1: MCP-Tools](../initial-infrastructure/epics/01-Eingaben-und-Host.md#mcp-tools) über das offizielle MCP-SDK implementieren; `start_review`, `get_review_status` und `report_review` mit Polling, Token-Lebensdauer und frischem Store-Laden anbinden.
  - Nicht: Kein Daemon, kein eigenes MCP-Protokoll und kein `structuredContent`.
  - Abnahme: Adapter- und Prozesstests prüfen alle Statusvarianten, Fehlercodes, deduplizierte Starts, Neustart, Verdict-Persistenz und reines Protokoll-stdout.
- [ ] **M3-T3 — Parallelität, Stale-Prüfung und Fehlerpfade abschließen.**
  - Intention: CLI und MCP sollen unter gleichzeitiger Nutzung denselben sicheren Zustand sehen.
  - Scope: [Epic 1: Lock und Abbruch](../initial-infrastructure/epics/01-Eingaben-und-Host.md#lock-und-abbruch) und die [Stale-Prüfung von `report_review`](../initial-infrastructure/epics/01-Eingaben-und-Host.md#mcp-tools) an beiden Zugängen vollständig integrieren.
  - Nicht: Kein Warten auf Locks, keine Urteile zu deaktivierten oder aufgelösten Findings und keine unvollständigen Erfolgsmeldungen.
  - Abnahme: IntegrationTests belegen parallele CLI-/MCP-Prozesse, belegte Locks, Abbruch, geänderte Quellen/Optionen/Versionen sowie erfolgreiche und abgewiesene Urteile ohne beschädigte Runs.
- [ ] **M3-A — Gesamtaudit nach Epic 1.**
  - Auftrag: Epic 1 samt den inzwischen vollständigen Epic-2- und Epic-3-Pfaden aus Sicht beider Schnittstellen prüfen; Prozessgrenzen, Logging, Pfade und Parallelität einbeziehen.
  - Abnahme: Lesender `gpt-6-sol`-Subagent mit Reasoning `medium` berichtet dem Orchestrator belegte Findings oder explizit keine; Korrekturen und Restbefunde sind gemäß Audit-Regel festgehalten.

## M4 — Gesamtabnahme (Epic 4 abschließen)

- [ ] **M4-T1 — Testmatrix, CI und Ist-Dokumentation schließen.**
  - Intention: Jeden Vertrag und seine Fehlerfälle reproduzierbar nachweisen.
  - Scope: [Epic 4: Testebenen](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#testebenen) mit FastTests, kleinen IntegrationTests und CI-Gates vervollständigen; `docs/` für tatsächlich implementierte Bereiche aktualisieren.
  - Nicht: Kein neuer Produktumfang und kein Routine-Performance-Lauf pro Commit.
  - Abnahme: Build, FastTests und IntegrationTests laufen über die vorgesehenen Skripte; keine relevante Vertragslücke oder irreführende Ist-Dokumentation bleibt offen.
- [ ] **M4-T2 — Lasttest, Dogfooding und Produkt-DoD nachweisen.**
  - Intention: Den ersten nutzbaren Stand als Ganzes belegen.
  - Scope: Separaten Infrastrukturlasttest, Analyse der eigenen Solution, `catalog` und sämtliche Punkte der [Definition of Done](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#definition-of-done) prüfen.
  - Nicht: Keine fachliche Review-Regel, keine Archivierung von AiNetLinter und keine Aussage über spätere Regel-Performance.
  - Abnahme: Der separate Performance-Test erfüllt die in Epic 4 definierten Daten-, Zeit- und Speichergrenzen auf der geforderten Testmaschine; Dogfooding und alle Produkt-DoD-Punkte sind mit ausgeführten Checks belegt.
- [ ] **M4-A — Gesamtaudit nach Epic 4.**
  - Auftrag: Den vollständigen ersten Produktstand gegen Konzept, alle Epics, Tests, Dokumentation und Roadmap auditieren; auch übergreifende Architektur- oder Vertragsabweichungen melden.
  - Abnahme: Lesender `gpt-6-sol`-Subagent mit Reasoning `medium` berichtet dem Orchestrator belegte Findings oder explizit keine; Korrekturen, Restbefunde und der finale DoD-Status sind gemäß Audit-Regel festgehalten.
