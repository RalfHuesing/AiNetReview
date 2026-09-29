# Roadmap: Sequentielle Umsetzung der drei Review-Tasks

Verbindlich ist [Konzept.md](Konzept.md). Jede ausführbare Umsetzung-Checkbox ohne Kinder ist genau eine Agent-Session. Ihre **Intention, ihr Scope, ihre Nicht-Ziele und ihre Abnahme** stehen ausschließlich im gleich bezeichneten Punkt der verlinkten Fach-Roadmap. Diese übergeordnete Checkbox wird erst geschlossen, wenn auch die Fach-Checkbox geschlossen, der Slice verifiziert, der Diff geprüft und der atomare Commit vorhanden ist. Der Orchestrator aktualisiert `Status.md` mit dem Ergebnis und der nächsten ausführbaren Checkbox. Parent-Checkboxen sind Aggregate und werden erst nach allen Kindern und ihrer Abnahme geschlossen.

Es läuft immer nur **ein** Agent. Orchestrator: `gpt-6-sol/high`; Umsetzung und Korrektur: `gpt-6-luna/high`; Audit: `gpt-6-sol/medium`. Audits melden belegte Findings; nur ein danach gestarteter Implementierungs-Agent ändert Code. Nach jedem Fix folgt ein erneuter unabhängiger Audit. Höchstens drei Review–Fix-Runden je Fach-Task insgesamt und drei beim Gesamtaudit; ohne begründeten neuen Ansatz endet ein blockierter Task früher. Diese Nutzervorgabe gilt hier auch dort, wo eine Fach-Roadmap oder der allgemeine Schritt-3-Workflow nur einen Korrekturpass nennt. Alle Ergebnisse, Restbefunde, Gates und Commit-Hashes stehen knapp in `Status.md`; kein Agentenprotokoll anlegen.

Für einen Umsetzungspunkt laufen die **betroffenen** Tests und die von seiner Fach-Roadmap geforderten Gates. Die vollständigen normalen FastTests- und IntegrationTests-Skripte laufen bei den ausdrücklich genannten Integrations- und Schlussabnahmen, nicht nach jedem Leaf aus Gewohnheit. Ein roter vollständiger Lauf wird anhand des betroffenen Tests untersucht; derselbe Gesamtlauf wird nicht bloß in der Hoffnung auf Grün wiederholt. Tests werden weder per `#if false` noch per Filter aus einer vorgeschriebenen Abnahme entfernt.

Bei einer dokumentierten harten Blockade bleibt die betreffende Checkbox offen. Der Orchestrator nimmt danach die erste **erreichbare** offene Checkbox: Ein nicht abgeschlossener Core-Task verhindert beide Folgetasks; eine nur auf Testabdeckung begrenzte Blockade verhindert die Größenanalyse nicht. Ein fehlgeschlagener Agentenlauf wird nicht ohne geänderten Ansatz wiederholt. Die Fachkonzepte und Repository-Regeln bleiben maßgeblich; keine Produktanforderung wird hier neu entschieden.

## A. Gemeinsame Core-Bausteine

