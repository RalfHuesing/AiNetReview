---
status: ready
---

# Sequentielle Umsetzung der drei Review-Tasks

## Intention

Die drei vorbereiteten Tasks sollen autonom und in fester Reihenfolge möglichst weit abgeschlossen werden. Gemeinsame Core-Bausteine entstehen zuerst; die beiden Analysen nutzen anschließend deren tatsächlich verifizierte Verträge. Unabhängige Audits und begrenzte Korrekturrunden sollen Fehler früh sichtbar machen, ohne die Umsetzung in eine endlose Review-Schleife zu verwandeln. Der Stand muss nach einer Kontextkomprimierung oder Unterbrechung aus Repository-Dateien und Commits rekonstruierbar sein.

## Verbindliche Ausgangslage

1. [`core-code-metriken`](../core-code-metriken/Konzept.md) mit [Roadmap](../core-code-metriken/roadmap.md): `status: ready`, vier offene Punkte einschließlich Schlussaudit. Liefert `ControlFlowMetrics`, `CodeLineMetrics` und `TestFrameworkClassifier`.
2. [`analyse-test-abdeckung`](../analyse-test-abdeckung/Konzept.md) mit [Roadmap](../analyse-test-abdeckung/roadmap.md): `status: ready`, fünf offene Punkte einschließlich Schlussaudit. Benötigt `ControlFlowMetrics` und `TestFrameworkClassifier`.
3. [`analyse-groessen`](../analyse-groessen/Konzept.md) mit [Roadmap](../analyse-groessen/roadmap.md): `status: ready`, vier offene Punkte einschließlich Schlussaudit. Benötigt `ControlFlowMetrics` und `CodeLineMetrics`.

Die Roadmaps und Konzepte der drei Tasks bleiben ihre fachlichen Verträge. Ein abgehakter Punkt ist kein Ersatz für die Prüfung von Code, Tests und Diff. Dieser übergeordnete Task steuert nur Reihenfolge, Audits, Korrekturen, Commits und Wiederaufnahme. Er ändert keine Produktanforderung.

## Scope

### Muss

