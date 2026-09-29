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
2. **Baseline ist ein Dateistand.** Eine gemeinsame `baseline.json` liegt direkt im aufgelösten Ausgabeverzeichnis, neben den Laufverzeichnissen. Sie enthält pro analysierter C#-Quelldatei den normalisierten Projektpfad und einen SHA-256 des geladenen Quell-Snapshots, auch für Dateien ohne Findings. Die zentrale Laufstufe berechnet jeden Dateihash nur einmal pro Audit. Fehlt die Baseline, gelten alle Dateien als neu. Dateilöschungen verschwinden beim nächsten bewussten Checkpoint aus der Baseline. Eine Änderung der wirksamen Analyseeinstellungen oder Tool-Version macht den alten Vergleich ungültig; dann zeigt die Änderungsansicht zunächst den vollständigen Befundbestand.
3. **Zwei Ansichten desselben Laufs.** `ohne-baseline/` enthält stets alle Findings. `mit-baseline/` enthält Findings, an denen mindestens eine gegenüber der Baseline neue oder geänderte Quelldatei beteiligt ist. Bei einem Cluster zählen alle Mitgliedsdateien; eine Änderung an einem Mitglied macht den gesamten Cluster sichtbar. Ohne Baseline sind beide Ansichten inhaltlich gleich. Jede Ansicht hat einen Index und Analyse-Dateien; eine Analyse ohne sichtbare Findings erhält dort keine Datei. Die vollständige Ansicht bleibt beim Bearbeiten der Änderungsansicht erhalten.
4. **Berichte sind Arbeitsmaterial.** Jede Zeile zeigt eine kurze, nur im Lauf eindeutige ID, die Analyse und das genaue Symbol beziehungsweise die Cluster-Mitglieder. Quellzeilennummern dienen nur zur Navigation im Snapshot, nicht als Teil der Symbolidentität. Index und Detailberichte zeigen Verbindungen zwischen Signalen am selben Symbol oder Cluster über Analysen und Quellorte hinweg. Der Agent darf Zeilen aus `mit-baseline/` nach der Kontextprüfung direkt entfernen; das ändert weder `ohne-baseline/` noch die Baseline. Es gibt keinen EXE-Befehl zum Entfernen oder Klassifizieren einzelner Findings.
5. **Bewusster Checkpoint.** Ein normaler Audit liest die vorhandene Baseline, veröffentlicht beide Ansichten und überschreibt die Baseline nicht. Der Lauf bewahrt sein maschinenlesbares Dateimanifest getrennt von den bearbeitbaren Markdown-Dateien auf. Nach der Sichtung schreibt ein eigener leichter Baseline-Befehl alle Dateihashes dieses ausgewählten erfolgreichen Laufs als neuen Vergleichspunkt. Er führt keine Analysen erneut aus und prüft zuvor, dass der aktuelle Quellstand noch zu diesem Lauf passt. Ein abgebrochener Lauf oder fehlgeschlagener Checkpoint lässt die bisherige Baseline bestehen. Der Dateistand wird als Ganzes akzeptiert; einzelne ignorierte oder zurückgestellte Findings werden nicht in der Baseline gespeichert.

## Arbeitsablauf

1. Audit starten und `mit-baseline/` als Änderungsansicht öffnen. Für einen 360-Grad-Blick steht `ohne-baseline/` immer daneben; der Index weist auf verwandte Signale hin.
2. Konkrete Findings im Anwendungskontext prüfen. Der Agent kann klare Fehlalarme oder bearbeitete Zeilen aus der Änderungsansicht entfernen. Mehrere Signale an einem Symbol verlangen eine gemeinsame Prüfung des Kontexts, aber nicht automatisch ein breites Refactoring.
3. Nach Codeänderungen einen normalen Audit wiederholen. Dadurch werden Quellorte, Findings und Dateihashes aus einem frischen Snapshot abgeleitet. Bewusst vertagte Findings bleiben in der vollständigen Ansicht sichtbar.
4. Erst wenn der aktuelle Stand als neuer Vergleichspunkt dienen soll, den Baseline-Checkpoint für diesen frischen Lauf ausführen. Ein anschließender unveränderter Audit hat eine leere Änderungsansicht und weiterhin eine vollständige Ansicht.

**Warum nicht bei jedem Audit automatisch schreiben?** Ein unmittelbar zweiter Lauf wäre zwar leer, auch wenn der erste Bericht nie geprüft wurde. Neu entdeckte und bewusst vertagte Findings könnten so ohne Entscheidung aus der Änderungsansicht verschwinden. Der explizite Checkpoint trennt Beobachten von Akzeptieren und benötigt keinen zweiten Analysemodus.

