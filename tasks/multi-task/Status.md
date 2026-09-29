# Laufstand: Drei Review-Tasks

## Vorabprüfung vom 29.09.2026

Schritt 3 wurde noch nicht gestartet. Bei `HEAD 1c96537` war der Arbeitsbaum vor der Prüfung sauber; kein fachlicher Umsetzungspunkt ist abgeschlossen.

- `pwsh -File ./scripts/test-fast.ps1`: 181/181 bestanden.
- `pwsh -File ./scripts/build.ps1`: bestanden, 0 Warnungen und 0 Fehler.
- `pwsh -File ./scripts/test-integration.ps1`: zwei vollständige Läufe mit jeweils 91/92 bestandenen Tests. Zuerst scheiterte `HostProcessIntegrationTests.ProcessInvocation_WithValidAnalysisConfigPublishesOneCompleteReport` mit Host-Exitcode 4 statt 0. Im zweiten Lauf scheiterte `HostAdapterIntegrationTests.ReviewCommand_ProductionDeadCodeAnalysisPublishesRepeatedAndEmptyAudits` beim dritten Review-Durchlauf mit `REPORT_FAILED`. Dieser zweite Test scheiterte auch einmal isoliert; ein weiterer isolierter Lauf bestand. Der Hostprozess-Test bestand isoliert. Die zugrunde liegende Ausnahme und die Ursache der wechselnden Fehler sind noch nicht geklärt.
- Der Diagnoseversuch mit einer vorübergehenden Konsolenausgabe in `ReviewCommand` wurde vollständig zurückgenommen. Es bleibt keine Codeänderung aus dieser Prüfung.
- Die lokalen Audit-Profile und Ziel-Solutions für AiNetReview und AiNetLinter sind vorhanden. Beide Profile haben nach der Nutzeränderung `enabled: false`; beide listen `code-size-candidates` noch nicht auf. Das beeinflusst die normalen IntegrationTests nicht, weil deren Skript `Category=Audit` ausschließt. C4 muss später einen tatsächlich ausgeführten Audit mit passenden Defaults nachweisen und darf einen übersprungenen Profil-Lauf nicht als Stichprobe zählen.

## Geprüfter Deaktivierungsversuch

Auf Nutzerwunsch wurden die zwei zuvor auffälligen Integrationstests probeweise mit sichtbarem xUnit-`Skip` deaktiviert. Der vollständige normale Lauf blieb rot: 89 bestanden, 2 übersprungen, 1 fehlgeschlagen. `MarkdownReportWriterPublicationTests.WriteAsync_PublishesUniqueConcurrentRunsAndPreservesEarlierRuns` scheiterte bei `MarkdownReportWriter.WriteAsync`, Zeile 94, mit `System.IO.IOException: Access to the path ... is denied` beim Verschieben des temporären Reportverzeichnisses. Das spricht für einen breiteren Fehlerbereich der Reportveröffentlichung; die genaue Ursache ist noch nicht belegt. Die zwei probeweisen Skips wurden vollständig zurückgenommen. Es ist kein Integrationstest dauerhaft deaktiviert.

## Nächster Punkt

[A2 — Codezeilenmessung ergänzen](roadmap.md) ist die nächste ausführbare Checkbox.

## A1 — Kontrollflussmessung extrahiert

- Ausgangsstand vor Edits: `HEAD 5868d1ead48d99f50bf4ed73eb14a1cb165eae38`, Arbeitsbaum sauber.
- Implementiert `ControlFlowMetrics.Measure` mit unverändertem Entscheidungs-Walker, Validierung von Block-/Expression-Knoten und unveränderlichem Ergebnis; `MethodControlFlowOutliersAnalysis` nutzt die API. Kandidaten, Messwerte, tiefste Evidenz, Descriptor und Behavior-Version bleiben erhalten.
- Dokumentationsabgleich: `docs/review/findings.md`, `docs/README.md` und `docs/development/adding-review-analyses.md` geprüft. Die vorhandene Beschreibung stimmt mit dem extrahierten Verhalten überein; keine Current-State-Doku musste geändert werden.
- Verifikation: fokussierte FastTests 32/32 bestanden; gezielter `MethodControlFlowOutliersIntegrationTests`-Test bestanden; Build-Gate mit 0 Warnungen und 0 Fehlern bestanden; `git diff --check` bestanden.
- Vollständiger IntegrationTests-Lauf: 91/92 bestanden. `HostAdapterIntegrationTests.ReviewCommand_ProductionDuplicateCodeAnalysisPublishesCurrentCrossProjectClusters` meldete `REPORT_FAILED` beim Publizieren des Reports. Der gezielte isolierte Wiederholungslauf dieses Tests bestand. Kein A1-relevanter Fehler reproduziert; der flüchtige Reportpublikationsbefund bleibt bis A4 sichtbar und die vollständige Suite wird ohne neue Hypothese nicht wiederholt.
- A1-Commit: `10f1adaaa1f0617a9208a2a83e4f24b235d70b68` (`feat(core): extract control flow metrics`).

