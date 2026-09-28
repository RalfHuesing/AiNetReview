---
status: draft
---

# AiNetReview – Konzept

## Intention

AiNetReview soll nach einem Entwicklungstask oder zu einem anderen Zeitpunkt lokal mögliche Qualitäts- und Architekturprobleme in einer C#/.NET-Solution sichtbar machen. Die ausführbare Anwendung prüft die konfigurierten Regeln gegen den **aktuellen** Code und schreibt für Agent und Nutzer verständliche Markdown-Berichte. Die Entscheidung, ob ein Hinweis sinnvoll ist und ob Code geändert wird, bleibt beim Review durch Agent und Nutzer. Das Werkzeug blockiert keinen Build und refaktoriert nichts automatisch.

Die Arbeitsthese bleibt: Code ist für Agenten besonders schwer zu beurteilen, wenn Entscheidungswege, Kontext oder Zustand unübersichtlich sind oder Dokumentation in die Irre führt. Menschliche Lesbarkeit und agentische Verständlichkeit überschneiden sich, sind aber nicht identisch. Der Nutzen späterer fachlicher Regeln wird im Review mit Agent und Nutzer bewertet; das Werkzeug behauptet keinen universellen KI-Komplexitätswert. Eindeutige technische Fehler gehören weiterhin zu Build- und Compilerprüfungen.

## Zielbild

Ein Aufruf der `.exe` lädt die angegebene `.sln` oder `.slnx` und die Regelkonfiguration, führt eine vollständige Analyse aus und erzeugt Markdown-Dateien im Ausgabeordner. Ein fehlgeschlagener oder unvollständiger Lauf darf nicht als „keine Findings“ erscheinen. Jeder Lauf betrachtet allein den aktuellen Code und die aktuelle Konfiguration.

Die Berichte sind die einzigen dauerhaften Ergebnisse des Werkzeugs. Es gibt keinen MCP-Server, keinen Finding-Store, keine gespeicherten Quellcode-Stände oder Snapshots, keine Checksummen zum Vergleich mit früheren Läufen, keine Ereignisketten und kein Archiv früherer Review-Entscheidungen. Eine Bewertung wie `accepted` oder `false-positive`, ihre automatische Unterdrückung und die Zustände `new`, `updated`, `reopened` und `resolved` lassen sich ohne Zustand zwischen Läufen nicht anbieten und gehören nicht zu diesem Zielbild.

Die einzige produktive Startregel bleibt `template-noop`; sie liefert absichtlich keine Findings. Eine testgebundene Fixture-Regel belegt den vollständigen Weg von der Analyse bis zum Markdown-Bericht. Fachliche Review-Regeln werden später gesondert festgelegt. Der Regelvertrag soll ihre Ergänzung ohne Änderungen an CLI, Runner und Berichtsgenerator erlauben.

AiNetLinter bleibt ein getrenntes, nur lesend verwendetes Referenzprojekt für passende Infrastrukturmechanismen. Seine fachlichen Regeln und Build-Gates werden nicht übernommen; seine spätere Ablösung ist kein Bestandteil dieses Vorhabens. Das [ursprüngliche Relaunch-Konzept](Relaunch-Konzept.md) dokumentiert die Motivation, ist aber für Storage, MCP und Verlaufsverhalten überholt.

## Scope

### Muss

- Die Spezifikation in diesem Task-Verzeichnis wird auf einen zustandslosen EXE-zu-Markdown-Ablauf umgeschrieben. Alle verbindlichen Verträge, Beispiele, Abnahmekriterien und Querverweise werden auf Widersprüche zu diesem Zielbild geprüft und bereinigt.
- Die spätere Umsetzung enthält nur die für Konfiguration, Solution-Laden, Regelprüfung, CLI und Markdown-Ausgabe benötigten Produktkomponenten. Bereits begonnene Storage-, MCP-, Fingerprint-, Snapshot-, Verdict- und Zustandsautomaten-Arbeit wird auf tatsächlich noch benötigte Teile geprüft; obsolete Teile werden aus Produktcode, Tests, Abhängigkeiten und Ist-Dokumentation entfernt.
- Findings sind Hinweise zum aktuellen Quellstand mit Quellort, Begründung und aktueller Evidenz. Regeln liefern ihre fachliche Aussage, der generische Runner prüft die Vollständigkeit, und der Berichtsgenerator schreibt die Ergebnisse nachvollziehbar und deterministisch.
- Fehlerfälle, leere vollständige Läufe und die Trennung zwischen produktiver `template-noop`-Regel und Test-Fixture werden automatisiert nachgewiesen.

