---
status: draft
---

# Dateibasierter Audit-Vergleich

## Intention

Mehrere Review-Analysen sollen bei wiederholten Audits ein vollständiges Gesamtbild liefern und zugleich die Prüfung geänderter Quelldateien erleichtern. Eine einfache Baseline aus Dateiständen dient als Vergleichspunkt, nicht als Liste akzeptierter Findings. Der Mensch und der Review-Agent entscheiden weiterhin anhand des Anwendungskontexts, was geändert, als Fehlalarm aus dem Arbeitsbericht entfernt oder bewusst zurückgestellt wird. Kein Signal ist für sich ein Defektbeweis oder ein automatischer Refactoring-Auftrag.

## Verifizierter Ausgangspunkt

- `ReviewRunner` sammelt und validiert die strukturierten Ergebnisse aller aktiven Analysen. `MarkdownReportWriter` veröffentlicht heute pro Lauf ein eigenes Verzeichnis mit `index.md` und Analyse-Dateien; ältere Läufe bleiben erhalten. Analyse-Ergebnisse enthalten noch keine veröffentlichte kurze Finding-ID oder Baseline.
- Der konfigurierte `outputDirectory` liegt im Projektbereich und ist standardmäßig `audit-reporting`. Dieses Repository ignoriert `audit-reporting/` per `.gitignore`; andere Audit-Ziele können einen anderen Ausgabepfad wählen. Der Produktlauf benötigt kein Git.
- `dead-code-candidates` bewertet Verwendungen in der gesamten geladenen Lösung; `method-control-flow-outliers` benutzt eine relative Projektauswahl. Ein neuer Befund kann daher in einer unveränderten Datei entstehen, nachdem sich andere Dateien geändert haben.

## Empfohlenes Modell (Draft)

1. **Analysen bleiben baselineblind.** Jede aktive Analyse gibt in jedem erfolgreichen Audit alle aktuellen Findings strukturiert an die zentrale Laufstufe zurück. Erst nach vollständiger Validierung werden Zusammenhänge zwischen Analysen bestimmt, Dateihashes berechnet und die beiden Berichtsansichten erstellt. Die Analysen erzeugen kein Markdown und keine Hashes.
2. **Baseline ist ein Dateistand.** Eine gemeinsame `baseline.json` liegt direkt im aufgelösten Ausgabeverzeichnis, neben den Laufverzeichnissen. Sie enthält für jede im konfigurierten Projekt analysierbare Quelldatei deren normalisierten Projektpfad und SHA-256, auch wenn die Datei keine Findings hat. Baseline-Erzeugung und Audit benutzen denselben Dateiumfang und dieselbe Hash-Bildung. Die zentrale Laufstufe berechnet jeden Dateihash nur einmal pro Aufruf. Fehlt die Baseline, gelten alle Dateien als neu. Der nächste Baseline-Aufruf ersetzt den gesamten gespeicherten Dateistand, einschließlich der Bereinigung inzwischen gelöschter Dateien.
3. **Zwei Ansichten desselben Laufs.** `all-findings/` enthält stets alle Findings. `changed-files/` enthält Findings, an denen mindestens eine gegenüber der Baseline neue oder geänderte Quelldatei beteiligt ist. Bei einem Cluster zählen alle Mitgliedsdateien; eine Änderung an einem Mitglied macht den gesamten Cluster sichtbar. Ohne Baseline sind beide Ansichten inhaltlich gleich. Jede Ansicht hat einen Index und Analyse-Dateien; eine Analyse ohne sichtbare Findings erhält dort keine Datei. Die vollständige Ansicht bleibt beim Bearbeiten der Änderungsansicht erhalten. Der Name `changed-files` beschreibt bewusst die Dateifilterung, nicht alle indirekten Wirkungen einer Änderung.
4. **Berichte sind Arbeitsmaterial.** Jede Zeile zeigt eine kurze, nur im Lauf eindeutige ID, die Analyse und das genaue Symbol beziehungsweise die Cluster-Mitglieder. Quellzeilennummern dienen nur zur Navigation im Snapshot, nicht als Teil der Symbolidentität. Index und Detailberichte zeigen Verbindungen zwischen Signalen am selben Symbol oder Cluster über Analysen und Quellorte hinweg. Der Agent darf Zeilen aus `changed-files/` nach der Kontextprüfung direkt entfernen; das ändert weder `all-findings/` noch die Baseline. Es gibt keinen EXE-Befehl zum Entfernen oder Klassifizieren einzelner Findings.
5. **Eigenständiger Baseline-Aufruf.** `--cmd baseline` erfasst direkt den aktuellen Dateistand des konfigurierten Projekts und schreibt `baseline.json` sicher als Ganzes. Der Befehl braucht keinen vorherigen Audit, keinen Bericht und keine Laufkennung; er führt keine Review-Analysen aus und erzeugt keinen Findings-Bericht. Er kann vor dem ersten oder nach beliebigen Audits aufgerufen werden. Schlägt er fehl, bleibt eine vorhandene Baseline erhalten. Ein normaler Audit liest die Baseline nur, wenn sie existiert, und verändert sie nicht. Die bestehende `review`-Aufrufform bleibt bestehen.

## Arbeitsablauf

