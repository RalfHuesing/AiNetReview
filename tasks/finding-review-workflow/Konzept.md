---
status: draft
---

# Finding-Bearbeitung und Baseline

## Intention

AiNetReview soll wiederholte Audits mit vielen Review-Analysen handhabbar machen. Eine Fundstelle soll ihre beteiligten Analysen und Quellorte zeigen; ein Review-Agent soll Entscheidungen über eindeutige IDs ausschließlich durch die EXE erfassen. Akzeptierte oder als Fehlalarm beurteilte Signale sollen bei unverändertem, dafür maßgeblichem Code nicht in jedem Lauf erneut Arbeit erzeugen. Ändert sich dieser Code oder die Bedeutung der Analyse, muss die Entscheidung überprüfbar ungültig werden. Die Berichte bleiben Anlässe zur Kontextprüfung, keine Defekturteile oder automatischen Änderungsaufträge.

## Verifizierter Ausgangspunkt

- `FindingDraft` enthält Analyse-spezifisch Projekt- und Quellpfad, `SubjectId`, `Discriminator`, Startzeile und Evidenz, aber keine veröffentlichte ID oder Checksumme. `CurrentFindingValidator` sichert die Kombination aus Analyse-ID, Projektpfad, Quellpfad, `SubjectId` und `Discriminator` nur innerhalb eines Laufs als eindeutig ab.
- Die Duplicate-Code-Analyse gibt derzeit einen ganzen Cluster als ein Finding aus; `SubjectId` ist dabei nur die Kennung des repräsentativen Mitglieds. Die Identitäten der übrigen Mitglieder stehen im lesbaren Evidenztext, nicht als strukturierte Schlüsseldaten. Das reicht für einen dauerhaften Cluster-Schlüssel noch nicht.
- `ReviewRunner` sammelt und validiert Analyseergebnisse, bevor `MarkdownReportWriter` den vollständigen Lauf als neues Verzeichnis veröffentlicht. Der Writer erzeugt pro Analyse eine Markdown-Tabelle und einen Index. Der Index fordert bislang zum manuellen Löschen bearbeiteter Zeilen auf; eine Bearbeitungs-CLI und Baseline sind nicht implementiert. Siehe [aktuelle Findings](../../docs/review/findings.md) und [CLI](../../docs/interfaces/cli.md).
- Die bestehende [Ideennotiz zur Ignore-Liste](../ideen/checksum-gebundene-ignore-liste.md) weist auf einen wichtigen Fall hin: Ein unveränderter Kandidat einer relativen Analyse kann vorübergehend unter die Auswahlgrenze fallen und später wieder erscheinen. Allein seine Abwesenheit in einem Lauf beweist daher nicht, dass eine frühere Entscheidung hinfällig ist.

## Empfohlener Grundvertrag

