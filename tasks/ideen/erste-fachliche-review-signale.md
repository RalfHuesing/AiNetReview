# Ideen für fachliche Review-Signale

Status: Ideensammlung für weitere fachliche Signale, keine verbindliche Spezifikation und kein implementiertes Produktverhalten. Bereits implementierte Analysen wie `method-control-flow-outliers`, `code-size-candidates`, `duplicate-code-candidates`, `structural-duplication-candidates`, `indirection-drift-candidates` und `missing-test-evidence-candidates` sind in [Current findings](../../docs/review/findings.md) dokumentiert.

## Ziel

AiNetReview soll einem Agenten nach einem Entwicklungstask Stellen zeigen, die eine Prüfung im Zusammenhang verdienen. Eine Messung ist weder ein Fehlernachweis noch ein Refactoring-Auftrag. Insbesondere sind feste Grenzen wie `max-method-length`, `max-class-length` oder `max-file-length` ungeeignet, wenn ihre Überschreitung als unmittelbar zu behebender Verstoß behandelt wird. Das ursprüngliche Konzept beschreibt das Risiko: Eine Methode wird wegen einer Längengrenze mechanisch geteilt, danach eine Datei wegen der nächsten Grenze in Partial-Klassen zerlegt, ohne die eigentliche Verantwortung zu prüfen.

Fachliche Auswertungen sollten deshalb alle nach einem nachvollziehbaren Kriterium relevanten Kandidaten liefern. Eine feste Höchstzahl würde weitere relevante Stellen verbergen. Der Agent liest die betroffenen Stellen und ihre Aufrufer, prüft Absicht und Verantwortung und bespricht erst dann mit dem Nutzer, ob überhaupt Handlungsbedarf besteht.

## Verbleibende Prioritäten

| Priorität | Signal | Warum es für agentische Entwicklung nützlich sein könnte | Einschätzung |
| --- | --- | --- | --- |
| 1 | Über Dateien verteilte Verständniskosten | Zeigt, wenn ein Agent für eine Änderung viele Aufruf- und Zustandsübergänge verfolgen muss. | Hoher Nutzen, schwerer sauber zu messen (jenseits reiner Weiterleitungsketten) |
| 2 | Entwicklung über vollständige Review-Läufe | Wiederholtes Wachstum oder neu entstehende Abhängigkeiten über viele Läufe hinweg können wichtiger sein als ein einmalig hoher Wert. | Nach stabiler Messung und Historie |
| 3 | Konzentration von Entscheidungen und Abhängigkeiten | Macht zentrale Engstellen auf Modul- oder Klassenebene sichtbar. | Ergänzender Überblick (vgl. Type-Dependency-Konzepte) |
| 4 | Dokumentationsbezug | Widersprüchliche Erklärungen zwischen Dokumentation und Code können agentische Änderungen erschweren. | Potenziell wertvoll, aber hohe Gefahr unbelegter Aussagen |

Die Reihenfolge bewertet den erwarteten Nutzen als **Review-Wegweiser**, nicht die Schwere eines möglichen Problems. Sie ist eine Arbeitshypothese und sollte anhand echter Review-Entscheidungen angepasst werden.

## Verteilte Verständniskosten

Agenten müssen Änderungen häufig über Dateien, Aufrufketten und Zustandsübergänge hinweg verstehen. Mögliche Beobachtungen sind ungewöhnlich viele direkt benötigte Typen, tiefe oder breite Aufrufketten, Weitergabe von veränderlichem Zustand und eine fachliche Operation, deren wesentliche Schritte über viele Bereiche verteilt sind. Ein Graphwert allein genügt nicht: Der Bericht müsste die tatsächlich betroffenen Symbole und Beziehungen zeigen, damit der Agent die Aussage prüfen kann. Framework- und Infrastrukturcode braucht möglicherweise andere Vergleichsgruppen als fachlicher Code.

## Veränderungen statt nur Momentaufnahmen

Mit mehreren vollständigen Review-Läufen lassen sich Fragen stellen wie: Welche Methode wächst über mehrere Änderungen? Welche Klasse erhält immer neue Abhängigkeiten? Wo konzentrieren sich neue Entscheidungswege? Solche Trends können einen sinnvollen Review-Anlass liefern, obwohl der absolute Wert unauffällig ist. Ein unveränderter historischer Ausreißer kann dagegen bewusst akzeptiert sein. Dafür müssen Messdefinition und Symbolzuordnung über Läufe stabil genug sein; umbenannte oder verschobene Symbole dürfen nicht vorschnell als neue Entwicklung interpretiert werden. Die Idee sollte ohne Git als Laufzeitvoraussetzung funktionieren, passend zur bestehenden Produktkonzeption.

## Konzentration und Verantwortungen

Auf Projekt- und Modulniveau wäre interessant, ob ein kleiner Teil der Klassen einen großen Teil der Entscheidungen, Aufrufbeziehungen oder Änderungen trägt. Das ist zunächst eine Landkarte für die Navigation. Für eine Aussage über zu viele Verantwortungen braucht es konkrete Evidenz, etwa unabhängige Aufrufergruppen oder nicht zusammenhängende Abhängigkeitsbereiche. Reine Klassen- oder Dateilänge liefert diese Evidenz nicht. Eine große Datei mit einem klaren Zweck kann völlig angemessen sein.

## Dokumentation als Kontext

Dokumentation und Code können auseinanderlaufen. Dies wäre für Agenten nützlich, ist statisch aber schwer zuverlässig zu beweisen: Eine anders formulierte Beschreibung ist nicht automatisch falsch. Solche Hinweise brauchen besonders konkrete Quellen und vorsichtige Formulierungen.

## Wie wir den Nutzen prüfen könnten

Eine kleine Liste an realen Codebasen erzeugen und jeden Kandidaten von einem Agenten mit Nutzerkontext beurteilen lassen: hilfreicher Hinweis, erwartbare Ausnahme, falsch gemessen oder ohne Erkenntniswert. Festhalten, **welche konkrete Frage** die Liste ausgelöst hat und ob die Prüfung zu einer Änderung, einer begründeten Akzeptanz oder keiner Entscheidung führte. Besonders auf Fälle achten, in denen bloße Größe zu mechanischem Aufteilen verleiten würde. Erst nach solchen Beobachtungen weitere Messwerte, Gewichtungen oder dauerhafte Findings festlegen.
