# Epic 3 – Markdown-Berichte

Dieses Epic definiert die **einzigen Review-Ergebnisdateien**. Eingaben und CLI-Antworten stehen in [Epic 1](01-Eingaben-und-Host.md), aktuelle Findings in [Epic 2](02-Regel-und-Findings.md). Interne EXE-relative Logdateien nach Epic 1 sind Betriebsdiagnostik und werden für Analysen nie eingelesen.

## Verzeichnis und Run-ID

Jeder vollständige Aufruf erzeugt genau einen Berichtssatz unter `<outputDirectory>/<runId>/`. Eine Run-ID hat das Format `yyyyMMddTHHmmssZ-<8 kleine Hexzeichen>`; der Zeitstempel ist UTC, die Zufallskomponente verhindert Kollisionen gleichzeitiger Starts. Bei einer Namenskollision wird eine neue Komponente gezogen. Ein Berichtssatz besteht aus `index.md` und genau einer Datei `rules/<ruleId>.md` für jede aktivierte Regel, auch wenn diese keine Findings liefert. Außer Markdown-Dateien werden im Ausgabeordner keine fertigen Ergebnisdateien erzeugt.

Frühere fertige Run-Verzeichnisse bleiben unverändert. Das Werkzeug liest sie weder zur Analyse noch für Unterdrückung oder Zählung ein, löscht sie nicht und verwaltet keine Retention. Das Löschen alter Berichte obliegt dem Nutzer. Ein neuer Lauf zeigt jedes aktuell erkannte Finding erneut.

Alle Markdown-Dateien sind UTF-8 ohne BOM mit LF-Zeilenenden. Ausgabe-Reihenfolge und Escaping sind deterministisch. Projektpfade erscheinen relativ zur Projektwurzel mit `/`. Links auf andere Berichte und auf Quellcode sind vom Speicherort der jeweiligen Markdown-Datei aus relativ; Sonderzeichen in Pfadsegmenten werden URL-kodiert. Inhalte aus Quellcode, Regeloptionen und Evidenz werden so escaped, dass sie weder Tabellen noch Linkziele oder Markdown-Struktur beschädigen.

## Index

`index.md` beginnt mit `# AiNetReview – <runId>`. Es folgt eine Metadaten-Tabelle mit UTC-Start und -Ende, repo-relativem Solution-Pfad und der Gesamtzahl `detected` aktuell erkannter Findings. Eine Regel-Tabelle enthält jede aktivierte Regel nach ID sortiert, ihren Titel, `behaviorVersion`, ihre wirksamen Optionen, ihren `detected`-Zähler und einen Link auf `rules/<ruleId>.md`. Ein kurzer Hinweis erklärt, dass Findings Review-Anlässe und keine automatisch zu behebenden Fehler sind.

Es gibt keine Spalten oder Zähler für frühere Läufe, `open`, `new`, `updated`, `reopened`, `accepted`, `falsePositive` oder `resolved`. `detected` ist die Summe aller von aktivierten Regeln in **diesem** vollständigen Lauf gelieferten Findings. Regeln mit null Findings bleiben sichtbar, damit ein leerer vollständiger Lauf von einem fehlgeschlagenen oder nicht aktivierten Regellauf unterscheidbar ist.

## Regelbericht und Findings

`rules/<ruleId>.md` beginnt mit Regel-ID, Titel, `behaviorVersion`, Zweck, Messung, Review-Fragen, wirksamen Optionen und `detected`-Zähler. Danach folgen **alle** Findings dieser Regel, sortiert nach `projectPath`, `sourcePath`, `startLine`, `subjectId` und `discriminator`. Für jedes Finding werden Projektpfad, Quellpfad mit 1-basierter Startzeile, betroffene Einheit, Unterscheidungsname, Begründung und nach Key sortierte Metriken gezeigt. Evidenz wird nach Pfad, Zeile, Label, Detail und Snippet sortiert und enthält diese Felder sowie den aktuellen Codeausschnitt. Quellpfade verlinken auf die analysierte Quelldatei. Der Codeausschnitt steht im Markdown-Bericht selbst; es gibt keine separate Snapshot-Datei.

Ein Finding hat im Bericht keinen über Läufe hinweg stabilen Status, keine zufällige Finding-ID und keinen Fingerprint. Der Nutzer und Agent können die aktuelle Stelle anhand von Pfad, Zeile, Einheit, Begründung und Evidenz prüfen. Eine frühere akzeptierende Entscheidung wird nicht dargestellt und beeinflusst den Bericht nicht. In alten Berichten bleibt der damalige Evidenz-Ausschnitt sichtbar; Quelllinks können inzwischen auf veränderten Code zeigen. Die alte Markdown-Datei ist deshalb eine archivierte Berichtsausgabe, aber kein vom Werkzeug ausgewerteter Code-Snapshot.

## Veröffentlichung und Fehler

Der Berichtsgenerator erstellt den vollständigen Berichtssatz zunächst unter einem eindeutig benannten temporären Verzeichnis im Ausgabeordner. Erst wenn alle Markdown-Dateien erfolgreich geschrieben und geschlossen sind, wird dieses Verzeichnis auf die finale Run-ID umbenannt. Diese Umbenennung ist der Veröffentlichungspunkt. Bei einem behandelten Fehler oder Abbruch wird der zugehörige temporäre Satz entfernt; ein fertiges Run-Verzeichnis und eine Erfolgsmeldung entstehen nicht. Nach einem Prozessabsturz kann ein erkennbar temporäres Verzeichnis übrig bleiben; es ist kein fertiger Bericht und wird nie als Eingabe verwendet. Frühere fertige Runs werden auch dann nicht verändert.

Gleichzeitige Prozesse benötigen keinen Repository-Lock, weil jeder eine eigene Run-ID und ein eigenes temporäres Verzeichnis verwendet. Eine Kollision beim finalen Namen wird durch eine neue Run-ID vermieden, ohne einen vorhandenen Run zu überschreiben. Fehler beim Anlegen, Schreiben oder finalen Umbenennen sind Berichtsschreibfehler nach Epic 1. Das Werkzeug verspricht keine Erkennung von Quelländerungen, die parallel zu einem laufenden Roslyn-Scan stattfinden; der Bericht beschreibt den von diesem Lauf geladenen Solution-Stand. Für einen neuen Stand wird die EXE erneut ausgeführt.
