---
status: draft
---

# Audit-Landkarte für fokussierte Agentenaufträge

## Intention

Ein auditierender Agent soll einen zusammenhängenden Arbeitsbereich mit seinen unterschiedlichen Signalen und seinem Testkontext untersuchen können, ohne dieselben Dateien aus mehreren Analyseberichten zusammensuchen zu müssen. AiNetReview ergänzt dafür die bestehenden Berichte um eine nachvollziehbare Audit-Landkarte mit eindeutig adressierbaren Arbeitspaketen.

Das Tool bleibt generisch für die geladenen C#-Quellen unterschiedlichster Anwendungen, etwa MCP-Server, Blazor-Server und Bibliotheken. Es leistet möglichst gute technische Vorarbeit für den Audit-Agenten: Signale bündeln, belegte Beziehungen zugänglich machen und den nötigen Einstiegskontext zeigen. Fachliche Bedeutung und tatsächliche Verantwortungsgrenzen untersucht der Agent anhand des jeweiligen Projekts.

Die Landkarte organisiert den Prüfauftrag. Sie bewertet keine Findings, behauptet keine gemeinsamen Fehlerursachen und verlangt keine Refaktorierung. Ein Paket kann Größen-, Kontrollfluss-, Dead-Code-, Duplikations- und Testpfadsignale gemeinsam enthalten. Seine Bezeichnung beschreibt den untersuchten Codebereich, nicht die Analyseart.

## Entscheidung und Vorgehen

Wir entwickeln eine kleine erste Produktversion mit deterministischer technischer Gruppierung. Die erste reale Landkarte entsteht während der Umsetzung und ist ein Abnahmebeispiel. Eine separate PoC-, Benchmark- oder längere Messphase ist keine Voraussetzung für den Beginn der Umsetzung.

Die erste Version bündelt Findings um gemeinsame Quelltypen und nutzt belegte statische Beziehungen zu Tests und angrenzenden Quellen als Kontext. Projekt- und Verzeichnisgrenzen dienen der Übersicht und als Rückfall für dateibezogene Signale. Ein Ordner, Namespace, Aufruf oder Typname beweist keine fachliche Zusammengehörigkeit. Es werden weder Geschäftsdomänen noch Anwendungstypen automatisch erkannt oder benötigt.

Die Pakete sind deterministische Untersuchungsvorschläge, deren fachlicher Zuschnitt unvollkommen sein darf. Der Agent kann sie während der Untersuchung teilen, zusammen betrachten oder weiteren Kontext hinzunehmen. Dabei bleiben die ursprünglichen Finding-IDs, deren primäre Paketzuordnung und der beauftragte Finding-Scope nachvollziehbar. Die Landkarte muss alle Findings erhalten und ihre Zuordnungsgründe korrekt zeigen; sie muss keine ideale Architekturzerlegung liefern.

Ein reiner Prompt würde Zuordnung, Vollständigkeitskontrolle und Navigation bei jedem Audit erneut dem Agenten überlassen. Diese drei Aufgaben werden im Produkt gelöst; der Prompt steuert die kontextbezogene Untersuchung und Bewertung.

## Gelesene Ausgangsbasis

- [Current findings](../../docs/review/findings.md) und die Implementierungen von [FindingDraft](../../src/AiNetReview.Core/Findings/FindingDraft.cs), [ReviewFinding](../../src/AiNetReview.Core/Analysis/ReviewFinding.cs), [ReviewFindingBuilder](../../src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs) und [MarkdownReportWriter](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs) wurden gelesen.
- Validierte Findings enthalten Symbolidentitäten, Quellen, Belege und getrennte Kontext- und Subjektvorkommen. Bereits vorhandene Beziehungen verbinden Findings über Projekt- und Symbolidentität. Diese Beziehungen ergeben noch keine Arbeitspakete nach Verantwortung; Markdown-Related-Links zeigen derzeit auf Analyseberichte.
- Der [SolutionReferenceIndex](../../src/AiNetReview.Core/Analysis/SolutionReferenceIndex.cs) bietet semantische Referenzen einschließlich Testherkunft. Der [Testpfad-Graph](../../src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceSemanticGraphBuilder.cs) ist eine vorhandene spezialisierte Implementierung, kein bereits exportierter allgemeiner Modulgraph.
- Der strukturell gelesene Lauf `20261001T105806Z-db0d277a` enthält in `changed-files` 284 Findings: 234 production und 50 tests. Mehrere Analyseberichte enthalten Signale zu denselben Quellen. Die Findings wurden für dieses Konzept nicht inhaltlich bewertet.

## Scope

### Muss

#### Zusätzliche Sicht und unveränderter Prüfauftrag

