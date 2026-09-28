# Epic 1 – Eingaben und Host-Verträge

Dieses Epic definiert die einzige Produktschnittstelle: einen synchronen CLI-Aufruf der EXE. Das [Konzept](../AiNetReview-Konzept.md) beschreibt den Zweck; [Epic 2](02-Regel-und-Findings.md) definiert die Regelresultate und [Epic 3](03-Storage-und-Berichte.md) die Markdown-Ausgabe.

## Konfiguration und Projektwurzel

`ainetreview.json` liegt unmittelbar in der Projektwurzel. `--config` muss ein absoluter Pfad zu einer Datei mit genau diesem Namen sein. Die Projektwurzel ist ihr Elternverzeichnis. Ein Git-Repository ist nicht erforderlich; direkte Regel-Overrides in der CLI und weitere Konfigurationsmodi gibt es nicht.

Schema-Version 1 hat genau diese Felder:

```json
{
  "schemaVersion": 1,
  "solution": "Project.slnx",
  "outputDirectory": "audit-reporting",
  "rules": {
    "template-noop": {}
  }
}
```

`schemaVersion` ist die Zahl `1`. `solution` und `outputDirectory` sind nichtleere Strings. `rules` ist ein nichtleeres Objekt mit registrierten Regel-IDs als Keys. Jede Regelkonfiguration ist ein Objekt mit ausschließlich den im Regeldeskriptor benannten Feldern; fehlende Felder erhalten dessen Defaults. Die einzige produktive Startregel `template-noop` hat keine Parameter und akzeptiert nur `{}`. Unbekannte oder doppelte JSON-Keys, unbekannte Regel-IDs, falsche Typen und unbekannte Schemaversionen ergeben `INVALID_INPUT`. Wirksame Optionen werden aus Defaults und JSON-Feldern gebildet und nach Key sortiert.

Interne Konfigurationspfade verwenden `/`, beginnen weder mit einem Laufwerksbuchstaben noch einem Separator und enthalten keine `..`-Segmente. Die Auflösung einschließlich Symlinks und Junctions muss innerhalb der Projektwurzel bleiben. `solution` zeigt auf eine vorhandene `.sln` oder `.slnx`. Das Ausgabeverzeichnis darf keine C#-Quelldateien der Solution enthalten und wird bei Bedarf angelegt. Eine eingebundene Quelldatei außerhalb der Projektwurzel macht die Analyse unvollständig und damit fehlerhaft. Vor jedem Regellauf muss jedes analysierte C#-Projekt eine Compilation ohne Roslyn-Diagnosen mit `Severity.Error` liefern; Lade-, Restore- und Kompilationsfehler ergeben `ANALYSIS_FAILED`.

Das Werkzeug ruft `git` nicht auf und verlangt weder `.git` noch `.gitignore`. Die Projektwurzel kann auch ein temporäres Testprojekt sein. Berichte verwenden repo-relative `/`-Pfade; lokale absolute Pfade erscheinen nur in Eingabeparametern und internen Diagnosen. Die Anwendung erzeugt keine Konfigurations- oder Store-Datei.

## CLI

`ainetreview review --config <absoluter-pfad>` startet die Analyse synchron. `--config` ist der einzige Pflichtparameter; zusätzliche Analyseparameter und andere Produktbefehle werden abgewiesen. Die EXE führt die Regeln gegen den aktuellen Solution-Stand aus und veröffentlicht bei vollständigem Erfolg die in [Epic 3](03-Storage-und-Berichte.md) definierten Markdown-Dateien unter `<outputDirectory>/<runId>/`.

Erfolg schreibt genau eine kompakte JSON-Zeile auf stdout:

```json
{"status":"completed","runId":"20260928T163802Z-a1b2c3d4","indexPath":"audit-reporting/20260928T163802Z-a1b2c3d4/index.md","counts":{"detected":0}}
```

Diese Zeile ist eine Prozessantwort, keine gespeicherte Datei. `indexPath` ist relativ zur Projektwurzel. Ein vollständiger Lauf mit Findings hat ebenfalls Exit-Code `0`. Fehler schreiben genau eine JSON-Zeile `{"code":"...","message":"..."}` auf stderr und keine halben Ergebnisse auf stdout. Progress und nutzergerichtete Diagnosen gehen auf stderr; Serilog schreibt nur in Dateien. Exit-Codes: `0` vollständiger Lauf, `2` Eingabe- oder Konfigurationsfehler, `3` Analyse- oder Roslyn-Fehler, `4` Logging- oder Berichtsschreibfehler, `130` Abbruch durch Nutzer. Bei Fehlern wird kein fertiger Berichtslauf veröffentlicht.

## Logging

Der Host initialisiert Serilog vor dem CLI-Parsing einmal pro Prozess. Sein einziger Sink schreibt unter `<AppContext.BaseDirectory>/logs/`, also relativ zum Verzeichnis der EXE, unabhängig vom Arbeitsverzeichnis, `--config` und der analysierten Projektwurzel. Die Logs liegen nur dann im analysierten Projekt, wenn die EXE selbst dort liegt. Logdateien sind interne Betriebsdiagnostik und kein Review-Store.

Die Datei `ainetreview-.log` rotiert täglich und bei 10 MiB; höchstens 30 Logdateien bleiben erhalten. Der File-Sink erlaubt gemeinsames Schreiben durch gleichzeitige Prozesse. Ereignisse enthalten Zeit, Level, Befehl, Run-ID soweit vorhanden und Fehlerkontext. Quellcode, vollständige Konfigurationen, Review-Kommentare und Geheimnisse werden nicht geloggt.

Kann der Host das Logverzeichnis oder die Logdatei nicht beschreiben, endet der Start mit `LOGGING_FAILED` und Exit-Code `4`. Die Fehlermeldung erscheint als eine JSON-Zeile auf stderr; stdout bleibt leer. Serilog schreibt nie auf stdout oder stderr.