1. **Erst sammeln, dann darstellen.** Alle aktiven Analysen liefern strukturierte Findings. Nach ihrer Validierung und vor dem Rendern wird bestimmt, welche Analysen dieselbe fachliche Fundstelle berühren. Gleiche Zeilennummer allein genügt dafür nicht; Symbol- oder Trefferidentität und die konkreten Quellorte zählen. Jedes Signal bleibt seiner Analyse zugeordnet. Der Bericht verlinkt verwandte Signale, ohne mehrere unabhängige Review-Fragen still zu einem Finding zu verschmelzen.
2. **ID und Entscheidung trennen.** Jede bearbeitbare Tabellenzeile bekommt eine nur im betreffenden Lauf eindeutige, kurze ID (etwa `f001` nach fester Sortierung) und zeigt das genaue Symbol beziehungsweise die Mitglieder eines Clusters. Eine Quellzeile kann als Navigationshilfe für diesen Snapshot erscheinen, gehört aber nicht zur Symbolkennung. Die EXE löst die kurze ID gegen genau diesen Lauf auf. Die Baseline speichert stattdessen einen fachlichen Schlüssel: Analyse-ID, Projekt- und Quellpfad, eindeutige Symbolkennung und Finding-Art (`Discriminator`). Die Zeilennummer gehört ausdrücklich nicht in diesen Schlüssel. Die vorhandenen Einzel-Symbol-Analysen bilden `SubjectId` bereits aus Roslyns Deklarations-ID, mit einer Anzeigeformat-Ersatzkennung; für den dauerhaften Vertrag muss die Eindeutigkeit der Ersatzkennung geprüft werden. Die Analyse-ID verhindert, dass `ignored` bei einer Analyse ein Signal einer anderen unterdrückt. Ein Duplicate-Code-Cluster ist kein einzelnes Symbol: Sein Schlüssel braucht die sortierte Menge aller Mitglieder mit Projekt, Pfad und Symbolkennung, damit neue oder entfernte Mitglieder nicht unter der alten Entscheidung verschwinden. Diese Mitglieder müssen strukturiert geliefert werden; sie aus Evidenztext zu lesen wäre fehleranfällig. Kollisionen müssen erkannt werden, statt zwei Findings gleichzusetzen.
3. **Bearbeitung über die EXE.** Ein Befehl erhält den Berichts-Lauf, die Aktion (`fixed`, `ignored`, `false-positive`) und eine oder mehrere IDs. Er prüft vor jeder Änderung, dass die IDs existieren, die Aktion zulässig ist und der Bericht seit dem Lesen nicht widersprüchlich verändert wurde. Erst dann entfernt er die betroffenen Zeilen, aktualisiert Analyse-Dateien, Index und Querverweise und schreibt bei `ignored` oder `false-positive` die Baseline. Eine fehlgeschlagene Mehrfachaktion darf keinen Teilstand hinterlassen. `fixed` kennzeichnet im aktuellen Bericht eine bearbeitete Zeile, auch wenn der Quelltext inzwischen geändert wurde; es erzeugt keine dauerhafte Unterdrückung und erst der nächste Audit bestätigt, ob das Finding verschwunden ist.
4. **Baseline mit Gültigkeitsprüfung.** Zunächst gilt SHA-256 über die vollständige betroffene `.cs`-Datei. Eine zentrale Core-Stufe nach dem Sammeln der Analyseergebnisse berechnet ihn aus dem geladenen Quell-Snapshot, einmal pro Pfad und Lauf; die Analysen liefern weiterhin Symbolidentität und Quellpfade, aber keinen Hash. Bei einem Finding mit mehreren maßgeblichen Quelldateien, etwa einem Duplicate-Code-Cluster, gehören die Hashes aller Mitgliederdateien dazu. Für `ignored` und `false-positive` prüft die EXE vor dem Eintrag den noch aktuellen Dateizustand gegen den Berichts-Snapshot und verlangt bei Abweichung einen neuen Audit. Der Agent übergibt weder Hash noch JSON-Inhalt. Ein Baseline-Eintrag enthält mindestens den stabilen Kandidatenschlüssel, Entscheidung, Dateihash beziehungsweise sortierte Pfad-Hash-Liste und die für seine Bedeutung nötige Analyseversion beziehungsweise wirksamen Optionen. Unterdrückung gilt nur bei passendem Schlüssel und Gültigkeitszustand. Die Baseline liegt getrennt von der Eingabekonfiguration und wird vom Tool sicher veröffentlicht. Änderungen irgendwo in einer betroffenen Datei lassen das Finding bewusst erneut prüfen; eine feinere Granularität kann später anhand realen Rauschens entschieden werden.
5. **Bereinigung.** Ein Baseline-Eintrag bleibt bei erfolgreichem Lauf erhalten, wenn ein unveränderter Kandidat lediglich nicht mehr ausgewählt wird. Er wird bereinigt, wenn eine betroffene Quelldatei fehlt, ihr Hash abweicht oder der Analysevertrag beziehungsweise wirksame Optionen die Entscheidung ungültig machen. Deaktivierte Analysen und fehlgeschlagene Läufe lösen keine Bereinigung aus. Eine ausdrückliche Rücknahme einer Akzeptanz muss möglich sein.
6. **Fokussierte Arbeitsanweisung mit Gesamtblick.** `index.md` zeigt zuerst zusammengehörige Signale und betroffene Symbole beziehungsweise Cluster, damit der Agent den Kontext über Analysen und Quellorte hinweg sieht. Danach prüft er eine Entscheidung nach der anderen, einschließlich Verträgen, Aufrufern und Tests. Mehrere Signale am selben Symbol sind Anlass für eine breitere Untersuchung, aber kein automatischer Auftrag zum großflächigen Refactoring. Jede Entscheidung bleibt findingbezogen. `index.md` erklärt die EXE-Befehle mit konkreten Beispielen; die Arbeit kann nach einem Finding zum nächsten übergehen, ohne alles gleichzeitig lösen zu müssen.

## Drei Findings in einer Datei

Die Baseline ist eine Liste von **Finding-Entscheidungen mit Dateihash**, keine Liste ignorierter Dateien. Für `file.cs` mit A, B und C gilt deshalb:

| Schritt | Dateistand | Baseline-Wirkung |
| --- | --- | --- |
| A wird ignoriert | Hash `H0` | Nur Schlüssel A + `H0` wird unterdrückt; B und C bleiben offen. |
| B wird im Code behoben | Hash `H1` | A + `H0` ist ungültig und darf nicht still auf `H1` umgebucht werden; der Eintrag wird beim nächsten erfolgreichen Audit bereinigt. Dieser Audit prüft, ob A und C weiterhin Findings sind und B verschwunden ist. |
| C soll ignoriert werden | Nach neuem Audit: `H1` | Nur Schlüssel C + `H1` wird unterdrückt. Wenn A weiterhin erscheint, braucht A eine erneute Entscheidung auf `H1`. |

