---
status: draft
---

# Dateibasierter Audit-Vergleich

## Intention

Mehrere Review-Analysen sollen bei wiederholten Audits ein vollständiges Gesamtbild liefern und zugleich die Prüfung geänderter Quelldateien erleichtern. Eine einfache Baseline aus Dateiständen dient als Vergleichspunkt, nicht als Liste akzeptierter Findings. Der Mensch und der Review-Agent entscheiden weiterhin anhand des Anwendungskontexts, was geändert, als Fehlalarm aus dem Arbeitsbericht entfernt oder bewusst zurückgestellt wird. Kein Signal ist für sich ein Defektbeweis oder ein automatischer Refactoring-Auftrag.

## Verifizierter Ausgangspunkt

- `ReviewRunner` sammelt und validiert die strukturierten Ergebnisse aller aktiven Analysen. `MarkdownReportWriter` veröffentlicht heute pro Lauf ein eigenes Verzeichnis mit `index.md` und Analyse-Dateien; ältere Läufe bleiben erhalten. Eine Baseline ist noch nicht implementiert.
- Der konfigurierte `outputDirectory` liegt im Projektbereich und ist standardmäßig `audit-reporting`. Dieses Repository ignoriert `audit-reporting/` per `.gitignore`; andere Audit-Ziele können einen anderen Ausgabepfad wählen. Der Produktlauf benötigt kein Git.
- `dead-code-candidates` bewertet Verwendungen in der gesamten geladenen Lösung; `method-control-flow-outliers` benutzt eine relative Projektauswahl. Ein neuer Befund kann daher in einer unveränderten Datei entstehen, nachdem sich andere Dateien geändert haben.

## Empfohlenes Modell (Draft)

1. **Analysen bleiben baselineblind.** Jede aktive Analyse gibt in jedem erfolgreichen Audit alle aktuellen Findings strukturiert an die zentrale Laufstufe zurück. Erst nach vollständiger Validierung werden Zusammenhänge zwischen Analysen bestimmt, Dateihashes berechnet und die beiden Berichtsansichten erstellt. Die Analysen erzeugen kein Markdown und keine Hashes.
2. **Baseline ist ein Dateistand.** Eine gemeinsame `baseline.json` liegt direkt im aufgelösten Ausgabeverzeichnis, neben den Laufverzeichnissen. Sie enthält für jede im konfigurierten Projekt analysierbare Quelldatei deren normalisierten Projektpfad und SHA-256, auch wenn die Datei keine Findings hat. Baseline-Erzeugung und Audit benutzen denselben Dateiumfang und dieselbe Hash-Bildung. Die zentrale Laufstufe berechnet jeden Dateihash nur einmal pro Aufruf. Fehlt die Baseline, gelten alle Dateien als neu. Der nächste Baseline-Aufruf ersetzt den gesamten gespeicherten Dateistand, einschließlich der Bereinigung inzwischen gelöschter Dateien.
3. **Zwei Ansichten, ein Einstieg.** `all-findings/` enthält stets alle Findings. `changed-files/` enthält Findings, an denen mindestens eine gegenüber der Baseline neue oder geänderte Quelldatei beteiligt ist. Bei einem Cluster zählen alle Mitgliedsdateien; eine Änderung an einem Mitglied macht den gesamten Cluster sichtbar. Ohne Baseline sind beide Ansichten inhaltlich gleich. Ein `index.md` im Laufverzeichnis erklärt beide Ansichten und verlinkt ihre Analyse-Dateien. Eine Analyse ohne sichtbare Findings erhält in dieser Ansicht keine Datei. `all-findings/` bleibt beim Bearbeiten von `changed-files/` erhalten.
4. **Berichte sind Arbeitsmaterial.** Jede Finding-Zeile zeigt das genaue Symbol beziehungsweise die Cluster-Mitglieder und ihren Quellort. Quellzeilennummern dienen zur Navigation im Lauf-Snapshot. Wenn andere Analysen dasselbe Symbol oder ein Cluster-Mitglied melden, verweist jede betroffene Finding-Zeile auf deren Bericht und Quellort. So kann ein Agent die Signale vor einer Entscheidung zusammen betrachten. Der Agent darf nach Kontextprüfung Zeilen aus `changed-files/` direkt entfernen und hält dabei Dateien und Links im Index konsistent. Eine Finding-ID und ein EXE-Befehl zum Bearbeiten einzelner Zeilen sind nicht nötig.
5. **Eigenständiger Baseline-Aufruf.** `--cmd baseline` erfasst direkt den aktuellen Dateistand des konfigurierten Projekts und schreibt `baseline.json` sicher als Ganzes. Der Befehl braucht keinen vorherigen Audit, keinen Bericht und keine Laufkennung; er führt keine Review-Analysen aus und erzeugt keinen Findings-Bericht. Er kann vor dem ersten oder nach beliebigen Audits aufgerufen werden. Schlägt er fehl, bleibt eine vorhandene Baseline erhalten. Ein normaler Audit liest die Baseline nur, wenn sie existiert, und verändert sie nicht. Die bestehende `review`-Aufrufform bleibt bestehen.