## Blocker und Restbefunde

Die IntegrationTests sind vor der fachlichen Umsetzung nicht verlässlich grün. A1–A3 können mit ihren betroffenen Tests bearbeitet werden. A4 und damit die Core-Abnahme und beide Folgetasks bleiben bis zu einem bestandenen normalen Integrations-Gate offen. Ein Fehler in den neuen Analysen muss gegen diesen vorbestehenden Befund abgegrenzt werden.

## A1-Audit, Runde 1

- Geprüft: `10f1ada7cb085ed92aaee555c877ca11e7835a22` und der reine Nachtrag `5c6faa9420b79b48d8dd98318b3a6556b0725121`. Der `DecisionVisitor` wurde ohne Regel- oder Reihenfolgeänderung nach `ControlFlowMetrics` verschoben; Kandidatenauswahl, Perzentile, Mindestwerte, Evidenzbildung, Descriptor und Behavior-Version blieben im Verbraucher unverändert. Die neuen Tests prüfen Entscheidungsformen, `else if`, Bedingungen, Switch-Abschnitte, Gleichstand, Expression-Bodies, verschachtelte Funktionen und ungültige Argumente. Die bestehenden Analyse- und Integrationstests bleiben aktiv. Die in A1 dokumentierten fokussierten Gates sind belegt.
- Korrektur: `ControlFlowMetricsTests.Measure_VisitsDecisionsInsideSwitchExpressionArmsAtSuccessiveLevels` prüft nun für eine bedingte Expression in einem Switch-Expression-Arm `DecisionCount=3`, `DecisionConstructCount=2`, `MaxDecisionNesting=2` und den konkreten Knoten als `DeepestDecision`. Kein Produktionscode geändert.
- Korrektur-Gates: fokussierter neuer FastTest 1/1 und gesamte `ControlFlowMetricsTests`-Klasse 20/20 bestanden; gezielter `MethodControlFlowOutliersIntegrationTests`-Lauf 1/1 bestanden; `scripts/build.ps1` mit 0 Warnungen und 0 Fehlern bestanden; `git diff --check` bestanden. `docs/review/findings.md`, `docs/README.md` und `docs/development/adding-review-analyses.md` geprüft; der Test-only-Fix ändert keinen dokumentierten Ist-Vertrag.
- Der bereits vor A1 wechselnd fehlschlagende Report-Publikationspfad ist kein belegter A1-Regressionsbefund. Der volle A1-Integrationslauf blieb mit 91/92 rot; der betroffene Test bestand isoliert. Ohne neue Hypothese wurde die komplette Suite nicht erneut gestartet. Das vollständige Integrations-Gate bleibt für A4 offen.
- Runde 1 von höchstens 3 endete mit einem Testabdeckungs-Finding und einem Korrektur-Commit. Der vollständige IntegrationTests-Lauf bleibt für A4 offen.

## A1-Nachaudit, Runde 2

- Geprüfte Commits: `10f1ada7cb085ed92aaee555c877ca11e7835a22`, `5c6faa9420b79b48d8dd98318b3a6556b0725121` und `bf70072cfac2175ee2e987a90c027e95e85966dc`. Der neue FastTest prüft die innere `?:`-Entscheidung im Switch-Expression-Arm mit Zählwerten, Tiefe und Knotenidentität der tiefsten Entscheidung. Das Finding aus Runde 1 ist damit belegt behoben.
- Stichprobe: Der verschobene `DecisionVisitor` ist gegenüber dem Vorzustand unverändert; der Verbraucher ersetzt ausschließlich die Messwertquelle. Kandidatenfilter, Perzentile, Auswahl, Evidenz-Token, Descriptor-Text und Behavior-Version 1 bleiben unverändert. Die FastTests decken die übrigen Entscheidungsformen, `else if`, Gleichstand, Expression-Bodies, Ausschlüsse und Fehlargumente ab; Analyse- und Integrationstests blieben aktiv.
- Unabhängig erneut ausgeführt: `ControlFlowMetricsTests` und `MethodControlFlowOutliersAnalysisTests` zusammen 33/33 bestanden. Die gezielte Integration, der warnungsfreie Build und `git diff --check` sind für A1 und die Korrektur in den vorherigen Abschnitten belegt. Kein offener Pflichtbefund zur A1-Extraktion; A1-Audit abgenommen. Verbraucht: zwei Audit-Runden und eine Korrekturrunde von höchstens drei Core-Audit–Fix-Runden.
- Der vor A1 beobachtete flüchtige Report-Publikationsfehler und das offene vollständige IntegrationTests-Gate bleiben für A4 sichtbar; ohne neue Hypothese wurde die vollständige Suite nicht wiederholt.
