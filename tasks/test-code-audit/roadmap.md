# Roadmap: Testcode als regulärer Bestandteil des Audits

Grundlage ist das freigegebene [Konzept](Konzept.md) mit `status: ready`. Diese Roadmap zerlegt dessen verbindlichen Scope; sie startet keine Umsetzung. Die bestätigten Bereiche heißen `production`, `tests` und `mixed`.

Die Arbeitspakete werden in dieser Reihenfolge abgearbeitet. Ein Paket umfasst Ist-Stand-Prüfung, Implementierung, passende automatisierte Vertragsprüfungen, betroffene aktuelle Dokumentation, Selbstprüfung und einen atomaren Commit einschließlich seines Roadmap-Hakens. Abschlussnachweise werden knapp beim jeweiligen Punkt ergänzt; zusätzliche Ausführungsprotokolle und Leaf-Dateien sind nicht erforderlich. Wiederaufnahme erfolgt an der ersten offenen Checkbox.

Vor Änderungen gelten [AGENTS.md](../../AGENTS.md) und die [Projektregeln](../../.agents/rules/README.md). C#-Navigation erfolgt gemäß Code-Practices zuerst semantisch. Verifizierte Details des Konzepts müssen gegen den dann aktuellen Code geprüft werden. `docs/` beschreibt in jedem Commit ausschließlich den tatsächlich implementierten Stand. Die Anforderungen und Frameworktabellen stehen einmal im Konzept; die Verweise hier sind Teil des jeweiligen Auftrags.

Für Codeänderungen sind die betroffenen Tests und die erforderlichen Gates gemäß [Build and Tests](../../docs/development/build-and-tests.md) auszuführen: `pwsh -File ./scripts/build.ps1`, `pwsh -File ./scripts/test-fast.ps1` und `pwsh -File ./scripts/test-integration.ps1`. Dokumentationsänderungen brauchen Diff-Prüfung und `git diff --check`. Der separate Performance-Release-Gate wird nicht als routinemäßiger Slice-Gate eingeführt. Jeder Haken benötigt einen belegten Abschluss, einschließlich Testausgängen und Commit; fehlgeschlagene relevante Prüfungen bleiben offen.

## Geordnete Arbeitspakete

- [x] **1 — Zentrale Projektrollen und Findingherkunft bereitstellen**

  **Intention:** Analysen, Optionsauswahl und Reporting nutzen dieselbe nachvollziehbare Herkunft, auch wenn die repräsentative Datei eines Findings allein dessen Bereich nicht bestimmt.

  **Scope:** Die vorhandene Projektklassifikation um ein gemeinsames, durch den Reviewlauf verfügbares Ergebnis mit Rolle und Klassifikationsgrund ergänzen. Die bestehenden Erkennungsregeln erhalten. Rollen der tatsächlich repräsentierten Symbole beziehungsweise Vorkommen bis zu den validierten Findings transportieren; normale Findings und Mehrfachvorkommen vollständig behandeln. Testpfade und andere Belegdateien bleiben Evidenz und entscheiden nicht über die Findingherkunft. Eine Übersicht aller geladenen C#-Projekte muss auch ohne Findings möglich sein. Einstiegspunkte sind `ReviewSourceClassifier`, Finding-Drafts/-Ergebnisse und der Reviewrunner; vorhandene Snapshot- und Projektidentitäten wiederverwenden. Maßgeblich sind [Erkennung und Grenzen](Konzept.md#erkennung-und-grenzen) und [Berichtsstruktur](Konzept.md#verbindliche-berichtsstruktur-getrennte-arbeitsbereiche-in-einem-reportlauf).

  **Nicht-Ziele:** Neue Projektmarker, MSBuild-Erkennung, Klassifikationskonfiguration, methodenweise Rollen oder bereits das neue Verzeichnislayout. Kandidatenfilter werden in diesem Paket nicht entfernt.

  **Abnahme:** Vertragsprüfungen belegen Referenz-, Namens- und Pfadklassifikation samt Grund und unveränderte Generated-/Projektgrenzen. Ein Produktions-Finding mit Testevidenz hat Produktionsherkunft; ein Test-Test-Cluster hat ausschließlich Testherkunft; ein gemischter Cluster trägt beide Rollen mit sämtlichen Vorkommen, unabhängig vom Repräsentanten. Projekte ohne Marker und leere Projekte bleiben sichtbar. Bestehende Findingvalidierung, Identitäten und Auswahlverträge funktionieren weiterhin. Damit ist die Herkunft für die folgenden Pakete nutzbar, ohne Namensheuristik im Writer zu duplizieren.

  **Abschlussnachweis:** ReviewRunResult liefert sortierte Klassifikationen aller geladenen C#-Projekte mit Rolle und Grund; jedes Finding führt die Rolle aller vertretenen Symbole/Vorkommen und ignoriert Evidenz bei der Herkunft. Gleichzeilige strukturelle Fragmente bleiben über OccurrenceId getrennt; Finding- und Related-Identität sowie Baselineauswahl bleiben davon unberührt. Verifiziert: Build 0 Warnungen/Fehler, FastTests 320/320, IntegrationTests 106/106; `git diff --check` sauber.