## `index.md` als Einstieg für den Review-Agenten

Die `index.md` im Laufverzeichnis enthält Links auf beide Ansichten und erklärt knapp: `changed-files/` zeigt Findings an neuen oder geänderten Quelldateien gegenüber der optionalen Baseline; `all-findings/` zeigt immer alle Findings. Ohne Baseline sind beide Ansichten gleich. Der Filter ist dateibasiert und ersetzt den Blick auf mögliche indirekte Folgen in anderen Dateien nicht. Analyse-Dateien mit Findings sind aus dem Index direkt erreichbar.

Sie enthält außerdem einen **konkreten, ausführbaren** Befehl zum Erzeugen einer neuen Baseline aus dem jetzigen Projektstand: den absoluten Pfad zur tatsächlich verwendeten AiNetReview-EXE, `--cmd baseline` und die nötigen Parameter für dasselbe Zielprojekt. Pfade werden für die angezeigte Shell korrekt gequotet. Es erscheinen keine Platzhalter, geratenen Installationspfade oder Verweise auf den gerade erzeugten Report, denn der Baseline-Befehl funktioniert unabhängig davon. Der Text erklärt, dass dieser Befehl den Vergleichspunkt für alle Quelldateien neu setzt.

Der allgemeine Agentenhinweis soll sinngemäß lauten:

> These findings are review signals, not proven defects or automatic change requests. Read the target repository's applicable instructions and relevant design documents. Consider the behavior of the application as a whole, including contracts, callers, tests, and related findings across analyses. First remove only clear false positives from `changed-files/` and leave uncertain cases for review. Then work through the remaining findings one decision at a time while keeping the wider context in view. Avoid local workarounds and refactoring driven only by a metric. Explain consequential changes and tradeoffs to the user. Keep report files and links consistent when editing them.

Der Hinweis gilt für beliebige Ziel-Repositories und nennt deshalb keine AiNetReview-spezifischen Projektregeln als allgemeine Pflicht.

## Arbeitsablauf

1. Audit starten und die `index.md` des Laufverzeichnisses öffnen. Von dort aus `changed-files/` als Arbeitsansicht und `all-findings/` für den Gesamtblick nutzen.
2. Zuerst sicher erkennbare Fehlalarme aus `changed-files/` entfernen; Unklares stehen lassen. Danach die übrigen Signale im Anwendungskontext prüfen. Mehrere Signale an einem Symbol verlangen eine gemeinsame Prüfung des Kontexts, aber nicht automatisch ein breites Refactoring.
3. Nach Codeänderungen einen normalen Audit wiederholen. Dadurch werden Quellorte, Findings und Dateihashes aus einem frischen Snapshot abgeleitet. Bewusst vertagte Findings bleiben in der vollständigen Ansicht sichtbar.
4. Wenn der aktuelle Dateistand künftig als Vergleichspunkt dienen soll, unabhängig von einem Bericht `--cmd baseline` aufrufen. Ein anschließender unveränderter Audit hat eine leere Änderungsansicht und weiterhin eine vollständige Ansicht. Derselbe Befehl kann auch vor dem allerersten Audit stehen.

**Warum zwei unabhängige Aufrufe?** `review` beobachtet und veröffentlicht; `--cmd baseline` speichert einen Dateistand. Die Reihenfolge ist frei. Der Nutzer entscheidet, wann ein neuer Vergleichspunkt entsteht, ohne einen vorherigen Report auswählen zu müssen.

**Drei Findings in `file.cs`:** Steht die Datei mit Hash `H0` in der Baseline und ändert sich zu `H1`, erscheinen A, B und C in `changed-files/`. Nach einer Behebung von B entsteht `H2`; ein neuer Audit zeigt dort A und C, sofern die Analysen sie weiterhin melden. Ein anschließender Aufruf von `--cmd baseline` speichert `H2`, auch ohne vorausgegangenen Audit. Der nächste unveränderte Audit zeigt in `changed-files/` keinen der beiden Befunde, in `all-findings/` weiterhin beide. Die Baseline macht keine Aussage, ob A und C einzeln akzeptiert wurden.

