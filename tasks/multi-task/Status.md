# Laufstand: Drei Review-Tasks

## Vorabprüfung vom 29.09.2026

Schritt 3 wurde noch nicht gestartet. Bei `HEAD 1c96537` war der Arbeitsbaum vor der Prüfung sauber; kein fachlicher Umsetzungspunkt ist abgeschlossen.

- `pwsh -File ./scripts/test-fast.ps1`: 181/181 bestanden.
- `pwsh -File ./scripts/build.ps1`: bestanden, 0 Warnungen und 0 Fehler.
- `pwsh -File ./scripts/test-integration.ps1`: zwei vollständige Läufe mit jeweils 91/92 bestandenen Tests. Zuerst scheiterte `HostProcessIntegrationTests.ProcessInvocation_WithValidAnalysisConfigPublishesOneCompleteReport` mit Host-Exitcode 4 statt 0. Im zweiten Lauf scheiterte `HostAdapterIntegrationTests.ReviewCommand_ProductionDeadCodeAnalysisPublishesRepeatedAndEmptyAudits` beim dritten Review-Durchlauf mit `REPORT_FAILED`. Dieser zweite Test scheiterte auch einmal isoliert; ein weiterer isolierter Lauf bestand. Der Hostprozess-Test bestand isoliert. Die zugrunde liegende Ausnahme und die Ursache der wechselnden Fehler sind noch nicht geklärt.
- Der Diagnoseversuch mit einer vorübergehenden Konsolenausgabe in `ReviewCommand` wurde vollständig zurückgenommen. Es bleibt keine Codeänderung aus dieser Prüfung.
- Die lokalen Audit-Profile und Ziel-Solutions für AiNetReview und AiNetLinter sind vorhanden. Das bestehende AiNetLinter-Profil hat jedoch `enabled: false`; beide vorhandenen Profile listen `code-size-candidates` noch nicht auf. C4 muss deshalb einen tatsächlich ausgeführten Audit mit passenden Defaults nachweisen und darf einen übersprungenen Profil-Lauf nicht als Stichprobe zählen.

## Geprüfter Deaktivierungsversuch

Auf Nutzerwunsch wurden die zwei zuvor auffälligen Integrationstests probeweise mit sichtbarem xUnit-`Skip` deaktiviert. Der vollständige normale Lauf blieb rot: 89 bestanden, 2 übersprungen, 1 fehlgeschlagen. `MarkdownReportWriterPublicationTests.WriteAsync_PublishesUniqueConcurrentRunsAndPreservesEarlierRuns` scheiterte bei `MarkdownReportWriter.WriteAsync`, Zeile 94, mit `System.IO.IOException: Access to the path ... is denied` beim Verschieben des temporären Reportverzeichnisses. Das spricht für einen breiteren Fehlerbereich der Reportveröffentlichung; die genaue Ursache ist noch nicht belegt. Die zwei probeweisen Skips wurden vollständig zurückgenommen. Es ist kein Integrationstest dauerhaft deaktiviert.

## Nächster Punkt

[A1 der übergeordneten Roadmap](roadmap.md): Die Kontrollflussmessung mit betroffenen Tests umsetzen. Den breiteren vorbestehenden Reportpublikationsfehler bis A4 als bekannten Gate-Befund mitführen; keine wiederholten vollständigen Testläufe ohne neue Hypothese.

## Blocker und Restbefunde

Die IntegrationTests sind vor der fachlichen Umsetzung nicht verlässlich grün. A1–A3 können mit ihren betroffenen Tests bearbeitet werden. A4 und damit die Core-Abnahme und beide Folgetasks bleiben bis zu einem bestandenen normalen Integrations-Gate offen. Ein Fehler in den neuen Analysen muss gegen diesen vorbestehenden Befund abgegrenzt werden.
