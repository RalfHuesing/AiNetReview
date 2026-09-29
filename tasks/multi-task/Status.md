# Laufstand: Drei Review-Tasks

## Vorabprüfung vom 29.09.2026

Schritt 3 wurde noch nicht gestartet. Bei `HEAD 1c96537` war der Arbeitsbaum vor der Prüfung sauber; kein fachlicher Umsetzungspunkt ist abgeschlossen.

- `pwsh -File ./scripts/test-fast.ps1`: 181/181 bestanden.
- `pwsh -File ./scripts/build.ps1`: bestanden, 0 Warnungen und 0 Fehler.
- `pwsh -File ./scripts/test-integration.ps1`: zwei vollständige Läufe mit jeweils 91/92 bestandenen Tests. Zuerst scheiterte `HostProcessIntegrationTests.ProcessInvocation_WithValidAnalysisConfigPublishesOneCompleteReport` mit Host-Exitcode 4 statt 0. Im zweiten Lauf scheiterte `HostAdapterIntegrationTests.ReviewCommand_ProductionDeadCodeAnalysisPublishesRepeatedAndEmptyAudits` beim dritten Review-Durchlauf mit `REPORT_FAILED`. Dieser zweite Test scheiterte auch einmal isoliert; ein weiterer isolierter Lauf bestand. Der Hostprozess-Test bestand isoliert. Die zugrunde liegende Ausnahme und die Ursache der wechselnden Fehler sind noch nicht geklärt.
- Der Diagnoseversuch mit einer vorübergehenden Konsolenausgabe in `ReviewCommand` wurde vollständig zurückgenommen. Es bleibt keine Codeänderung aus dieser Prüfung.
- Die lokalen Audit-Profile und Ziel-Solutions für AiNetReview und AiNetLinter sind vorhanden. Das bestehende AiNetLinter-Profil hat jedoch `enabled: false`; beide vorhandenen Profile listen `code-size-candidates` noch nicht auf. C4 muss deshalb einen tatsächlich ausgeführten Audit mit passenden Defaults nachweisen und darf einen übersprungenen Profil-Lauf nicht als Stichprobe zählen.

## Nächster Punkt

[P0 der übergeordneten Roadmap](roadmap.md): Den vorbestehenden Integrationsfehler gezielt diagnostizieren und die normalen Gates stabilisieren. Keine wiederholten vollständigen Testläufe ohne neue Hypothese. Falls der Fehler danach offen, aber nachweislich unabhängig von den betroffenen A1–A3-Tests ist, dürfen diese Core-Slices mit dokumentierter Einschränkung beginnen; A4 bleibt bis zu grünen normalen Gates offen.

## Blocker und Restbefunde

Die IntegrationTests sind vor der fachlichen Umsetzung nicht verlässlich grün. Bis zur Klärung bleibt P0 offen; ein Fehler in den neuen Analysen lässt sich sonst nicht sauber von einem vorbestehenden Gate-Fehler unterscheiden. Eine belegte Unabhängigkeit erlaubt nur die Core-Slices A1–A3, nicht die Core-Abnahme oder den Start der Folgetasks.
