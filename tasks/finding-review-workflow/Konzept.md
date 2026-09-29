---
status: draft
---

# Finding-Bearbeitung und Baseline

## Intention

AiNetReview soll wiederholte Audits mit vielen Review-Analysen handhabbar machen. Eine Fundstelle soll ihre beteiligten Analysen und Quellorte zeigen; ein Review-Agent soll Entscheidungen über eindeutige IDs ausschließlich durch die EXE erfassen. Akzeptierte oder als Fehlalarm beurteilte Signale sollen bei unverändertem, dafür maßgeblichem Code nicht in jedem Lauf erneut Arbeit erzeugen. Ändert sich dieser Code oder die Bedeutung der Analyse, muss die Entscheidung überprüfbar ungültig werden. Die Berichte bleiben Anlässe zur Kontextprüfung, keine Defekturteile oder automatischen Änderungsaufträge.

## Verifizierter Ausgangspunkt

- `FindingDraft` enthält Analyse-spezifisch Projekt- und Quellpfad, `SubjectId`, `Discriminator`, Startzeile und Evidenz, aber keine veröffentlichte ID oder Checksumme. `CurrentFindingValidator` sichert die Kombination aus Analyse-ID, Projektpfad, Quellpfad, `SubjectId` und `Discriminator` nur innerhalb eines Laufs als eindeutig ab.
- `ReviewRunner` sammelt und validiert Analyseergebnisse, bevor `MarkdownReportWriter` den vollständigen Lauf als neues Verzeichnis veröffentlicht. Der Writer erzeugt pro Analyse eine Markdown-Tabelle und einen Index. Der Index fordert bislang zum manuellen Löschen bearbeiteter Zeilen auf; eine Bearbeitungs-CLI und Baseline sind nicht implementiert. Siehe [aktuelle Findings](../../docs/review/findings.md) und [CLI](../../docs/interfaces/cli.md).
- Die bestehende [Ideennotiz zur Ignore-Liste](../ideen/checksum-gebundene-ignore-liste.md) weist auf einen wichtigen Fall hin: Ein unveränderter Kandidat einer relativen Analyse kann vorübergehend unter die Auswahlgrenze fallen und später wieder erscheinen. Allein seine Abwesenheit in einem Lauf beweist daher nicht, dass eine frühere Entscheidung hinfällig ist.

## Empfohlener Grundvertrag

1. **Erst sammeln, dann darstellen.** Alle aktiven Analysen liefern strukturierte Findings. Nach ihrer Validierung und vor dem Rendern wird bestimmt, welche Analysen dieselbe fachliche Fundstelle berühren. Gleiche Zeilennummer allein genügt dafür nicht; Symbol- oder Trefferidentität und die konkreten Quellorte zählen. Jedes Signal bleibt seiner Analyse zugeordnet. Der Bericht verlinkt verwandte Signale, ohne mehrere unabhängige Review-Fragen still zu einem Finding zu verschmelzen.
2. **ID und Entscheidung trennen.** Jede bearbeitbare Tabellenzeile bekommt eine im betreffenden Lauf eindeutige, kurze ID. Die EXE löst sie gegen genau diesen Lauf auf. Für eine Baseline reicht die sichtbare ID allein nicht: Dort braucht es einen stabilen Schlüssel aus Analyse, Kandidatenidentität und relevante Code-Evidenz. Zeilennummern und Reportpfade sind keine dauerhafte Identität. Eine Kollision muss erkannt werden, statt zwei Zeilen gleichzusetzen.
3. **Bearbeitung über die EXE.** Ein Befehl erhält den Berichts-Lauf, die Aktion (`fixed`, `ignored`, `false-positive`) und eine oder mehrere IDs. Er prüft vor jeder Änderung, dass die IDs existieren, die Aktion zulässig ist und der Bericht seit dem Lesen nicht widersprüchlich verändert wurde. Erst dann entfernt er die betroffenen Zeilen, aktualisiert Analyse-Dateien und Index und schreibt bei `ignored` oder `false-positive` die Baseline. Eine fehlgeschlagene Mehrfachaktion darf keinen Teilstand hinterlassen. `fixed` ist eine Entscheidung für den aktuellen Bericht, keine dauerhafte Unterdrückung; der nächste Audit prüft den Code erneut.
4. **Baseline mit Gültigkeitsprüfung.** Die EXE berechnet den Fingerprint beim Audit und prüft ihn beim Entscheidungsbefehl gegen den noch aktuellen Quellzustand. Der Agent übergibt weder Hash noch JSON-Inhalt. Ein Baseline-Eintrag enthält mindestens den stabilen Kandidatenschlüssel, Entscheidung, Fingerprint und die für seine Bedeutung nötige Analyseversion beziehungsweise wirksamen Optionen. Unterdrückung gilt nur bei passendem Schlüssel und Gültigkeitszustand. Die Baseline liegt getrennt von der Eingabekonfiguration und wird vom Tool sicher veröffentlicht.
5. **Fokussierte Arbeitsanweisung.** `index.md` erklärt Befehle mit konkreten Beispielen. Die Anleitung fordert dazu auf, ein Finding nach dem anderen im Anwendungskontext zu prüfen, betroffene Verträge, Aufrufer und Tests einzubeziehen und weder Kennzahlen noch Hotspots als automatischen Änderungsauftrag zu behandeln. Die Arbeit kann danach zum nächsten Finding übergehen; sie muss nicht künstlich auf ein einziges Finding pro Sitzung begrenzt werden.