- Die Umsetzung erst nach einem eigenen Aufruf von `.agents/agent-workflow/03-orchestrierte-umsetzung.md` für `tasks/multi-task` starten. Schritt 2 wird ebenfalls nur auf eigenen Nutzeraufruf ausgeführt. Der Orchestrator implementiert keinen Produktionscode.
- Ausschließlich sequenziell arbeiten: genau ein aktiver Implementierungs-, Audit- oder Korrektur-Agent zur selben Zeit; vor dem nächsten Agenten dessen Abschluss und den Repository-Stand prüfen. Reihenfolge: Core, Testabdeckung, Größenanalyse. Vor jedem Folgetask den vollständigen Abschluss der Core-Roadmap und die benötigten Core-APIs im Code prüfen, wie es die beiden `ready`-Konzepte verlangen.
- Den Orchestrator mit `gpt-6-sol` und Reasoning `high`, Implementierungs- und Korrektur-Agenten mit `gpt-6-luna` und Reasoning `high` einsetzen.
- Nach jedem fachlichen Umsetzungspunkt Diff, Tests/Gates, Dokumentationsabgleich, Checkbox und Commit prüfen. Fehlende Abnahme im selben Punkt nacharbeiten lassen, bevor der nächste Punkt beginnt. Jede abgeschlossene, verifizierte Änderung atomar mit passenden Taskpfaden committen; keine fremden Änderungen aufnehmen, nicht pushen und keine Historie umschreiben.
- Zusätzliche unabhängige Audits an risikoreichen Übergängen vorsehen: nach der Kontrollfluss-Extraktion im Core-Task, nach dem semantischen Testpfad-Punkt im Testabdeckungs-Task und nach Klassen-/Dateikandidaten im Größen-Task. Den bestehenden Schlussaudit jeder abgeschlossenen Task-Roadmap als verbindlichen Task-Audit verwenden. Nach der Bearbeitung der erreichbaren Tasks einen weiteren taskübergreifenden Endaudit durchführen, der Verträge, gemeinsame APIs, Integration, Tests, Dokumentation und Commits zusammen betrachtet.
- Für jeden Audit `gpt-6-sol` mit Reasoning `medium` verwenden. Ein Audit liest und meldet konkrete Findings mit Fundstellen; ein separater Implementierungs-Agent behebt bestätigte Findings. Danach prüft ein neuer Audit den Stand. Pro Task gelten höchstens drei Audit–Fix-Runden insgesamt, einschließlich der Zwischenaudits; für den taskübergreifenden Endaudit gelten ebenfalls höchstens drei eigene Runden. Ohne Findings wird keine Korrekturrunde verbraucht. Diese ausdrückliche Vorgabe ersetzt für diesen Lauf die Ein-Runden-Grenze aus Schritt 3 des allgemeinen Workflows.
- Bei einer echten Sackgasse den betroffenen Agentenlauf und Task sofort abbrechen, Ursache, betroffenen Punkt, bereits versuchte Ansätze und nötige Entscheidung festhalten. Keine drei Runden ausschöpfen, wenn kein begründeter neuer Lösungsansatz vorliegt. Danach nur den nächsten Task in der festgelegten Reihenfolge beginnen, dessen Voraussetzungen tatsächlich erfüllt sind: Ein blockierter Core-Task sperrt beide Folgetasks gemäß deren `ready`-Konzepten; ein nur auf die Testabdeckung begrenzter Blocker lässt die Größenanalyse zu. Nicht kritische, belegte Restbefunde dürfen offen bleiben, wenn sie den jeweiligen `ready`-Vertrag und die Abnahme nicht verletzen; sie bleiben sichtbar und werden nicht als erledigt ausgegeben.
- `tasks/multi-task/Status.md` als knappen dauerhaften Laufstand führen: letzter abgeschlossener Punkt, zugehörige Commits, Audit-Ergebnisse und verbleibende Findings, bestandene Gates, Blocker und nächste ausführbare Checkbox. Ergebnisse der fachlichen Schlussaudits in den zugehörigen Roadmaps beziehungsweise deren `audit.md` festhalten. `Status.md` nach jedem Umsetzungspunkt, jeder Korrekturrunde und jeder Blockade aktualisieren und mit dem jeweiligen Slice committen; keine chronologischen Agentenprotokolle oder zusätzlichen Kopien der Konzepte anlegen. Wiederaufnahme erfolgt anhand dieser Dateien, der offenen Checkboxen und des Git-Stands, nicht allein aus dem Gesprächskontext.
- Nach den bearbeitbaren Tasks einen taskübergreifenden Endaudit über alle bis dahin entstandenen Änderungen und offenen Blockaden durchführen, sofern kein harter Blocker den gesamten Lauf sofort beendet. Am Ende die abgeschlossenen und offenen Punkte, alle relevanten Commit-Hashes, bestandene beziehungsweise fehlgeschlagene Gates und Restbefunde in Markdown und in der Abschlussmeldung nennen. Die taskübergreifende Abnahme nur bei tatsächlich erfüllten Verträgen und bestandenen Gates markieren.

### Nicht

- In diesem Konzeptschritt eine Roadmap erstellen, Agenten starten, Code ändern oder einen der drei Tasks umsetzen.
- Die fachlichen `ready`-Konzepte still ändern, zusätzliche Produktfeatures aufnehmen oder Audit-Findings pauschal als neue Anforderungen behandeln.
- Parallel laufende Agenten, unendliche Review-Schleifen, Sammelcommits ohne verifizierbaren Slice oder eine behauptete Fertigstellung trotz offenem Pflichtbefund.

## Verifikation des Gesamtlaufs

Die fachlichen Gates und Tests stehen in den drei Roadmaps. Vor jedem Taskabschluss bestätigt der Orchestrator, dass alle Muss-Punkte der jeweiligen Roadmap samt Schlussaudit erfüllt und belegt sind. Der Endaudit prüft außerdem die Wiederverwendung der Core-Bausteine in beiden Analysen, die unveränderten bestehenden Review-Verträge, den dokumentierten Ist-Stand und die Nachvollziehbarkeit der Commits. Eine spätere Kontextkomprimierung ist kein Abnahmenachweis; der Orchestrator liest den persistierten Stand und verifiziert kritische Aussagen erneut am Repository.