- Jeder neue Review-Lauf veröffentlicht zusätzlich `audit-map/changed-files/index.md`, Paketberichte und ein JSON-Manifest. Eine getrennte `audit-map/all-findings/`-Sicht verwendet denselben Vertrag für den vollständigen Findings-Bestand.
- Der bestehende Root-Index verlinkt die zusätzliche Navigation. Bestehende Analyseberichte, Herkunftsbereiche, CLI-Antwort und Baseline-Verhalten bleiben gültig. Die Landkarte ist eine weitere Sicht auf dieselben validierten Findings, keine neue Review-Analyse.
- `changed-files` verwendet exakt die vorhandene Reporting-Auswahl, einschließlich der snapshotweiten Auswahl für `missing-test-evidence-candidates`. Die vollständige Sicht benötigt weiterhin einen ausdrücklich beauftragten vollständigen Audit.
- Quellen ohne eigenes Finding dürfen als Prüfkontext erscheinen. Das erweitert den Finding-Auftrag nicht. Verweise auf Findings außerhalb der gewählten Sicht müssen als außerhalb des Auftrags erkennbar sein und dürfen deren Details nicht in die begrenzte Sicht übernehmen.
- Ein fehlerhafter oder abgebrochener Lauf veröffentlicht keine unvollständige Landkarte. Bestehende atomare Veröffentlichung, Pfadgrenzen, externe Auditziele und Erhaltung älterer Läufe gelten auch für die neuen Dateien.

#### Nachvollziehbare Gruppierung

- Ein technischer Produktionsbereich wird durch Projektpfad und den äußersten enthaltenden Quelltyp bestimmt. Member-, Typ- und Fragment-Findings desselben Typs werden damit über Analysearten hinweg gemeinsam vorbereitet; verschachtelte Typen gehören zu diesem Schwerpunkt. Partial-Deklarationen behalten sämtliche ausgewiesenen Orte. Ein Dateifinding gehört bei genau einem äußersten Quelltyp zu dessen Schwerpunkt; bei mehreren oder keinem entsteht ein eigener Dateibereich mit Verweisen auf vorhandene Typschwerpunkte. Nicht auf einen Quelltyp auflösbare Subjekte erhalten ebenfalls einen Dateibereich. Keine Rückfallzuordnung darf ein Finding verlieren.
- Projekte und Quellordner gliedern die Übersicht über diese Schwerpunkte. Sie vereinigen Pakete nicht automatisch. Namen werden aus belegten Typ- und Quellnamen abgeleitet; sie behaupten keine erkannten Geschäftsdomänen. Gleiche Symbolidentität ist eine direkte Zuordnung, gemeinsame Typ- oder Dateiumgebung eine technische Vorgruppierung, Referenzbeziehungen sind ausgewiesener Kontext. Diese Gründe bleiben unterscheidbar.
- Eine nichtgenerierte Testdatei wird einem Produktionsbereich zugeordnet, wenn ihre semantisch aufgelösten Referenzen auf nichtgenerierte Produktionsquellen genau einen solchen Bereich benennen. Die Begründung und konkreten Referenzorte bleiben sichtbar. Das ist eine Zuordnungshilfe, kein Nachweis von Testausführung, Assertion-Qualität oder Laufzeitabdeckung.
- Bei Referenzen auf mehrere Produktionsbereiche, fehlenden aufgelösten Referenzen oder erkannter Bindungsunsicherheit bleiben die Testsubjekte in eigenen technischen Testbereichen nach derselben Typ-/Dateiregel wie Produktionssubjekte. Erkannte Produktionsbezüge werden als Kontextbeziehungen gezeigt. Eine fehlende Zuordnung darf keine fehlenden Tests behaupten.
- Testzuordnungen werden aus dem geladenen Quellkontext ermittelt, nicht nur aus Findings. Dadurch können relevante Testdateien ohne eigene Signale sichtbar werden. Der Kontext umfasst die zugeordneten Testdateien, die direkt für die Paket-Findings ausgewiesenen Beleg- und Symbolorte sowie aufgelöste direkte eingehende und ausgehende Referenzen des untersuchten Typs auf andere nichtgenerierte Quelltypen. Jede Referenz nennt Ziel und konkreten Quellort. Es erfolgt keine rekursive Aufnahme aller erreichbaren Quellen. Bereiche ohne eigene Findings dürfen als Kontextziele erscheinen, ohne einen zusätzlichen Finding-Prüfauftrag zu erhalten.
- Findings werden anhand ihrer Subjektvorkommen den technischen Bereichen zugeordnet. Kontextsymbole und Testpfadbelege ändern die Finding-Herkunft nicht. Dateifindings und zusätzliche Partial-Deklarationen behalten ihre nachgewiesenen Orte.
- Liegen die Subjekte eines Findings in einem Bereich, gehört es zu dessen Paket. Liegen sie in mehreren Bereichen, erhält es ein gemeinsames Paket für diese sortierte Bereichsmenge, mit Verweisen aus den beteiligten Bereichen. Das gilt insbesondere für Duplikate, Fragmente und Weiterleitungsketten; jedes beteiligte Vorkommen bleibt zugänglich.
- Jedes Finding besitzt je Sicht genau ein primär zuständiges Paket. Verweise aus anderen Paketen erzeugen keinen zweiten Prüfauftrag. Beziehungen zwischen Paketen führen nicht zu deren transitiver Zusammenlegung; ein gemeinsamer Helfer darf nicht die gesamte Anwendung zu einem Cluster verbinden.
- Für identische validierte Ergebnisse, Quellen und Sicht ist die Zuordnung unabhängig von Enumerationsreihenfolgen deterministisch. Jede Zuordnung nennt ihren Grund. Es gibt keine Top-N-Auswahl, keine künstliche Höchstzahl von Findings und keine ausgeblendeten Restmengen. Große Pakete erhalten Navigation nach Dateien und Symbolen.