Die sichere, möglichst transaktionale Bearbeitung und die Bindung an Analyseversion/Optionen sind zwei notwendige Ergänzungen zur Ideensammlung: Ohne sie können Teiländerungen am Bericht oder eine später geänderte Analyse eine alte Akzeptanz irreführend erscheinen lassen.

## Scope

### Muss

- Für jeden veröffentlichten Finding-Eintrag eine eindeutige, per CLI ansprechbare ID und überprüfbare Zuordnung zu Analyse, Quellort und Evidenz vorsehen.
- Querverweise zwischen tatsächlich zusammengehörigen Signalen verschiedener Analysen vor dem Markdown-Rendern aus den gesammelten Ergebnissen bilden; die unabhängigen Signale und Review-Fragen erhalten.
- Einen EXE-gesteuerten Flow für `fixed`, `ignored` und `false-positive` samt Aktualisierung der betroffenen Markdown-Berichte und des Index festlegen. Agenten bearbeiten Finding-Markdown und Baseline nicht selbst.
- Akzeptierte und als Fehlalarm beurteilte Findings per Fingerprint-basierter Baseline gezielt unterdrücken; Staleness, deaktivierte oder fehlgeschlagene Analysen, gelöschte Quellen und veränderte Analysebedeutung korrekt behandeln.
- Leere Analyse-Dateien und Indexlinks nach Entscheidungen konsistent entfernen und den vollständig bearbeiteten Lauf klar kennzeichnen.
- Eine deterministische, nachvollziehbare Reihenfolge und konkrete Bearbeitungsbeispiele im Index vorsehen. Mehrfachtreffer an einem Symbol sichtbar machen; daraus allein keine Dringlichkeitswertung ableiten.

### Nicht

- In diesem Konzeptschritt Code, CLI, Berichte oder aktuelle Produktdokumentation ändern; keine Roadmap erstellen.
- Review-Findings zu Build-Fehlern machen oder Quellcode automatisch reparieren.
- Eine vollständige Finding-Historie, einen Zustandsautomaten über alle Läufe oder eine Git-Laufzeitabhängigkeit einführen.
- Aus bloßer Anzahl von Signalen automatisch einen Hotspot-Schweregrad oder Refactoring-Auftrag ableiten.

## Verifikation für die spätere Umsetzung

- Zwei Analysen melden dasselbe Symbol an unterschiedlichen Zeilen; beide Berichte zeigen die Beziehung, ohne unabhängige Befunde zu vermischen. Unverwandte Signale in derselben Datei werden nicht verknüpft.
- IDs bleiben innerhalb eines Laufs eindeutig, auch bei gleichen Dateinamen, mehreren Projekten, Duplicate-Code-Clustern und mehreren Findings derselben Analyse. Falsche, doppelte oder veraltete IDs ändern weder Bericht noch Baseline teilweise.
- `fixed` entfernt nur die aktuelle Zeile. `ignored` und `false-positive` unterdrücken bei unverändertem gültigem Kontext im Folgelauf genau den betroffenen Kandidaten. Eine maßgebliche Code- oder Analyseänderung lässt ihn wieder erscheinen, sofern die Analyse ihn weiterhin meldet.
- Deaktivierte Analysen, abgebrochene Läufe und vorübergehend nicht gemeldete relative Kandidaten verlieren keine gültige Akzeptanz. Gelöschte Quellen und nachweislich ungültige Einträge werden bereinigt.
- Berichtsdateien, Indexlinks und Baseline bleiben bei Fehlern und konkurrierenden Bearbeitungen konsistent.

## Arbeitsgedächtnis (nur Draft)

1. **ID-Stabilität:** Empfehlung: Die sichtbare kurze ID ist im ausgewählten Lauf eindeutig; die Baseline nutzt unabhängig davon eine stabile fachliche Kandidatenidentität. Alternative: dieselbe sichtbare ID über Läufe hinweg; dafür müssten Umbenennungen, geänderte Cluster und Kollisionen als öffentlicher Dauervertrag gelöst werden.
2. **Fingerprint-Grenze:** Empfehlung: Der Fingerprint deckt den für das Signal maßgeblichen Codebereich und nötige externe Evidenz ab; Analysen ohne verlässlich definierbaren Gültigkeitsbereich dürfen zunächst keine Baseline erzeugen. Ein Hash der ganzen Quelldatei ist einfacher, erzeugt bei unabhängigen Änderungen aber erneute Review-Arbeit. Ein Hash nur einer Zeile kann relevante Änderungen übersehen.
3. **Aufräumen bei Abwesenheit:** Empfehlung: Eine weiterhin gültige Akzeptanz nicht allein deshalb löschen, weil ein Kandidat in einem erfolgreichen Lauf nicht ausgewählt wurde. Erst Quellverlust, geänderte maßgebliche Evidenz oder geänderte Analysebedeutung machen sie ungültig; eine explizite Rücknahme bleibt möglich. Automatisches Löschen bei jeder Abwesenheit spart Baseline-Einträge, kann aber das von der Baseline verhinderte Wiederholungsrauschen zurückbringen.
4. **Hotspots:** Für dieses Vorhaben genügt die sichtbare Beziehung zwischen Signalen und eine deterministische Sortierung. Eine eigene Hotspot-Aggregation sollte erst mit realen Berichten und einem nachweisbaren Navigationsnutzen festgelegt werden.
