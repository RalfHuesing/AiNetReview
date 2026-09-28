# Roadmap: Agentenfreundliche Auditberichte

- [ ] **Kompakte Markdown-Berichte und bearbeitbare Befundtabellen**
  - Intention: Ein Agent findet nur relevante Regeldateien und kann offene Befunde dort als einzelne Tabellenzeilen abarbeiten.
  - Scope: `MarkdownReportWriter` und die beschreibenden Texte der drei Produktionsregeln an [Konzept.md](Konzept.md) anpassen: schlanker Index, getrennte Meldungen für null Befunde und null aktive Regeln, keine Dateien für leere Regeln, kurzer Regelkontext und eine Tabellenzeile pro Befund mit repräsentativem Quelllink, knappem Signal und nötigen weiteren Quelllinks. Der Index erklärt Löschen und Abschluss der Bearbeitung. Bestehende Quelllink-Escapes, deterministische Ordnung und atomare Veröffentlichung bleiben erhalten. Betroffene aktuelle `docs/`-Seiten und Tests im selben Slice anpassen.
  - Nicht: Keine Erkennungslogik, keine künstliche Schwere oder Wahrscheinlichkeit, keine Checkboxen oder Fortschrittsdatei; die manuelle `findings.json` wird erst im nächsten Punkt entfernt.
  - Abnahme: FastTests und IntegrationTests decken gemischte und leere Ergebnisse, null aktive Regeln, Tabellenzeilen und alle Duplicate-Code-Fundstellen ab. Build, betroffene Tests, `git diff --check` und Bericht-Diff bestehen; Änderungen sind als eigener Slice committet.

- [ ] **Manuellen Auditlauf auf Markdown beschränken**
  - Intention: Auch das separat gestartete Repository-Audit veröffentlicht nur den für den Agenten vorgesehenen Bericht.
  - Scope: `findings.json` samt nur dafür benötigter Erzeugung und Tests aus dem manuellen Audit entfernen. Den zentral veröffentlichten Index mit absolutem Repositorypfad und funktionierenden Quelllinks prüfen. Betroffene aktuelle `docs/`-Seiten im selben Slice anpassen.
  - Nicht: Keine Änderung am Audit-Profilformat, der CLI-Erfolgsantwort oder den Review-Regeln.
  - Abnahme: Build, FastTests, IntegrationTests und `pwsh -File ./scripts/test-audit.ps1 -Target ainetreview` bestehen. Der neue Lauf enthält nur `index.md` und Regeldateien mit Befunden, keine `findings.json`; Quelllinks führen zum analysierten Repository. `git diff --check` besteht; Änderungen sind als eigener Slice committet.

- [ ] **Abschließendes Audit**
  - Intention: Das Ergebnis gegen das freigegebene Konzept und den tatsächlich erzeugten Bericht prüfen.
  - Scope: Änderungen, Tests und aktuelle Dokumentation nur lesend prüfen; einen neuen Bericht auf knappen Regelkontext, bearbeitbare Tabellen, Navigation, Leerfälle und fehlende JSON-Zweitausgabe prüfen. Festgestellte Abweichungen als konkretes Feedback für höchstens einen Korrektur-Slice benennen.
  - Nicht: Keine Produktionscode-Änderung im Audit.
  - Abnahme: Jeder Muss-Punkt aus [Konzept.md](Konzept.md) ist mit Code-, Test- oder Berichtsnachweis abgedeckt; keine offene Abweichung bleibt unbeurteilt. Checkbox erst nach bestandenem Audit setzen.