Ein alter Bericht darf nach einer Änderung an `file.cs` nicht mehr dazu dienen, C ungeprüft mit `H1` zu akzeptieren. Bei dateibasiertem Hash kann die EXE nicht beweisen, dass B geändert wurde und C unverändert blieb. Bewusst zurückgestellte Findings erhalten keine Baseline-Entscheidung; sie bleiben im nächsten Audit sichtbar.

**Empfohlener Arbeitsablauf:** Audit ausführen, Gesamtbild sichten, gewünschte Codeänderungen vornehmen, erneut auditieren und erst im frischen Bericht die verbleibenden Findings als `ignored` oder `false-positive` akzeptieren. Zwischenzeitliche Entscheidungen sind möglich, können nach einer Änderung derselben Datei aber ungültig werden. Dafür braucht es keinen eigenen „Baseline-Run“: Ein pauschales Baselinen aller übrigen Findings würde auch ungeklärte oder zurückgestellte Fälle verstecken; ein echtes zweistufiges System müsste akzeptierte Entscheidungen bis zum zweiten Lauf gesondert speichern und erneut zuordnen.

Die sichere, möglichst transaktionale Bearbeitung und die Bindung an Analyseversion/Optionen sind zwei notwendige Ergänzungen zur Ideensammlung: Ohne sie können Teiländerungen am Bericht oder eine später geänderte Analyse eine alte Akzeptanz irreführend erscheinen lassen.

## Scope

### Muss

- Für jeden veröffentlichten Finding-Eintrag eine eindeutige, per CLI ansprechbare ID und das genaue Symbol ohne Zeilennummer als Identitätsbestandteil anzeigen; Quellorte und Evidenz bleiben als Navigationshilfe überprüfbar.
- Querverweise zwischen tatsächlich zusammengehörigen Signalen verschiedener Analysen vor dem Markdown-Rendern aus den gesammelten Ergebnissen bilden; die unabhängigen Signale und Review-Fragen erhalten.
- Einen EXE-gesteuerten Flow für `fixed`, `ignored` und `false-positive` samt Aktualisierung der betroffenen Markdown-Berichte und des Index festlegen. Agenten bearbeiten Finding-Markdown und Baseline nicht selbst.
- Akzeptierte und als Fehlalarm beurteilte Findings per dateibasierter SHA-256-Baseline gezielt unterdrücken; Hashes zentral aus dem Quell-Snapshot pro Datei und Lauf nur einmal berechnen. Staleness, deaktivierte oder fehlgeschlagene Analysen, gelöschte Quellen und veränderte Analysebedeutung korrekt behandeln.
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
- `fixed` entfernt nur die aktuelle Zeile. `ignored` und `false-positive` unterdrücken bei unverändertem gültigem Kontext im Folgelauf genau den betroffenen Kandidaten. Eine Änderung an einer betroffenen Quelldatei oder der Analysebedeutung lässt ihn wieder erscheinen, sofern die Analyse ihn weiterhin meldet. Auch eine reine Zeilenverschiebung verändert den Dateihash und führt mit der gewählten Dateigranularität bewusst zu erneuter Prüfung.
- Deaktivierte Analysen, abgebrochene Läufe und vorübergehend nicht gemeldete relative Kandidaten verlieren keine gültige Akzeptanz. Gelöschte Quellen und nachweislich ungültige Einträge werden bereinigt.
- Wenn A in `file.cs` bei `H0` ignoriert, B in derselben Datei behoben und C bei `H1` ignoriert werden soll, verlangt die EXE vor C eine frische Analyse. A wird nicht automatisch auf `H1` übertragen; B darf nicht durch die Baseline unterdrückt werden. Zurückgestellte Findings bleiben im Folgelauf offen.
- Berichtsdateien, Indexlinks und Baseline bleiben bei Fehlern und konkurrierenden Bearbeitungen konsistent.

## Arbeitsgedächtnis (nur Draft)

1. **Workflow bei Änderungen:** Empfehlung zur Bestätigung: kein technisch eigener zweiter Baseline-Run, sondern ein erneuter normaler Audit nach Codeänderungen. Nur ausdrücklich akzeptierte Findings aus einem aktuellen Bericht werden baselined; zurückgestellte bleiben offen. Eine dateibasierte Akzeptanz vor einer späteren Änderung derselben Datei muss erneut geprüft werden.
2. **Hotspots:** Für dieses Vorhaben genügt die sichtbare Beziehung zwischen Signalen und eine deterministische Sortierung. Eine eigene Hotspot-Aggregation sollte erst mit realen Berichten und einem nachweisbaren Navigationsnutzen festgelegt werden.