### Nicht

- Kein MCP-Server und keine zweite Analyse-Schnittstelle.
- Kein persistierter Review-Zustand, keine vorherigen Code-Stände, keine Vergleichs-Checksummen, keine Review-Urteile, kein Verlauf und keine automatische Unterdrückung früher akzeptierter Findings.
- Keine neue fachliche Produktregel, kein automatisches Refactoring, kein Build-Gate für Review-Hinweise und keine Änderung am AiNetLinter-Repository.
- Keine Implementierung oder neue Umsetzungs-Roadmap in dieser Konzeptphase.

## Verifikation

Eine produktive EXE erzeugt mit `template-noop` nach vollständiger Analyse gültige Markdown-Ausgabe ohne Findings. Die Fixture-Regel erzeugt überprüfbare, nach Regel und Quellort geordnete Hinweise mit aktueller Evidenz. Wiederholte Läufe benötigen keinen gespeicherten Vorzustand und geben bei identischer Eingabe inhaltlich identische Berichte aus. Ungültige Konfiguration, Lade- oder Analysefehler sowie Fehler beim Schreiben führen zu einem klaren Fehlschlag. Tests und Repository-Inspektion belegen, dass kein MCP-Zugang und kein persistierter Review-Zustand im Produkt verbleiben.

## Arbeitsgedächtnis (nur Draft)

- Der bisherige Detailvertrag in [Epic 1](epics/01-Eingaben-und-Host.md), [Epic 2](epics/02-Regel-und-Findings.md), [Epic 3](epics/03-Storage-und-Berichte.md), [Epic 4](epics/04-Umsetzung-und-Abnahme.md) und der [Roadmap](roadmap.md) beschreibt noch Storage/MCP und ist für die Neuausrichtung **nicht freigegeben**. Er wird erst nach Klärung der offenen Entscheidungen konsistent ersetzt. `tasks/initial-infrastructure-umsetzen/` ist ein separater, inzwischen überholter Arbeitsstand und wird hier nicht geändert.
- Die Repository-Regeln zu MCP und Storage sowie die Ist-Dokumentation müssen nach der Konzeptfreigabe gegen den tatsächlich geänderten Produktstand bereinigt werden. Während dieses Planungsschritts bleiben Produktcode und Ist-Dokumentation unverändert.
- Offen: Soll ein akzeptierter Hinweis künftig bei jedem Lauf erneut erscheinen? Jede automatische Unterdrückung benötigt eine dauerhafte Entscheidung oder eine explizite Regel-/Konfigurationsausnahme und würde das Ziel „nur aktuelle Analyse, keine gespeicherte Entscheidung“ ändern.
- Offen: Bedeutet „es kommen md-files raus, nicht mehr und nicht weniger“ auch, dass das bisherige EXE-relative Serilog-Dateilogging und generierte Katalog-/JSON-Dateien entfallen? Der Entwurf behandelt Markdown als einziges dauerhaftes Analyseergebnis; die Grenze für sonstige Host-Dateien muss feststehen.
- Für die Umsetzung zu entscheiden: Werden Dateien im dedizierten Ausgabeordner bei jedem Lauf ersetzt, oder legt jeder Lauf einen eigenen Berichtssatz an? Letzteres würde ohne Store trotzdem einen Verlauf von Markdown-Dateien hinterlassen. Empfehlung: ein definierter Berichtssatz, der beim nächsten vollständigen Lauf ersetzt wird, ohne fremde Dateien im Ausgabeordner zu löschen.
