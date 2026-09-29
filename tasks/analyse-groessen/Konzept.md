---
status: ready
---

# Codegrößen als Review-Signal

## Intention

`code-size-candidates` soll ungewöhnlich umfangreiche ausführbare C#-Member und Klassen sowie sehr große C#-Dateien zur Prüfung zeigen. Der Bericht erklärt den gemessenen Umfang und den Auslösegrund. Ein Treffer ist weder ein Qualitätsurteil noch ein Auftrag, Code allein wegen seiner Größe zu zerlegen.

## Abhängigkeit und Produktgrenze

Die Umsetzung beginnt erst, nachdem [`core-code-metriken`](../core-code-metriken/Konzept.md) mit seiner [Roadmap](../core-code-metriken/roadmap.md) abgeschlossen ist. Diese Vorarbeit liefert `CodeLineMetrics` und `ControlFlowMetrics` unter `AiNetReview.Core.Analysis`. Die Größenanalyse verwendet beide Core-Messungen; sie implementiert deren Zählregeln nicht erneut. Fehlt der abgeschlossene Core-Vertrag oder weicht seine Implementierung davon ab, wird dieser Konflikt vor der Größenanalyse behoben, nicht mit einer lokalen Ersatzmetrik umgangen.

`code-size-candidates` ist genau **eine** registrierte `IReviewAnalysis` mit Descriptor-Titel `Code Size Candidates`, Behavior-Version 1 und Default `enabled: true` im bestehenden `review`-Befehl. `method-control-flow-outliers` bleibt als eigenständige Analyse mit unverändertem Verhalten, Descriptor und Konfigurationsvertrag bestehen. Das Ein- oder Ausschalten der Größenanalyse ändert deren Ergebnisse nicht. Beide Analysen können dieselbe gewöhnliche Methode melden; die bestehende symbolbasierte Beziehung im Report zeigt dann beide Prüfhinweise.

Die Analyse betrachtet ausschließlich geladene Produktions-C#-Projekte. Sie verwendet `ReviewSourceClassifier` für Testprojekte, generierte Dokumente und generierte Symbole. Messungen erfolgen aus dem geladenen Roslyn-Snapshot, nicht aus erneut gelesenen Dateien. Treffer führen weder zu Buildfehlern noch zu automatischen Änderungen.

## Kandidaten und Messung

### Ausführbare Member

Kandidaten sind explizit deklarierte gewöhnliche Methoden, Instanz- und statische Konstruktoren, Accessors, Operatoren, Konvertierungsoperatoren sowie Properties und Indexer mit eigenem Expression-Body. Ein Property/Indexer mit Accessor-Liste ist **kein** zusätzlicher aggregierter Kandidat; seine ausführbaren Accessors sind Kandidaten. Deklarationen ohne Body oder Expression-Body, Destruktoren und implizit erzeugte Symbole sind ausgeschlossen. Von einer partiellen Methode zählt nur die implementierende Deklaration. Lokale Funktionen, Lambdas und anonyme Methoden erhalten keinen eigenen Treffer.

`CodeLineMetrics.CountExecutableDeclaration` liefert `memberCodeLines`. Es zählt Token-Startzeilen der ganzen ausführbaren Deklaration einschließlich Signatur, Attribute und Klammern; Leerzeilen und Trivia zählen nicht. Lokale Funktionen und Lambdas zählen zum Umfang des umgebenden Members. `ControlFlowMetrics.Measure` erhält nur dessen Body oder Expression-Body-Ausdruck und liefert `DecisionCount`, `DecisionConstructCount` und `MaxDecisionNesting`. Seine Messung überspringt lokale Funktionen und Lambdas. Diese unterschiedliche Behandlung ist Teil des Vertrags: Umfang eingeschlossener Funktionen kann einen Extremgrößen-Treffer auslösen, ihr Kontrollfluss macht den umgebenden Member aber nicht zum verzweigten Kandidaten.