- [ ] **Core-Task abgeschlossen** — Aggregat aller folgenden Core-Punkte; erst nach abgeschlossener [Core-Roadmap](../core-code-metriken/roadmap.md), verifizierten APIs, Schlussaudit und dokumentierten Commits schließen.
  - [x] **A1 — Kontrollflussmessung extrahieren** — [Core-Roadmap, Punkt 1](../core-code-metriken/roadmap.md) vollständig ausführen und abnehmen.
  - [x] **A1-Audit — bestehendes Analyseverhalten prüfen**
    - Intention: Die verhaltensgleiche Extraktion prüfen, bevor weitere Verbraucher auf der API aufbauen.
    - Scope: Unabhängig `ControlFlowMetrics`-Vertrag, Regressionstests, Kandidaten, Evidenz, Descriptor und Behavior-Version gegen [Core-Konzept](../core-code-metriken/Konzept.md) und den A1-Diff prüfen; konkrete Findings mit Fundstellen in `Status.md` festhalten und nötige Korrekturen sequenziell nachprüfen.
    - Nicht: Neue Entscheidungsregeln oder Folgetask-Code einführen.
    - Abnahme: Kein offener Pflichtbefund zur Extraktion; akzeptierte Restbefunde und verwendete Korrekturrunden sind dokumentiert, der geprüfte Commit ist benannt.
  - [x] **A2 — Codezeilenmessung ergänzen** — [Core-Roadmap, Punkt 2](../core-code-metriken/roadmap.md) vollständig ausführen und abnehmen.
  - [x] **A3 — Test-Roots klassifizieren** — [Core-Roadmap, Punkt 3](../core-code-metriken/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **A4 — Core-Schlussaudit** — [Core-Roadmap, Punkt 4](../core-code-metriken/roadmap.md) mit einem unabhängigen Audit-Agenten ausführen; volle Gates und alle Core-Verträge prüfen. Falls die in [Status.md](Status.md) belegten vorbestehenden Integrationsfehler noch auftreten, gezielt die zugrunde liegende Ausnahme ermitteln und einen reproduzierbaren Defekt nur mit Rot-vor-Grün-Test beheben; keine bloßen Wiederholungsläufe. Ohne begründeten neuen Ansatz den Blocker dokumentieren und A4 offen lassen. Die Zahl der Korrekturrunden aus A1-Audit zählt zum Core-Limit. Audit-Ergebnis und etwaige Korrekturen dokumentieren und committen.

## B. Fehlende Testevidenz

Start nur nach geschlossenem Core-Aggregat und Prüfung von `ControlFlowMetrics.Measure` und `TestFrameworkClassifier.IsActiveTestRoot` im Code.

- [ ] **Testabdeckungs-Task abgeschlossen** — Aggregat aller folgenden Punkte; bei lokaler harter Blockade offen lassen und nach Dokumentation mit C fortfahren.
  - [ ] **B1 — Kandidaten bestimmen** — [Testabdeckungs-Roadmap, Punkt 1](../analyse-test-abdeckung/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **B2 — Semantische Testpfade bestimmen** — Aggregat aus [Testabdeckungs-Roadmap, Punkt 2a und 2b](../analyse-test-abdeckung/roadmap.md); erst nach beiden Slices und der kombinierten Abnahme schließen.
    - [ ] **B2a — Semantischen Graphen aufbauen** — [Testabdeckungs-Roadmap, Punkt 2a](../analyse-test-abdeckung/roadmap.md) vollständig ausführen und abnehmen.
    - [ ] **B2b — Testpfade klassifizieren** — [Testabdeckungs-Roadmap, Punkt 2b](../analyse-test-abdeckung/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **B2-Audit — Pfade und Unsicherheit prüfen**
    - Intention: Fehler im Call-Graph und unberechtigte Aussagen über Testabdeckung vor der Veröffentlichung entdecken.
    - Scope: Unabhängig Roots, Pfadpriorität, deterministische kürzeste Pfade, Unsicherheit und Fehlerfälle gegen [Testabdeckungs-Konzept](../analyse-test-abdeckung/Konzept.md), B1/B2-Code und Tests prüfen; Findings und gegebenenfalls sequenzielle Fix-/Nachaudit-Runden in `Status.md` festhalten.
    - Nicht: Laufzeitabdeckung behaupten oder Framework-Erkennung duplizieren.
    - Abnahme: Kein offener Pflichtbefund zu B1/B2; geprüfte Commits, akzeptierte Restbefunde und verbrauchte Runden sind dokumentiert.
  - [ ] **B3 — Findings und Optionen ergänzen** — [Testabdeckungs-Roadmap, Punkt 3](../analyse-test-abdeckung/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **B4 — Host und Berichte integrieren** — [Testabdeckungs-Roadmap, Punkt 4](../analyse-test-abdeckung/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **B5 — Testabdeckungs-Schlussaudit** — [Testabdeckungs-Roadmap, Punkt 5](../analyse-test-abdeckung/roadmap.md) mit einem unabhängigen Audit-Agenten ausführen; alle Verträge, Berichtsansichten und Gates prüfen. Die Runden aus B2-Audit zählen zum Task-Limit. Audit-Ergebnis und etwaige Korrekturen dokumentieren und committen.

## C. Größenanalyse

Start nur nach geschlossenem Core-Aggregat und Prüfung von `ControlFlowMetrics.Measure` und `CodeLineMetrics` im Code. Eine dokumentierte B-Blockade ändert diese Voraussetzung nicht.

- [ ] **Größen-Task abgeschlossen** — Aggregat aller folgenden Punkte; nur bei erfüllter Fach-Roadmap samt realer Stichprobe schließen.
  - [ ] **C1 — Membergrößen messen** — [Größen-Roadmap, Punkt 1](../analyse-groessen/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **C2 — Klassen und Dateien messen** — [Größen-Roadmap, Punkt 2](../analyse-groessen/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **C2-Audit — Aggregation und Dateimaße prüfen**
    - Intention: Fehler bei `partial`-Teilen, verschachtelten Typen und Dateigrenzen vor der Registrierung entdecken.
    - Scope: Unabhängig Mess-APIs, Kandidaten, Auswahlpfade, Evidenz und `changed-files/` gegen [Größen-Konzept](../analyse-groessen/Konzept.md), C1/C2-Code und Tests prüfen; Findings und gegebenenfalls sequenzielle Fix-/Nachaudit-Runden in `Status.md` festhalten.
    - Nicht: Core-Metriken oder festgelegte Schwellenwerte still ändern.
    - Abnahme: Kein offener Pflichtbefund zu C1/C2; geprüfte Commits, akzeptierte Restbefunde und verbrauchte Runden sind dokumentiert.
  - [ ] **C3 — Analyse und Bericht integrieren** — [Größen-Roadmap, Punkt 3](../analyse-groessen/roadmap.md) vollständig ausführen und abnehmen.
  - [ ] **C4 — Größen-Schlussaudit und Stichprobe** — [Größen-Roadmap, Punkt 4](../analyse-groessen/roadmap.md) mit einem unabhängigen Audit-Agenten ausführen; volle Gates und die read-only Stichprobe an AiNetReview und AiNetLinter prüfen. Vor der Stichprobe sicherstellen, dass das tatsächlich verwendete lokale Audit-Profil aktiviert ist und `code-size-candidates` mit den Konzept-Defaults enthält; falls nötig ein temporäres Profil im AiNetReview-Repository verwenden, ohne das AiNetLinter-Repository oder bestehende lokale Profile zu ändern. Ein wegen `enabled: false` übersprungener Lauf ist kein Nachweis. Die Runden aus C2-Audit zählen zum Task-Limit. Audit-Ergebnis und etwaige Korrekturen dokumentieren und committen.

## Gesamtabnahme

- [ ] **Endaudit über den erreichten Stand**
  - Intention: Prüfen, ob die drei Taskergebnisse zusammen den freigegebenen Verträgen entsprechen und der Lauf nachvollziehbar abgeschlossen oder begründet teilabgeschlossen ist.
  - Scope: Nach allen erreichbaren Punkten einen unabhängigen Audit-Agenten die gemeinsamen Core-APIs, beide Verbraucher, bestehende Review-Verträge, Tests/Gates, Doku, Checkboxen, Restbefunde und Commits gegen [Konzept.md](Konzept.md) und die drei Fachkonzepte prüfen lassen. Bestätigte Pflichtbefunde in höchstens drei eigenen sequenziellen Fix-/Nachaudit-Runden bearbeiten; `Status.md` mit abgeschlossenem und offenem Umfang aktualisieren und committen.
  - Nicht: Einen blockierten Fach-Task als erledigt markieren, neue Features aufnehmen oder denselben aussichtslosen Fix wiederholen.
  - Abnahme: Für abgeschlossene Tasks sind Verträge und Gates belegt; blockierte Punkte und nicht kritische Restbefunde sind mit Gründen sichtbar. Die Checkbox nur schließen, wenn der Audit des tatsächlich erreichten Stands abgeschlossen ist. Bei einem harten Blocker, der den gesamten Lauf sofort beendet, bleibt sie offen und `Status.md` hält den Grund fest.