- [x] **2 — Größen- und Kontrollflussanalysen einschließlich Testoptionen umsetzen**

  **Intention:** Den konkreten übergroßen Test sichtbar machen und unterschiedliche Testparameter bei vollständiger Optionsübersicht nachvollziehbar erlauben.

  **Scope:** Testprojekte in `code-size-candidates` und `method-control-flow-outliers` aufnehmen. Die im Konzept festgelegten `testOptions` über autoritative Optionsbeschreibungen validieren, auflösen und projektweise anwenden. Herkunft expliziter beziehungsweise geerbter Werte für spätere Reports verfügbar machen und die Auswahlgründe auf die wirksamen Werte beziehen. Den Defaultgenerator und die Repository-Datei `ainetreview.json` für alle registrierten Analysen vollständig halten, einschließlich Arrays, API-Modi und vollständiger expliziter Testdefaults ausschließlich bei diesen beiden Analysen. Beide betroffenen `BehaviorVersion`-Werte aktualisieren. Aktuelle Konfigurations- und Analysebeschreibungen sowie vorhandene Optionsdarstellung anpassen; die pauschale Test-Ausschlussempfehlung in der Entwickleranleitung bereits hier durch die tatsächlich implementierte Analysepolitik ersetzen und in den folgenden Paketen nachführen. Verbindlich sind [Messwerte](Konzept.md#messwerte-und-vergleichsgruppen), [Testoptionen](Konzept.md#unterschiedliche-bewertung-und-konfigurierbare-testoptionen) und [vollständige Standard-JSON](Konzept.md#vollständige-standard-json-als-optionsübersicht).

  **Nicht-Ziele:** Höhere Testdefaults, neue feste Kontrollflussparameter, rollenbasierte Multiplikatoren, weitere Testprofile, neue Kandidatenarten oder veränderte Messdefinitionen. Benutzerdateien werden nicht überschrieben.

  **Abnahme:** Der Ausgangsfall mit über 3.000 physischen Testdateizeilen und über 1.000 Member-Token-Anfangszeilen liefert die jeweiligen Findings; eine lange String-Fixture erzeugt keine erfundene Membergröße. Helpers und übersprungene Tests sind eingeschlossen. Tests sichern projektweise Populationen, Gleichstände, relative Kriterien und unabhängige Extremgrenzen. Niedrigere/höhere Testwerte und ein anderes Testperzentil ändern nur die entsprechende Testauswahl. Fehlende, leere und partielle Overrides, explizite Unabhängigkeit und wiederhergestellte Vererbung funktionieren. Unbekannte/doppelte Keys, falsche Typen, Bereichsverletzungen und unzulässige Optionen scheitern auch bei deaktivierten Analysen. Generator und Repository-Datei werden gegen sämtliche registrierten Optionsbeschreibungen geprüft; beide Kommandos akzeptieren die vollständige Datei und erhalten bestehende Benutzerkonfigurationen. Optionsänderungen erzeugen keine geänderten Quelldateihashes. Konzept-Abnahme 1, 3, 11 und 12 ist auf Analyse-/Konfigurationsebene belegt; die gemeinsame Root-Darstellung folgt in Paket 5.

  **Abschlussnachweis:** Projektweise Testoptionen verwenden dieselben Deskriptoren und bewahren explizite/geerbte Herkunft; Größen- und Kontrollflussberichte zeigen effektive Werte. Defaultgenerator, Repository-JSON und Konfigurationsbeispiel enthalten alle acht Analysen und sämtliche verfügbaren Defaults. Verifiziert: `build.ps1` 0 Warnungen/Fehler, `test-fast.ps1` 329/329, `test-integration.ps1` 108/108; `git diff --check` sauber.

- [x] **3 — Duplikation, Weiterleitung und Bezeichner auf Testcode erweitern**

  **Intention:** Die vier weiteren Wartbarkeitssignale erfassen Testinfrastruktur und bereichsübergreifende Zusammenhänge mit ihren bestehenden Kriterien.

  **Scope:** Testprojekt-Ausschlüsse in `DuplicateCodeDetector`, `StructuralDuplicateDetector`, `TransparentForwardingClassifier` und `NonAsciiIdentifiersAnalysis` entfernen. Beide Duplikationsverfahren auf den gemeinsamen Bestand anwenden; Vorkommen und Herkunft aus Paket 1 vollständig erhalten. Weiterleitung bleibt projektintern. Die vier `BehaviorVersion`-Werte, betroffenen Analysedeskriptoren und aktuellen Dokumente aktualisieren. Bestehende Ausschlusstests durch Einschluss- und Grenzprüfungen ersetzen. Maßgeblich ist die [Analysepolitik](Konzept.md#analysepolitik) samt den [Vergleichsgruppen](Konzept.md#messwerte-und-vergleichsgruppen).

  **Nicht-Ziele:** Getrennte Produktions-/Testscans, neue Ähnlichkeitsregeln, Testschwellen für Duplikate, Aufteilen oder Begrenzen von Clustern, projektübergreifende Weiterleitungsgraphen und neue Identifiersignale.

  **Abnahme:** Kleine deterministische Lösungen liefern für jede der vier Analysen ein qualifizierendes Test-Finding, auch aus Helpers beziehungsweise deaktivierten Tests; ungeeigneter Code erhält kein künstliches Finding. Beide Duplikationsanalysen erhalten Produktions-, Test- und gemischte Gruppen vollständig und einmal. Bestehende Normalisierungs-, Bindungs- und Fragmentgrenzen bleiben wirksam. Eine Änderung ausschließlich am Testvorkommen wählt das gesamte betroffene Finding in `changed-files` aus. Generated-Dateien/-Symbole bleiben ausgeschlossen. Zuordnungsdaten sind für Paket 5 vollständig. Konzept-Abnahme 2 und 4 ist für diese Analysen auf Analyse-/Runner-Ebene belegt.

  **Abschlussnachweis:** Alle vier Analysen prüfen erkannte Produktions- und Testprojekte; ihre `BehaviorVersion`-Werte sind 2 und Deskriptoren sowie aktuelle Analyse-/Entwicklerdokumentation nennen den erweiterten Scope. FastTests belegen Testhelper und `Fact(Skip)`-Vorkommen, Generated-Ausschlüsse, unveränderte Duplikatnormalisierung sowie strukturelle Bindungs- und Fragmentgrenzen. Jede Duplikationsanalyse erhält genau vollständige Production-only-, Test-only- und Mixed-Gruppen. Ein echter Runnerlauf mit beiden Duplikationsanalysen zeigt bei ausschließlich geänderter Testdatei die vollständigen Findings samt Produktions- und Testvorkommen in `changed-files`; die Weiterleitungsanalyse bildet eine übersprungene Testwurzel mit Helperkette projektintern ab. Verifiziert: `build.ps1` 0 Warnungen/Fehler, `test-fast.ps1` 332/332, `test-integration.ps1` 109/109; `git diff --check` sauber.

- [ ] **4 — Dead-Code-Testscope mit vollständigem Frameworkschutz freigeben**

  **Intention:** Veraltete Testhelpers sichtbar machen, ohne runnergebundene Tests, Hooks, Fixtures und Datenquellen als unbenutzt auszugeben.

  **Scope:** Den gesamten [Dead-Code-Schutzvertrag](Konzept.md#ungenutzter-testcode-braucht-einen-eigenen-schutzvertrag) für xUnit v2/v3, NUnit und MSTest einschließlich dessen Tabelle umsetzen und automatisiert absichern. Semantische Metadatenidentitäten, abgeleitete Attribute, geerbte Bindungen, deaktivierte Tests, Typ-/Containing-Type-Schutz, Fixture-/Lifecycle-Verträge und benannte beziehungsweise typgebundene Datenprovider berücksichtigen. Lokale und breite Bindungsunsicherheit gemäß Konzept behandeln und betroffene Projektbereiche mit Grund für die spätere Scope-Übersicht bereitstellen. Erst mit diesem Schutz im selben abgeschlossenen Slice Testprojekte als Dead-Code-Kandidaten zulassen. `BehaviorVersion`, Deskriptor und aktuelle Dokumentation anpassen. Vorhandene Referenz- und Indirect-Usage-Infrastruktur wiederverwenden.

  **Nicht-Ziele:** Wiederverwendung der aktiven Testwurzeldefinition als vollständiger Unused-Schutz, pauschaler Schutz aller Fixturemethoden, neue Kandidatenarten, Runnerausführung, Paketupgrades im Zielprojekt oder Änderungen am Testpfadgraphen. `apiSurface` wird nicht nach Projektrolle umgeschaltet.

  **Abnahme:** Jeder Mechanismus der verbindlichen Frameworktabelle hat einen passenden Schutztest; Kontrollfälle belegen, dass lokale Lookalike-Attribute keine Frameworkbindung vortäuschen und ein unreferenzierter privater Helper neben einem echten Test Kandidat bleibt. Beide API-Modi, vererbte/mehrdeutige Provider, statisch unbekannte Namen und nicht eingrenzbare Quelltypen erfüllen ihre jeweiligen Verträge. Breite Unsicherheit ist mit Projektbereich und Bindungsgrund im Ergebnis verfügbar; lokal unbeteiligte Helpers bleiben dort Kandidaten, wo der Vertrag keine breite Unsicherheit verlangt. Generierte und projektübergreifende Referenzen schützen wie zuvor. Regressionen belegen unveränderte aktive Wurzeln und Testpfade bei `missing-test-evidence-candidates`. Konzept-Abnahme 5 ist vollständig auf Analyseebene erfüllt.

- [ ] **5 — Drei Reportbereiche mit gemeinsamem Auditauftrag veröffentlichen**

  **Intention:** Ein Agent kann Produktion, Tests und gemischte Findings getrennt bearbeiten und erkennt trotzdem den vollständigen beauftragten Umfang sowie bereichsübergreifende Beziehungen.

  **Scope:** Das im Konzept bestätigte [Verzeichnislayout](Konzept.md#verbindliche-berichtsstruktur-getrennte-arbeitsbereiche-in-einem-reportlauf) im MarkdownWriter und dessen Publikations-/CLI-Anbindung umsetzen. Gemeinsamer Root-Index, sechs stets vorhandene View-Indizes, nichtleere Analyseberichte im passenden Bereich und genau eine vollständige Darstellung je Finding/View. Herkunft, Vorkommensrollen, aktivierte Analysen, Anwendbarkeit, Klassifikationsgründe, Unsicherheitsausschlüsse sowie effektive Optionen und deren Vererbung sichtbar machen. Root-Anleitung, Related-Verweise und sämtliche relativen Links an die neue Tiefe anpassen. Zählung und Sortierung erhalten. Die Reviewtexte übernehmen [Audit-Schritte und Zusammenhänge](Konzept.md#getrennte-audit-schritte-und-gemeinsame-zusammenhänge) und [Agentenerwartungen](Konzept.md#was-ein-agent-erwarten-soll). Betroffene README-, CLI-, Finding-, Konfigurations- und Entwicklerdokumentation einschließlich der bisherigen Test-Ausschlussempfehlung aktualisieren.

  **Nicht-Ziele:** Doppelte flache Reportkopien, Änderung alter Runs, mehrere Analyseausführungen, separate Baselines, neue CLI-Kommandos, Workflowdateien, automatische Delegation oder Bearbeitungsstatusverwaltung.

  **Abnahme:** Writer- und Hosttests sichern leere Bereiche, keine aktiven Analysen, aktive Analysen ohne Findings, alle drei Herkunftsfälle und vollständige gemischte Cluster. Bereichssummen stimmen je View mit der Gesamtsumme überein; globale repräsentative Projekt-/Dateipaare werden korrekt dedupliziert und erläutert. Related-Findings anderer Bereiche sind innerhalb derselben View auffindbar; keine falsche Full-Audit-Zuordnung entsteht. Relative Root-, Bereichs-, Quell- und Full-Audit-Links sind geprüft. Der normale unbeschränkte Auftrag umfasst alle drei `changed-files`-Bereiche, ein Teilauftrag weist den übrigen Umfang aus, und `all-findings` verlangt ausdrücklich einen Voll-Audit. Testfragen verlangen eine begründete Untersuchung und keine automatische Umgestaltung. Die CLI verweist weiterhin auf den gemeinsamen Root-Index; Fehler und Abbruch veröffentlichen keinen Teilbaum, ältere Runs bleiben unverändert. Konzept-Abnahme 6, 7 und 9 sowie die Reportteile von 5 und 11 sind belegt.

- [ ] **6 — Durchgehende Produkt- und Baselineverträge abnehmen**

  **Intention:** Die zusammengesetzten Änderungen funktionieren über den echten Host und behalten die Grenzen zwischen vollständigen Ergebnissen, Dateiänderungen und Testpfadevidenz.

  **Scope:** Verbliebene Host-/Integrationstestannahmen anpassen und fehlende durchgehende Vertragsfälle ergänzen. Ein deterministischer Bestand mit Produktion, Tests, Testinfrastruktur und gemischten Findings prüft den Weg von Konfiguration und Snapshot über alle acht registrierten Analysen bis zum veröffentlichten Report. Beide Kommandos, Default-Bootstrap und bestehende Benutzerdateien einbeziehen. Bereits belegte lokale Detailtests nicht erneut nachbauen. Die zwölf Kriterien unter [Verifikation](Konzept.md#verifikation-des-späteren-ergebnisses) anhand der Pakete und Testfälle auf Vollständigkeit prüfen; zugehörige aktuelle Testdokumentation nachführen.

  **Nicht-Ziele:** Neue Baseline-Version allein wegen Testscope, künstliches Markieren unveränderter Dateien, automatischer Voll-Audit nach Options-/Scopeänderung oder eine rekursive Testpflicht für Tests.

  **Abnahme:** Ohne Baseline enthalten beide Views sämtliche aktuellen Findings. Mit einer schon Testdateien enthaltenden Baseline bleiben neue Findings aus unverändertem Testcode ausschließlich in der vollständigen Datei-basierten View. Teständerungen und Änderungen gemischter Vorkommen wählen vollständige Findings; Partial-Type-Evidenz bleibt erhalten. `missing-test-evidence-candidates` behält Produktionsziele und seine bestehende Snapshot-Auswahl bei hinzugefügtem, geändertem oder gelöschtem C# einschließlich Testcode; Änderungen ausschließlich anderer Dateien erfinden keinen C#-Trigger. Reine Optionsänderungen behalten Datei-Hashes und Datei-basierte Auswahl bei. Konfigurationsbootstrap beider Kommandos erhält Benutzerdateien, und die vollständigen Standards passen weiterhin zu den Deskriptoren. Analysefehler, Abbruch, leerer Lauf und Wiederholung erhalten den atomaren Publikationsvertrag. Build, FastTests und IntegrationTests bestehen; Konzept-Abnahme 1–9 und 11–12 hat nachvollziehbare automatisierte Belege.

- [ ] **7 — Praktischen Agentenaudit aus dem Root-Report durchführen**

  **Intention:** Prüfen, ob ein auditierender Agent den Testscope und die Signale anhand der fertigen Reports sinnvoll bearbeiten und begründet abschließen kann.

  **Scope:** Den in [Konzept-Abnahme 10](Konzept.md#verifikation-des-späteren-ergebnisses) definierten repräsentativen Bestand ausdrücklich als Voll-Audit untersuchen: lange Tests, lange String-Fixtures, Wiederholungen, gemischte Erwartungslogik und Testinfrastruktur. Vorhandene deterministische Testbestände beziehungsweise ein isoliertes Auditfixture verwenden, mit sämtlichen acht Analysen aktiviert. Einstieg ausschließlich über den gemeinsamen Root-Report; Quellen aus allen Bereichen als Kontext nutzen. Zusätzlich die Beschränkung eines Test-Teilauftrags prüfen. Paketnachweis direkt hier ergänzen: reproduzierbarer Aufruf und Bestand, Root-Reportpfad, Laufzeit/Findingumfang je Bereich, konkrete Signalbewertungen mit Quellen sowie gemeinsame Abschlussbewertung. Verbleibende Report- oder Vertragsdefekte innerhalb dieses Paketauftrags beheben und passend absichern.

  **Nicht-Ziele:** Refactoring des auditierten Bestands, objektive Testqualitätsquote, neue automatische Analysen, Schwellenkalibrierung oder Top-N-Filter zum Verkürzen des Audits. Die Beobachtung von Laufzeit ersetzt keinen Performance-Release-Gate.

  **Abnahme:** Für jede vorhandene Signalart liegen konkrete, quellenbelegte Bewertungen als nützlich/actionable, acceptable design, false positive oder needs clarification vor. Testdaten werden von ausführbarem Code unterschieden; Wiederholung und Frameworkbindungen werden im Kontext beurteilt. Produktion, Tests und `mixed` fließen vollständig in den Abschluss ein; ein begrenzter Testauftrag weist Produktion und gemischte Findings ausdrücklich als unbearbeitet aus. Reportnavigation, effektive Optionen und Scopegrenzen sind für diese Bewertung nachvollziehbar. Laufzeit und Umfang sind dokumentiert, ohne stille Unterdrückung. Konzept-Abnahme 10 ist praktisch belegt; bei Korrekturen bestehen betroffene Tests und erforderliche Gates erneut.

- [ ] **8 — Unabhängiges Abschlussaudit**

  **Intention:** Nach den fachlichen Paketen prüfen, ob Umsetzung, Nachweise und tatsächlicher Agentenauftrag das freigegebene Konzept vollständig erfüllen.

  **Scope:** Gemäß [Umsetzungsworkflow](../../.agents/agent-workflow/04-orchestrierte-umsetzung.md#audit) ein unabhängiges, lesendes Audit von Code, Tests, aktuellen Dokumenten, Standardkonfiguration, Reports und den Paketnachweisen durchführen. Alle zwölf Konzeptkriterien und sämtliche Nicht-Ziele prüfen. Besonders nach verbliebenen Testausschlüssen, ungeschützten Frameworkbindungen, verdeckter breiter Unsicherheit, unvollständigen Defaults, auseinandergerissenen Clustern, falschen View-/Related-Verweisen und einem nur auf Produktion ausgerichteten Abschlussauftrag suchen. Ergebnis mit Fundstellen in dieser Roadmap festhalten.

  **Nicht-Ziele:** Produktionscode durch den Audit-Agenten ändern, neue Anforderungen beschließen, einen weiteren Workflow starten oder Korrekturschleifen einführen.

  **Abnahme:** Das Audit hat überprüfbare Fundstellen und benennt erfüllte Kriterien beziehungsweise konkrete Abweichungen. Bei Findings folgt gemäß Workflow höchstens ein Korrektur-Implementierer; dessen Änderungen und Prüfungen werden nachgewiesen. Die Roadmap darf erst als insgesamt abgeschlossen gelten, wenn keine unbelegte fachliche Abnahme oder ungelöste Abweichung verbleibt. Andernfalls bleiben die betroffenen Punkte offen und der verbleibende Umfang wird ausdrücklich benannt. Ohne Findings ist kein Korrekturauftrag erforderlich.

## Zuordnung zur Konzeptabnahme

Diese Zuordnung dient der Vollständigkeitsprüfung; sie ersetzt keine Abnahme des jeweiligen Pakets.

| Konzeptkriterium | Zuständige Pakete |
| --- | --- |
| 1 — Übergroße Tests und String-Fixtures | 2, 6, 7 |
| 2 — Alle sieben Wartbarkeitsanalysen | 2, 3, 4, 6 |
| 3 — Projektweise Statistik und Grenzen | 2, 6 |
| 4 — Vollständige Duplikate und Teständerungen | 3, 5, 6 |
| 5 — Frameworkschutz, API-Modi und Unsicherheit | 4, 5, 6 |
| 6 — Rollen, Klassifikationsgründe und Generated-Grenzen | 1, 2, 3, 4, 5, 6 |
| 7 — Reportstruktur, Counts, Links und Scope | 1, 5, 6 |
| 8 — Baseline und Testpfad-Snapshot | 3, 4, 6 |
| 9 — Signalcharakter und atomare Publikation | 5, 6, 7 |
| 10 — Praktischer Agentenaudit | 7 |
| 11 — Testoptionen, Vererbung und Darstellung | 2, 5, 6 |
| 12 — Vollständige Standarddatei | 2, 6 |
| Unabhängige Prüfung aller Kriterien und Nicht-Ziele | 8 |