Ein Member gilt für diese Analyse als verzweigt, wenn `DecisionCount >= 8` **und** `DecisionConstructCount >= 2`, oder wenn `MaxDecisionNesting >= 4`. Ein großer einzelner flacher `switch` erfüllt die Entscheidungsbedingung deshalb nicht allein. Dies ist die Entscheidungsmetrik der Core-Vorarbeit, keine kognitive Komplexitätszahl und keine Aussage über Testbarkeit.

### Klassen

Kandidaten sind explizite Klassen und Record-Klassen, auch geschachtelte und `partial` deklarierte. Structs, Record-Structs, Interfaces, Enums und Delegates sind keine Typkandidaten. `CodeLineMetrics.CountOwnTypePart` zählt die Token-Startzeilen jedes nicht generierten Deklarationsteils ohne die Deklarationen geschachtelter Typen oder Delegates. `typeCodeLines` ist die Summe dieser Teilwerte für dasselbe Roslyn-Typsymbol **innerhalb eines Projekts**. Jeder geschachtelte Klassentyp wird eigenständig gemessen. Ein Typ mit generiertem Symbol wird ganz ausgeschlossen; generierte Dokumente liefern keine Teile.

### Dateien

Jedes nicht generierte `.cs`-Dokument eines Produktionsprojekts ist ein Dateikandidat. `fileLines` ist `SourceText.Lines.Count` einschließlich einer von `SourceText` gezählten letzten leeren Zeile. `fileUtf8Bytes` ist die UTF-8-Bytezahl des geladenen `SourceText` ohne BOM. Mehrzeilige Literale, Kommentare und Leerraum zählen hier vollständig. Dieselbe physische Datei kann bei Einbindung in mehrere Produktionsprojekte einmal je Projekt erscheinen. Ein Dokument ohne eine einzige Zeile mit einem Nicht-Leerraum-Zeichen wird nicht gemeldet, weil die bestehende Finding-Validierung eine nicht leere Quelltext-Evidenz verlangt.

## Auswahl

Für jedes Produktionsprojekt werden die Größen aller berechtigten ausführbaren Member sowie aller berechtigten Klassen jeweils in einer eigenen Gruppe verglichen. Das nächste-Rang-Perzentil `P` ist der aufsteigend sortierte Wert an der einsbasierten Position `ceil(P / 100 × Gruppengröße)`. Bei leerer Gruppe gibt es keinen relativen Kandidaten. Bei einer Gruppe von einem Element gilt dieses Element als Perzentilwert; die absolute Untergrenze bleibt erforderlich. Gleichstände am Perzentilwert werden vollständig eingeschlossen. Untere Ausreißer werden nicht gemeldet.

Ein Member erzeugt **einen** Treffer, wenn mindestens einer dieser unabhängigen Pfade erfüllt ist:

1. **Länge plus Kontrollfluss:** `memberCodeLines >= minMemberCodeLines`, `memberCodeLines >= Member-Perzentilwert` und die obige Verzweigungsbedingung ist erfüllt.
2. **Extreme Länge:** `memberCodeLines >= extremeMemberCodeLines`, unabhängig von Kontrollfluss und Perzentil.

Ein flacher 200-Codezeilen-Mapper erfüllt mit den Defaults keinen der beiden Pfade. Eine 200-Codezeilen-Methode mit hinreichendem Kontrollfluss kann den ersten Pfad erfüllen, sofern sie den Projekt-Perzentilwert erreicht. Werden beide Pfade erfüllt, bleibt es ein Treffer mit beiden Auslösegründen.

Eine Klasse erzeugt **einen** Treffer, wenn `typeCodeLines >= minTypeCodeLines` und `typeCodeLines >= Klassen-Perzentilwert`, oder wenn `typeCodeLines >= extremeTypeCodeLines`. Beide Gründe können gemeinsam vorliegen.

Eine Datei erzeugt **einen** Treffer, wenn `fileLines >= extremeFileLines` **oder** `fileUtf8Bytes >= extremeFileUtf8Bytes`. Für Dateien wird kein Perzentil berechnet. Physische Dateigröße ist ein Hinweis auf Navigations- und Kontextaufwand, kein Beleg für einen Defekt oder einen Fehler eines LLM-Werkzeugs.