1. Audit starten und `changed-files/` als Änderungsansicht öffnen. Für einen 360-Grad-Blick steht `all-findings/` immer daneben; der Index weist auf verwandte Signale hin.
2. Konkrete Findings im Anwendungskontext prüfen. Der Agent kann klare Fehlalarme oder bearbeitete Zeilen aus der Änderungsansicht entfernen. Mehrere Signale an einem Symbol verlangen eine gemeinsame Prüfung des Kontexts, aber nicht automatisch ein breites Refactoring.
3. Nach Codeänderungen einen normalen Audit wiederholen. Dadurch werden Quellorte, Findings und Dateihashes aus einem frischen Snapshot abgeleitet. Bewusst vertagte Findings bleiben in der vollständigen Ansicht sichtbar.
4. Wenn der aktuelle Dateistand künftig als Vergleichspunkt dienen soll, unabhängig von einem Bericht `--cmd baseline` aufrufen. Ein anschließender unveränderter Audit hat eine leere Änderungsansicht und weiterhin eine vollständige Ansicht. Derselbe Befehl kann auch vor dem allerersten Audit stehen.

**Warum zwei unabhängige Aufrufe?** `review` beobachtet und veröffentlicht; `--cmd baseline` speichert einen Dateistand. Die Reihenfolge ist frei. Der Nutzer entscheidet, wann ein neuer Vergleichspunkt entsteht, ohne einen vorherigen Report auswählen zu müssen.

**Drei Findings in `file.cs`:** Steht die Datei mit Hash `H0` in der Baseline und ändert sich zu `H1`, erscheinen A, B und C in `changed-files/`. Nach einer Behebung von B entsteht `H2`; ein neuer Audit zeigt dort A und C, sofern die Analysen sie weiterhin melden. Ein anschließender Aufruf von `--cmd baseline` speichert `H2`, auch ohne vorausgegangenen Audit. Der nächste unveränderte Audit zeigt in `changed-files/` keinen der beiden Befunde, in `all-findings/` weiterhin beide. Die Baseline macht keine Aussage, ob A und C einzeln akzeptiert wurden.

## Scope

### Muss

- Alle Analysen immer ohne Baseline-Einfluss vollständig ausführen und die vollständige Ansicht pro erfolgreichem Lauf veröffentlichen.
- Eine einfache Baseline für alle analysierbaren Quelldateien im Ausgabeverzeichnis halten; keine Baseline-Datei bedeutet identische vollständige und gefilterte Ansicht.
- Pro Lauf die beiden Berichtsverzeichnisse `changed-files/` und `all-findings/` mit konsistenten Indizes und Analyseberichten erzeugen.
- Run-lokale Finding-IDs, genaue Symbolangaben ohne Zeile als Identität, verknüpfte Signale und einen Gesamtblick im Index bereitstellen.
- Agenten die Änderungsberichte als Arbeitsmaterial bearbeiten lassen; den vollständigen Bericht und den Baseline-Dateistand dadurch nicht verändern.
- Die Baseline über einen eigenständigen `--cmd baseline`-Aufruf ohne vorausgesetzten Audit oder Laufkennung aus dem aktuellen Dateistand sicher veröffentlichen.

### Nicht

- In diesem Konzeptschritt Produktcode oder aktuelle Produktdokumentation ändern oder eine Roadmap erstellen.
- Findings als Build-Fehler ausgeben oder Code automatisch ändern.
- Entscheidungen pro Finding dauerhaft speichern, Markdown-Zeilen per EXE bearbeiten oder eine Git-Laufzeitabhängigkeit einführen.
- Aus mehreren Signalen automatisch einen Schweregrad oder Refactoring-Auftrag ableiten.

## Verifikation für die spätere Umsetzung

- Bei einem Audit ohne Baseline sind beide Ansichten inhaltlich gleich. Zwei unveränderte Audits ohne zwischenzeitlichen Baseline-Aufruf bleiben gleich; nach einem Baseline-Aufruf ist die Änderungsansicht des nächsten unveränderten Audits leer, die vollständige bleibt erhalten.
- Eine Änderung an einer Datei zeigt alle aktuellen Findings mit dieser Datei in der Änderungsansicht. Ein Cluster erscheint, wenn eine seiner Mitgliedsdateien geändert wurde. Die zentrale Stufe hasht dieselbe Datei nur einmal pro Lauf.
- Agentenänderungen an `changed-files/` beeinflussen weder `all-findings/` noch einen späteren Baseline-Aufruf. Ein fehlgeschlagener Audit oder Baseline-Aufruf verändert die bestehende Baseline nicht.
- `--cmd baseline` funktioniert ohne vorhandenen Bericht und erfasst auch Dateien ohne Findings. Anschließende Quelländerungen werden beim nächsten Audit über den Dateihash sichtbar.
- Zusammengehörige Signale sind über Analysen hinweg sichtbar; unverwandte Signale in derselben Datei werden nicht als ein Befund ausgegeben.

## Grenzen des Dateivergleichs

- `changed-files/` bedeutet Findings in neuen oder geänderten Dateien, nicht alle seit dem letzten Lauf neu entstandenen Findings. Eine gelöschte Verwendung kann zum Beispiel `dead-code-candidates` in einer unveränderten Datei neu auslösen; dieses Finding steht dann nur in `all-findings/`.
- Vertagte Findings erscheinen nach dem Baseline-Aufruf nur noch in `all-findings/`, bis ihre Datei sich ändert. Der Vergleichspunkt ist keine einzelne Ignore-Entscheidung.
