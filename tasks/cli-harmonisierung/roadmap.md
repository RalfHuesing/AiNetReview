# Roadmap: CLI-Harmonisierung

- [ ] **Punkt 1 — Subcommands und CLI-Parser im Host implementieren**
  - Intention: `RootCommand` auf die beiden strikten Subcommands `review` und `baseline` umstellen, `--cmd` und `--config` entfernen, Vorab-Logging in `Program.cs` anpassen.
  - Scope:
    - `src/AiNetReview/Cli/ReviewCommand.cs`: `RootCommand` ohne eigene Action, Subcommands `review` und `baseline` mit einheitlichem Argument `project-path`.
    - Aufruf ohne Subcommand oder mit unbekannten Subcommands/Optionen liefert `INVALID_INPUT` (Exit-Code 2).
    - `ResolvePaths`: Konfigurationspfad ist per Definition immer `<projectRoot>/ainetreview.json`.
    - `src/AiNetReview/Program.cs`: `GetCommandCategory` vereinfachen auf `args.FirstOrDefault()`.
  - Nicht:
    - CLI-Overrides oder zusätzliche Optionen einführen.
    - Format von JSON-Ausgaben auf stdout/stderr ändern.
  - Abnahme:
    - Parser akzeptiert `review [path]` und `baseline [path]`.
    - Aufruf ohne Subcommand oder mit `--cmd` / `--config` wird mit Exit-Code 2 abgewiesen.

- [ ] **Punkt 2 — Baseline-Befehl in index.md und CLI-Dokumentation anpassen**
  - Intention: Den in `index.md` generierten Handlungshinweis für Review-Agenten und die Produktdokumentation an die neue Syntax anpassen.
  - Scope:
    - `src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs`: Generierter Baseline-Befehl lautet `<exePath> baseline "<projectRoot>"`.
    - `docs/interfaces/cli.md`: Dokumentation der Befehle, Argumente und Fehlercodes auf das Ein-Weg-Prinzip aktualisieren.
  - Nicht:
    - Andere Abschnitte der Berichtsgenerierung ändern.
  - Abnahme:
    - `docs/interfaces/cli.md` beschreibt die exakten Aufrufformen `review` und `baseline`.
    - Generierte `index.md` enthält den neuen Baseline-Befehl ohne `--cmd`.

- [ ] **Punkt 3 — Tests anpassen und neue Testfälle ergänzen**
  - Intention: Bestehende Tests auf die neue Subcommand-Syntax umstellen und das Abweisen alter bzw. ungültiger Aufrufformen automatisiert absichern.
  - Scope:
    - FastTests und IntegrationTests aktualisieren, die bisher `ainetreview [project-path]`, `--cmd baseline` oder `--config` aufrufen.
    - Neue Testfälle ergänzen: Aufruf ohne Subcommand (`ainetreview`) liefert Code 2; alte Optionen (`--cmd`, `--config`) liefern Code 2.
  - Nicht:
    - Fachliche Analysen-Logik oder Roslyn-Tests ändern.
  - Abnahme:
    - Alle Fast- und Integrationstests laufen fehlerfrei durch.

- [ ] **Punkt 4 — Audit**
  - Intention: Vollständige Prüfung der Umsetzung gegen Konzept, Regeln und Gesamtsystem.
  - Scope:
    - Verifikation aller Punkte aus `tasks/cli-harmonisierung/Konzept.md`.
    - Konsistenzprüfung von Code, Tests und Dokumentation.
  - Abnahme:
    - Alle Kriterien aus dem Konzept sind nachweisbar erfüllt.