## Konfiguration

Die folgenden Analyseoptionen sind ganzzahlige JSON-Werte; andere JSON-Typen und Werte außerhalb der Bereiche werden durch den bestehenden Konfigurationsmechanismus abgewiesen. `enabled` ist das bestehende Standardfeld und hat den Default `true`. Die beiden Pfade je Kandidatenart sind unabhängig; zwischen Untergrenze und Extremwert gibt es deshalb absichtlich keine zusätzliche Ordnungsbedingung.

| Option | Default | Gültiger Bereich | Wirkung |
| --- | ---: | ---: | --- |
| `percentile` | 90 | 50–99 | Gemeinsames nächstes-Rang-Perzentil für die getrennten Member- und Klassengruppen. |
| `minMemberCodeLines` | 80 | 1–2.147.483.647 | Absolute Untergrenze nur für den relativen Member-Pfad. |
| `extremeMemberCodeLines` | 300 | 1–2.147.483.647 | Unabhängiger Extrempfad für Member. |
| `minTypeCodeLines` | 300 | 1–2.147.483.647 | Absolute Untergrenze nur für den relativen Klassen-Pfad. |
| `extremeTypeCodeLines` | 800 | 1–2.147.483.647 | Unabhängiger Extrempfad für Klassen. |
| `extremeFileLines` | 1000 | 1–2.147.483.647 | Erster unabhängiger Dateipfad. |
| `extremeFileUtf8Bytes` | 131072 | 1–2.147.483.647 | Zweiter unabhängiger Dateipfad. |

Die Defaultwerte sind Auswahlgrenzen für Review-Kandidaten, keine zulässigen Höchstgrößen. Der Descriptor ist die Quelle der generierten Defaults. Die Repository-Konfiguration `ainetreview.json` führt die neue Produktionsanalyse mit denselben Werten auf. Es gibt keine weiteren Analyseoptionen, keine Gewichte eines Gesamtscores und keine separate Aktivierung je Kandidatenart.

## Findings und Bericht

Ein Treffer nennt Kandidatenart, gemessenen Umfang, den jeweils wirksamen Grenzwert oder Perzentilwert und jeden erfüllten Auslösepfad. Bei Membern nennt er zusätzlich Entscheidungszahl, Zahl der Entscheidungskonstrukte und maximale Verschachtelung; diese Werte bleiben erklärender Kontext, wenn allein die Extremgröße auslöst. Die drei Fragen im englischen Bericht lauten für Member „Is this executable body cohesive, and are its paths and tests easy to review?“, für Klassen „Do the members of this class serve one cohesive responsibility?“ und für Dateien „Can relevant code in this file be located and edited with focused context?“ Kein Text fordert allein wegen eines Treffers zum Aufteilen auf.

Member- und Klassen-Findings verwenden als `SubjectId` `DocumentationCommentId.CreateDeclarationId(symbol)`, ersatzweise `symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`, wie die bestehende Kontrollflussanalyse. Ihre `Discriminator`-Werte sind `member-size` und `type-size`. Member-Findings nehmen die Deklarationsstelle und eine Evidenz am Namen beziehungsweise am Accessor- oder Operator-Keyword. Ein Klassen-Finding verwendet als repräsentative Stelle den nach projekt-relativem Pfad und Startzeile ersten nicht generierten Deklarationsteil und je Teil eine Evidenz am Deklarationsnamen. Es bleibt ein Single-Symbol-Finding mit genau einem `RelatedSymbol`; sämtliche Teil-Dateien stehen in `Evidence`, damit eine Änderung an irgendeinem Teil den Treffer in `changed-files/` auswählt. Datei-Findings haben `Discriminator` `file-size`, `SubjectId` als Konkatenation von `file:` und projekt-relativem Quellpfad und die erste Zeile mit einem Nicht-Leerraum-Zeichen als repräsentative Stelle und Evidenz. Alle Evidenz-Snippets stammen aus derselben geladenen Quelltextzeile und genügen `ReviewRunner`s Validierung.