#### Agententaugliche Ausgabe

- Jedes Finding erhält eine eindeutige ID aus seiner vorhandenen Identität: Analyse, Projekt, repräsentative Quelle, Subject-ID und Discriminator. Paket-IDs ergeben sich aus der Bereichsidentität beziehungsweise der sortierten Bereichsmenge. IDs bleiben bei identischen Eingaben gleich; Umbenennungen und Verschiebungen versprechen keine historische Identität.
- Paketberichte zeigen ID, Name, Sicht, zuständige Finding-IDs, Analysearten, Quellen, Testkontext, Zuordnungsgründe und Beziehungen zu anderen Paketen. Sie behalten die Originalsignale, Messwerte, Belege, Herkunft und Unsicherheiten bei. Links beziehungsweise Anker führen zu konkreten Findings, nicht ausschließlich zu einer gesamten Analysedatei.
- Die Ausgabe kennzeichnet den Zuschnitt als technische Vorgruppierung, nennt angewendete Rückfälle und erkannte Unsicherheiten und macht ausdrücklich, dass statisch nicht belegte Beziehungen fehlen können. Eine gemeinsame Paketzuordnung behauptet weder eine gemeinsame Fehlerursache noch eine unabhängige Änderbarkeit. Der Agent erhält eine belastbare Grundlage, keine fertige fachliche Bewertung.
- Das versionierte JSON-Manifest enthält Lauf und Sicht, die Findings samt ihren Originalidentitäten und relevanten Belegen, die Paketzuordnung, ausgewiesenen Kontext und Paketbeziehungen. Markdown und JSON entstehen aus derselben Aufbereitung; ein Agent muss Markdown nicht zur Vollständigkeitskontrolle parsen.
- Die Paketübersicht nennt eindeutige Findings-Zahlen und macht eine leere Sicht ausdrücklich sichtbar. Die Summe der primär zugeordneten Findings entspricht exakt der Findings-Zahl der Sicht. Überlappender Kontext wird nicht als zusätzlicher Finding-Bestand gezählt.
- Die gemeinsame Audit-Anleitung enthält einen konkreten Paketauftrag, etwa: „Prüfe im Lauf X, Sicht changed-files, Paket Y alle zuständigen Findings anhand von Implementierung, Aufrufern, Verträgen und Tests. Begründe jede Einstufung und benenne offenen Kontext sowie nicht geprüfte Pakete.“ Ein Paketauftrag ist keine automatische Freigabe zu Änderungen.
- Die vorhandenen Fragen und Einstufungen gelten weiter: false positive, acceptable design, needs clarification oder actionable. Der Agent untersucht verwandte Signale gemeinsam, hält seine Begründung aber für jedes Finding nachvollziehbar. Ein teilweise bearbeiteter Paketauftrag darf keinen vollständigen Audit behaupten.

### Nicht