## Scope

### Muss

- Alle Analysen immer ohne Baseline-Einfluss vollständig ausführen und die vollständige Ansicht pro erfolgreichem Lauf veröffentlichen.
- Eine einfache Baseline für alle analysierbaren Quelldateien im Ausgabeverzeichnis halten; keine Baseline-Datei bedeutet identische vollständige und gefilterte Ansicht.
- Pro Lauf eine `index.md` als Einstieg und die beiden Berichtsverzeichnisse `changed-files/` und `all-findings/` mit konsistenten Links und Analyseberichten erzeugen.
- Genaue Symbolangaben, Quellort-Navigation und verknüpfte Signale anderer Analysen für jede Fundstelle bereitstellen; die `index.md` erklärt den allgemeinen Review-Ablauf und zeigt einen für diesen Audit tatsächlich ausführbaren Baseline-Befehl.
- Agenten die Änderungsberichte als Arbeitsmaterial bearbeiten lassen; den vollständigen Bericht und den Baseline-Dateistand dadurch nicht verändern.
- Die Baseline über einen eigenständigen `--cmd baseline`-Aufruf ohne vorausgesetzten Audit oder Laufkennung aus dem aktuellen Dateistand sicher veröffentlichen.

### Nicht

- In diesem Konzeptschritt Produktcode oder aktuelle Produktdokumentation ändern oder eine Roadmap erstellen.
- Findings als Build-Fehler ausgeben oder Code automatisch ändern.
- Entscheidungen pro Finding dauerhaft speichern, Markdown-Zeilen per EXE bearbeiten oder eine Git-Laufzeitabhängigkeit einführen.
- Künstliche Finding-IDs einführen, die nur für einen Bearbeitungsbefehl gebraucht würden.
- Aus mehreren Signalen automatisch einen Schweregrad oder Refactoring-Auftrag ableiten.

## Verifikation für die spätere Umsetzung

- Bei einem Audit ohne Baseline sind beide Ansichten inhaltlich gleich. Zwei unveränderte Audits ohne zwischenzeitlichen Baseline-Aufruf bleiben gleich; nach einem Baseline-Aufruf ist die Änderungsansicht des nächsten unveränderten Audits leer, die vollständige bleibt erhalten.
- Eine Änderung an einer Datei zeigt alle aktuellen Findings mit dieser Datei in der Änderungsansicht. Ein Cluster erscheint, wenn eine seiner Mitgliedsdateien geändert wurde. Die zentrale Stufe hasht dieselbe Datei nur einmal pro Lauf.
- Agentenänderungen an `changed-files/` beeinflussen weder `all-findings/` noch einen späteren Baseline-Aufruf. Ein fehlgeschlagener Audit oder Baseline-Aufruf verändert die bestehende Baseline nicht.
- `--cmd baseline` funktioniert ohne vorhandenen Bericht und erfasst auch Dateien ohne Findings. Anschließende Quelländerungen werden beim nächsten Audit über den Dateihash sichtbar.
- Zusammengehörige Signale sind über Analysen hinweg sichtbar; unverwandte Signale in derselben Datei werden nicht als ein Befund ausgegeben.
- Ein Agent, der nur den Pfad zum Laufverzeichnis erhält, findet in dessen `index.md` beide Ansichten, einen konkreten ausführbaren Baseline-Aufruf und die Anweisung für einen projektbezogenen Gesamtblick sowie die erste Runde klarer Fehlalarme. Der generierte Befehl verwendet die tatsächliche EXE und den richtigen Zielkontext, auch wenn Pfade Leerzeichen enthalten.

## Grenzen des Dateivergleichs

- `changed-files/` bedeutet Findings in neuen oder geänderten Dateien, nicht alle seit dem letzten Lauf neu entstandenen Findings. Eine gelöschte Verwendung kann zum Beispiel `dead-code-candidates` in einer unveränderten Datei neu auslösen; dieses Finding steht dann nur in `all-findings/`.
- Vertagte Findings erscheinen nach dem Baseline-Aufruf nur noch in `all-findings/`, bis ihre Datei sich ändert. Der Vergleichspunkt ist keine einzelne Ignore-Entscheidung.