Der Markdown-Bericht erhält für `code-size-candidates` einen eigenen knappen Signaltext mit den obigen Zahlen und der zur Kandidatenart passenden Frage. Er druckt keine langen Rationale- oder Code-Snippets. Findings, Evidenz und Bericht bleiben deterministisch sortiert. Bei deaktivierter Analyse oder null Treffern gelten die vorhandenen Regeln für Reportdateien und Indizes.

## Technischer Zuschnitt

Ein `IReviewAnalysis`-Einstieg koordiniert die Analyse. Messung der drei Kandidatenarten, Auswahl und Aufbau der Findings bleiben getrennte Verantwortlichkeiten; gemeinsame Logik wird nur für identische Operationen geteilt. Die konkrete Zahl und Benennung privater Klassen ist kein Produktvertrag. Es entsteht weder eine Klasse, die alle Messungen und Berichtspfade zugleich enthält, noch eine separate öffentlich registrierte Analyse pro Kandidatenart.

## Scope

### Muss

- Den obigen Mess-, Auswahl-, Konfigurations- und Berichtvertrag als genau eine Produktionsanalyse implementieren, in der Produktionsregistrierung und im Repository-`ainetreview.json` aufnehmen und die betroffenen `docs/`-Seiten aktualisieren.
- Die Core-Vorarbeit ausschließlich als abgeschlossene Abhängigkeit nutzen und die bestehende Kontrollflussanalyse unverändert lassen.
- Automatisierte Tests für Deklarationsarten und Ausschlüsse, Token- und Dateimaße, `partial`-Aggregation, kleine und leere Gruppen, Gleichstände, beide Auslösepfade, Grenzwerte und ungültige Konfiguration, Finding-Identität, Evidenz aus dem Snapshot, `changed-files/`-Selektion und sichtbaren Markdown-Text bereitstellen.
- Die Defaults an AiNetReview und dem read-only betrachteten AiNetLinter als manuellen Audit auswerten. Zahl und Arten der Treffer festhalten und stichprobenartig prüfen, ob die Begründungen die tatsächlich auslösenden Pfade zeigen; diese Beobachtung ändert die festgelegten Defaults nicht stillschweigend.

### Nicht

- Buildfehler, automatische Codeänderung, pauschale Refactoring-Anweisung oder eine Diagnose „God Class“ allein wegen Typumfang.
- Treffer für Testprojekte, generierten Code, ungewöhnlich kleine Einheiten, lokale Funktionen oder Lambdas als eigene Subjekte.
- Übernahme von AiNetLinter-Grenzen, einer transitiven `AIContextFootprint`-Metrik, einer allgemeinen kognitiven Komplexitätszahl oder einem zusammengesetzten Qualitäts-Score.
- Änderung von `method-control-flow-outliers`, Core-Messregeln, Report-Publikationsstruktur oder CLI-Befehlen.

## Verifikation

Die Tests prüfen Grenzwerte jeweils unmittelbar darunter, genau darauf und darüber; Perzentile mit einem und vielen Elementen sowie Gleichständen; flache Mapper gegen verzweigte Fachlogik; zusammengefasste `partial`-Klassen einschließlich einer Änderung an einem nicht repräsentativen Teil; Dateigröße durch Zeilen und separat durch UTF-8-Bytes einschließlich mehrzeiliger Literale; ungültige Optionswerte und die unveränderte bestehende Kontrollflussanalyse. Betroffene FastTests und IntegrationTests, `dotnet test AiNetReview.slnx`, `dotnet build AiNetReview.slnx` ohne Warnungen und `git diff --check` laufen vor Abschluss. Ein manueller Audit protokolliert die Trefferzahl und Beispiele aus AiNetReview und AiNetLinter; dessen Verzeichnis bleibt unverändert. Die Umsetzung folgt den Repository-Regeln zu Dokumentation und atomaren Commits.