- Keine separate Vorstudie, kein wegwerfbarer PoC und keine quantitativ belegte Zeitersparnis als Umsetzungsvoraussetzung.
- Keine LLM-Abhängigkeit, externe Dienste, MCP-Laufzeitabhängigkeit oder automatische Deutung von Geschäftsdomänen in der Berichtserzeugung.
- Keine Erkennung von Anwendungskategorien und keine für MCP, Blazor oder andere Frameworks fest eingebauten Paketregeln. Vorhandene Analysebelege bleiben erhalten; daraus folgt keine vollständige Gruppierung von Razor-, XAML-, JavaScript- oder anderen Nicht-C#-Quellen.
- Keine neue Modulkonfiguration, frei konfigurierbare Clusterregeln oder manuelle Paketpflege in dieser ersten Version.
- Keine Bewertung, Prioritäts- oder Gesamtscores, neuen Analysesignale, Änderung der bisherigen Schwellen oder Einschränkung der vorhandenen Finding-Auswahl.
- Keine automatische Refaktorierung, Agentenausführung oder parallele Umsetzung mehrerer Pakete. Ein gemeinsamer Kontext ist keine Garantie unabhängiger Änderungen.
- Keine persistierten Review-Entscheidungen, Bearbeitungsstatus, historische Wiederöffnung oder neue Baseline-Semantik. Der Agent berichtet die Untersuchungsergebnisse; die Landkarte bleibt ein Laufartefakt.
- Keine automatische Ableitung von Dokumentationszuständigkeiten oder vollständige Modellierung dynamischer Aufrufe, Reflection, DI und Laufzeit-Testabdeckung.
- Keine Migration oder nachträgliche Bearbeitung vorhandener veröffentlichter Auditläufe.

## Verifikation und erste reale Anwendung

Die Umsetzung prüft die Verträge mit automatisierten Tests und erzeugt anschließend eine erste reale Landkarte beim regulären Repository-Audit. Der gelesene Lauf dient als strukturelles Beispiel; die neue Funktion wird gegen einen neuen Snapshot ausgeführt. Die frühere Anzahl von 284 Findings ist kein festgeschriebener erwarteter Wert.

Die automatisierte Abnahme deckt mindestens ab:

- Vollständige, eindeutige Zuordnung aller Findings je Sicht, einschließlich leerer Ergebnisse, einzelner Dateien, mehrfach betroffener Symbole und bereichsübergreifender Subjekte.
- Typbezogene Gruppierung über Analysearten hinweg, verschachtelte und partielle Typen sowie Dateirückfälle bei mehreren, keinen oder nicht auflösbaren Quelltypen; keine automatische Vereinigung allein durch denselben Quellordner.
- Generische Zuordnung anhand von Symbolen und Quellen bei unterschiedlich strukturierten C#-Fixtures, ohne Abhängigkeit von Anwendungskategorien oder fachlich interpretierten Namenskonventionen.
- Gleiche IDs und Zuordnung bei identischen Eingaben mit unterschiedlicher Enumerationsreihenfolge; unveränderte Originalsignale und Projektrollen.
- Eindeutige Testzuordnung sowie separate Testbereiche bei mehreren Bezügen, keinem Bezug und Bindungsunsicherheit; Testkontext ohne eigenes Finding.
- Keine transitive Verschmelzung durch gemeinsame Testhelfer oder andere Paketbeziehungen; vollständige Vorkommen in gemeinsamen Paketen.
- Nachvollziehbare direkte Referenzorte als begrenzter Kontext, auch für Quellen ohne eigenes Finding; keine rekursive Kontextausweitung und sichtbare Rückfälle beziehungsweise Unsicherheiten.
- Unveränderte Baseline- und Scope-Auswahl, insbesondere snapshotweite Testpfadsignale und keine Übernahme ausgeschlossener Finding-Details in `changed-files`.
- Übereinstimmung zwischen Manifest, Übersicht und Paketberichten; auflösbare Finding-Links, sichere Pfade und vollständige Veröffentlichung auch bei externen Auditwurzeln.
- Keine veröffentlichte Teilmenge bei Fehler oder Abbruch und Erhaltung bisheriger Berichtspfade sowie älterer Läufe.

Bei der ersten realen Anwendung wird für ein repräsentatives Paket geprüft, ob ein Agent vom Paketbericht aus die zuständigen Findings, Quellen und zugeordneten Tests untersuchen und jede Einstufung unter ihrer ID berichten kann. Diese begrenzte Untersuchung ist Teil der Umsetzung und ihrer Abnahme, kein Auftrag zur Behebung sämtlicher Repository-Findings.

Die Abnahme verlangt korrekte Anwendung der technischen Zuordnungsregeln, bedienbare Navigation und einen konkret ausführbaren Paketauftrag. Sie verlangt weder perfekte fachliche Cluster noch eine Vergleichsstudie oder ein inhaltlich abgeschlossenes Audit des gesamten Repositories. Eine fachlich unpassende Vorgruppierung darf der Agent mit nachvollziehbarer Finding-Zuordnung korrigieren; verlorene Findings, falsche Referenzbelege oder versteckte Unsicherheiten sind dagegen Fehler. Technische Zuordnungsfehler werden innerhalb dieser ersten Version korrigiert, ohne die Nicht-Ziele still zu erweitern.
