---
status: draft
---

# Initial-Infrastruktur umsetzen

## Intention

Die bereits spezifizierte [Initial-Infrastruktur](../initial-infrastructure/README.md) soll in AiNetReview vollständig und überprüfbar umgesetzt werden. Dieses Task-Verzeichnis beschreibt die Ausführungsreihenfolge und den Audit-Prozess. Die Produktverträge bleiben ausschließlich im [AiNetReview-Konzept](../initial-infrastructure/AiNetReview-Konzept.md) und seinen vier [Epics](../initial-infrastructure/README.md); die ursprüngliche [Roadmap](../initial-infrastructure/roadmap.md) bleibt deren grobe Fortschrittsübersicht.

## Scope

### Muss

- Die Umsetzung erfüllt das [Produkt-DoD](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#definition-of-done) und die Verträge aus [Epic 1](../initial-infrastructure/epics/01-Eingaben-und-Host.md), [Epic 2](../initial-infrastructure/epics/02-Regel-und-Findings.md), [Epic 3](../initial-infrastructure/epics/03-Storage-und-Berichte.md) und [Epic 4](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md). Die neue Roadmap zerlegt diese Arbeit nach technischen Abhängigkeiten; sie ersetzt keinen Vertrag.
- Jeder ausführbare Punkt prüft vor Änderungen den Ist-Stand, passende getestete AiNetLinter-Referenzen gemäß [Epic 4](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#ainetlinter-als-referenz), die betroffenen Repo-Regeln und die zugehörigen Tests. Geänderter Ist-Stand wird erst nach Verifikation in `docs/` beschrieben; der Fortschritt wird in dieser und der ursprünglichen Roadmap abgeglichen.
- Nach Abschluss jedes der vier Epics prüft ein **lesender Audit-Subagent** mit `gpt-6-sol` und Reasoning `medium` den erreichten Stand im Gesamtzusammenhang von Konzept, allen Epics, Anwendung und Tests. Er beschränkt sich nicht auf den zuletzt bearbeiteten Diff. Er meldet belegte Findings mit Fundstellen an den Orchestrator. Der Orchestrator bewertet sie und entscheidet über Korrektur, dokumentierte Nicht-Aktion oder Stopp bei einem offenen Produkt-Fork. Ein Audit-Finding ist kein automatischer Refactoring-Auftrag.
- Die spätere Umsetzung verwendet den [orchestrierten Schritt 3](../../.agents/agent-workflow/03-orchestrierte-umsetzung.md) erst nach gesondertem Nutzeraufruf. Konzept und Roadmap werden jetzt erstellt; Code wird in diesem Schritt nicht geändert.

### Nicht

- Keine neuen Produktverträge oder fachlichen Review-Regeln über `template-noop` hinaus; die `FixtureFindingRule` bleibt ausschließlich in Tests.
- Keine Änderung am AiNetLinter-Repository, keine Ablösung oder Archivierung von AiNetLinter und keine automatische Behebung von Review-Findings.
- Keine Implementierung, Tests, Paketänderungen oder Produkt-Dokumentation im aktuellen Planungsschritt.

## Verifikation

Die [Roadmap](roadmap.md) muss alle vier Epics und das Produkt-DoD auf ausführbare, geordnete Punkte zurückführen, nach jedem abgeschlossenen Epic einen Audit-Punkt enthalten und ohne Duplizieren der Vertragsdetails auf die maßgeblichen Stellen verweisen. In der späteren Umsetzung gelten die Testebenen und Abnahmekriterien aus [Epic 4](../initial-infrastructure/epics/04-Umsetzung-und-Abnahme.md#testebenen) sowie die [Repo-Verifikationsregel](../../.agents/rules/04-verification.mdc).