**Drei Findings in `file.cs`:** Steht die Datei mit Hash `H0` in der Baseline und ändert sich zu `H1`, erscheinen A, B und C in `mit-baseline/`. Nach einer Behebung von B entsteht `H2`; ein neuer Audit zeigt dort A und C, sofern die Analysen sie weiterhin melden. Erst der Checkpoint setzt den Dateivergleich auf `H2`. Der nächste unveränderte Audit zeigt in `mit-baseline/` keinen der beiden Befunde, in `ohne-baseline/` weiterhin beide. Das ist ein Checkpoint für die ganze Datei, keine Aussage, dass A und C jeweils einzeln akzeptiert wurden.

## Scope

### Muss

- Alle Analysen immer ohne Baseline-Einfluss vollständig ausführen und die vollständige Ansicht pro erfolgreichem Lauf veröffentlichen.
- Eine einfache Baseline für alle analysierten C#-Quelldateien im Ausgabeverzeichnis halten; keine Baseline-Datei bedeutet identische vollständige und gefilterte Ansicht.
- Pro Lauf die beiden Berichtsverzeichnisse `mit-baseline/` und `ohne-baseline/` mit konsistenten Indizes und Analyseberichten erzeugen.
- Run-lokale Finding-IDs, genaue Symbolangaben ohne Zeile als Identität, verknüpfte Signale und einen Gesamtblick im Index bereitstellen.
- Agenten die Änderungsberichte als Arbeitsmaterial bearbeiten lassen; den vollständigen Bericht und den Baseline-Dateistand dadurch nicht verändern.
- Den Baseline-Checkpoint nach einem erfolgreichen, noch aktuellen Audit ausdrücklich auslösen und sicher veröffentlichen.

### Nicht

- In diesem Konzeptschritt Produktcode oder aktuelle Produktdokumentation ändern oder eine Roadmap erstellen.
- Findings als Build-Fehler ausgeben oder Code automatisch ändern.
- Entscheidungen pro Finding dauerhaft speichern, Markdown-Zeilen per EXE bearbeiten oder eine Git-Laufzeitabhängigkeit einführen.
- Aus mehreren Signalen automatisch einen Schweregrad oder Refactoring-Auftrag ableiten.

## Verifikation für die spätere Umsetzung

- Beim ersten Audit ohne Baseline sind beide Ansichten inhaltlich gleich. Ohne Checkpoint bleiben zwei unveränderte Audits gleich; nach Checkpoint ist die Änderungsansicht des nächsten unveränderten Audits leer, die vollständige bleibt erhalten.
- Eine Änderung an einer Datei zeigt alle aktuellen Findings mit dieser Datei in der Änderungsansicht. Ein Cluster erscheint, wenn eine seiner Mitgliedsdateien geändert wurde. Die zentrale Stufe hasht dieselbe Datei nur einmal pro Lauf.
- Agentenänderungen an `mit-baseline/` beeinflussen weder `ohne-baseline/` noch einen späteren Checkpoint. Ein fehlgeschlagener Audit oder Checkpoint verändert die bestehende Baseline nicht.
- Eine Änderung zwischen ausgewähltem Lauf und Checkpoint wird erkannt und verlangt einen neuen Audit. Geänderte Analyseeinstellungen oder Tool-Version führen zu einer vollständigen Änderungsansicht.
- Zusammengehörige Signale sind über Analysen hinweg sichtbar; unverwandte Signale in derselben Datei werden nicht als ein Befund ausgegeben.

## Arbeitsgedächtnis (nur Draft)

1. **Checkpoint statt automatischer Fortschreibung:** Empfehlung: Der Audit schreibt die Baseline nicht selbst; ein expliziter leichter Befehl setzt sie nach der Sichtung. Automatische Fortschreibung spart einen Befehl, lässt aber ungeprüfte Findings bei einem zweiten Lauf verschwinden.
2. **Grenze des Dateivergleichs:** `mit-baseline/` bedeutet Findings in neuen oder geänderten Dateien, nicht alle seit dem letzten Lauf neu entstandenen Findings. Eine gelöschte Verwendung kann zum Beispiel `dead-code-candidates` in einer unveränderten Datei neu auslösen; dieses Finding steht dann nur in `ohne-baseline/`. Soll die Änderungsansicht auch solche indirekt neuen Findings enthalten, braucht die Baseline zusätzlich Finding-Identitäten oder analyseabhängige Wirkungsbereiche.
3. **Vertagte Findings:** Mit einer pauschalen Dateibaseline erscheinen sie nach dem Checkpoint nur noch in `ohne-baseline/`, bis ihre Datei sich ändert. Das ist eine bewusste Bedeutung des Checkpoints, keine einzelne Ignore-Entscheidung.
