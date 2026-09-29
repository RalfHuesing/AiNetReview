---
status: draft
---

# Größen als Review-Signal

## Intention

Ein Review soll auffällig große C#-Methoden, Typen und Dateien sichtbar machen, damit ein Mensch oder Agent ihre Verständlichkeit, Änderbarkeit und den nötigen Werkzeugkontext prüfen kann. Ein Treffer ist eine begründete Prüfaufforderung, kein Qualitätsurteil und kein Auftrag, Code allein für einen kleineren Messwert zu zerschneiden.

## Ist-Stand und Abgrenzung

- `method-control-flow-outliers` vergleicht bereits Entscheidungszahl und maximale Entscheidungsverschachtelung innerhalb eines Produktionsprojekts. `sourceSpanLines` ist dort nur Kontext und kein Auswahlkriterium. Die Analyse verwendet Perzentil und Mindestwerte; ein einzelner großer `switch` reicht nicht allein für einen Treffer.
- Der Bericht zeigt knappe Review-Signale, aber keine Rohmetriken oder langen Begründungen. Die neue Analyse muss ihre auslösenden Größen und den Prüfgrund im sichtbaren Signal verständlich machen.
- AiNetLinter zählt für Methoden Codezeilen ohne Kommentare und Leerzeilen und setzt feste Limits. Das ist eine technische Referenz für die Messung, kein zu übernehmender Grenzwert. Seine `AIContextFootprint`-Metrik schätzt transitive Zeilen, aber weder ein Dateilimit noch eine belegte Grenze für LLM-Verlässlichkeit folgt daraus.
- [OpenAIs Apply-Patch-Dokumentation](https://developers.openai.com/api/docs/guides/tools-apply-patch) beschreibt gezielte Diffs und Werkzeugzugriff zur selektiven Dateierkundung. Die [LongCodeU-Studie](https://aclanthology.org/2025.acl-long.1324/) findet Schwierigkeiten beim Verstehen langen Codes und besonders bei Beziehungen zwischen Codeeinheiten. Beides stützt die Prüfung von Kontextkosten, aber keinen festen Grenzwert für C#-Dateien oder Codex-Edits.

## Empfohlener Ansatz

Eine Review-Analyse für drei getrennt benannte Kandidatenarten: Methode, Typ und Datei. Jede Art hat eine nachvollziehbare Messung und Auswahl; es gibt keinen gemeinsamen Qualitäts-Score. Nur obere Ausreißer sind relevant. Bei Methoden und Typen kann ein hohes Perzentil innerhalb eines Produktionsprojekts Kandidaten filtern; eine Mindestgröße verhindert triviale Treffer in kleinen oder insgesamt kleinen Projekten. Ein sehr großer Wert kann unabhängig von der relativen Stellung auffallen. Für Dateien ist nur dieser Extrempfad vorgesehen. Gleiche Werte an der Grenze werden gleich behandelt.

Als Größenmaß für Methoden und Typen bieten sich unterschiedliche, nicht leere Codezeilen mit C#-Tokens an; so ändern Kommentare und Leerzeilen die Auswahl nicht. Bei einem Typ werden `partial`-Teile zusammengerechnet und geschachtelte Typen getrennt betrachtet. Für Dateien sind physische Zeilen und UTF-8-Bytes der geladenen Quelldatei sinnvoll, weil auch große Kommentare, Literale oder wenige extrem lange Zeilen den Bearbeitungskontext belasten können. Diese Maße dürfen im Bericht nicht als gleiche Einheit vermischt werden.

Für Methoden wird der Umfang mit Entscheidungszahl und Verschachtelung kombiniert: Erhöhte Kontrollflusslast kann einen moderat großen Methodenumfang interessant machen; bei flachem Ablauf reicht nur ein extremer Umfang allein. Ein einfacher 200-Zeilen-Mapper wird nicht schon deshalb gemeldet, weil er relativ lang ist; 200 Zeilen verzweigte Fachlogik können dagegen einen Treffer auslösen. Auch eine lange Methode kann testbar sein; der Treffer fragt nach prüfbaren Pfaden und Verantwortlichkeiten. Die bestehende Kontrollflussanalyse bleibt als eigenes Signal erhalten und darf durch das Aktivieren oder Deaktivieren der Größenanalyse nicht verändert werden. Überschneidungen sollen als zwei begründete Prüfhinweise erkennbar sein.

Typgröße meint den eigenen deklarierten Code eines Typs, auch über `partial`-Dateien hinweg; Dateigröße meint die physische Quelldatei. Eine Datei erzeugt nur bei sehr großem Umfang einen eigenen Treffer; gewöhnliche relative Dateiausreißer bleiben Kontext für Methoden und Typen. Ein großer Typ kann Verantwortlichkeiten bündeln, eine sehr große Datei kann Navigation und manche Werkzeugaufrufe erschweren. Beides ist kontextabhängig: Ein gezielter Edit in einer großen Datei kann weiterhin problemlos sein. Dateigröße allein belegt weder einen Defekt noch einen konkreten LLM-Fehler. Untere Ausreißer werden nicht gemeldet; kleine, fokussierte Einheiten sind kein Größenproblem.

## Scope

### Muss

- Eine zusätzliche, nicht buildbrechende Review-Analyse für auffälligen Methoden-, Typ- und Dateiumfang in produktivem, nicht generiertem C#-Code.
- Deterministische Messung und Auswahl mit erklärter Bezugsgruppe, absoluten Mindestgrößen und einer Behandlung sehr großer Einheiten; keine pauschale Obergrenze als Refactoring-Pflicht.
- Methodenauswahl, die einfachen langen Ablauf anders behandelt als langen verzweigten Ablauf, ohne die bestehende Kontrollflussanalyse zu ersetzen.
- Bericht pro Treffer mit Kandidatenart, gemessenem Umfang, Vergleichsgrund und einer passenden Review-Frage. Bei Dateien ist die Frage auf Navigation und Bearbeitungskontext gerichtet, bei Typen auf Verantwortung und Kohäsion, bei Methoden auf Ablauf und Testbarkeit im konkreten Fall.
- Bei einem Typ über mehrere `partial`-Dateien müssen alle beitragenden Quelldateien als Fundstellen erkennbar sein, damit eine Änderung an einem beliebigen Teil den Treffer in `changed-files/` sichtbar macht.
- Verifikation an synthetischen Grenzfällen und realen Produktionsprojekten: lange flache Mapper, lange verzweigte Methoden, große Typen über `partial`-Dateien, große Dateien mit mehreren Typen, kleine Projekte, Gleichstände sowie ausgeschlossene Tests und generierter Code. Prüfen, ob Treffer zu nützlichen Review-Fragen führen und keine massenhaften mechanischen Schnittvorschläge erzeugen.

### Nicht

- Automatisches Aufteilen oder Refactoring, Buildfehler oder die Behauptung, ein Größenwert beweise schlechte Qualität, Untestbarkeit oder einen LLM-Fehler.
- Meldung ungewöhnlich kleiner Methoden, Typen oder Dateien.
- Übernahme fester AiNetLinter-Limits oder der transitiven `AIContextFootprint`-Metrik als Qualitätsmaßstab.

## Arbeitsgedächtnis (nur Draft)

- **Entschieden:** Eine neue gemeinsame Größenanalyse deckt Methode, Typ und Datei ab; `method-control-flow-outliers` bleibt eine eigene, unveränderte Analyse. Einfache lange Methoden werden nur bei extremer Größe als eigener Größen-Treffer gemeldet; relative Länge allein reicht nicht. Dateien werden ebenfalls nur bei sehr großem Umfang eigenständig gemeldet; gewöhnliche relative Dateiausreißer sind Kontext.
- **Noch zu kalibrieren:** konkrete Größenmaße, Vergleichsgruppen und numerische Mindest-/Extremwerte an realen Projekten. Besonders ein Perzentil ohne Untergrenze würde in kleinen Projekten triviale Treffer produzieren; ein absoluter Grenzwert allein provoziert mechanische Schnitte. Die Auswahlregeln müssen vor `ready` feststehen.
- **Noch zu definieren:** genaue C#-Deklarationsarten für den Methoden- und Typumfang sowie die Behandlung von Dokumenten mit `partial`-Typen, damit die Umsetzung nicht raten muss.
- **Vorläufige Annahme zur Deklarationsart:** Methoden, Konstruktoren und Accessors als eigene Kandidaten; lokale Funktionen und Lambdas als Teil des umgebenden Members. Klassen und Record-Klassen als Typkandidaten, einschließlich aller `partial`-Teile. Die Nutzerantwort dazu steht noch aus.
