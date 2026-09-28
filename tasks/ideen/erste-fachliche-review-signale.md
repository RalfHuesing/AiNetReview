# Ideen für erste fachliche Review-Signale

Status: Ideensammlung, keine verbindliche Spezifikation und kein implementiertes Produktverhalten. Der initiale Produktstand enthielt bewusst noch keine fachliche Regel.

## Ziel

AiNetReview soll einem Agenten nach einem Entwicklungstask Stellen zeigen, die eine Prüfung im Zusammenhang verdienen. Eine Messung ist weder ein Fehlernachweis noch ein Refactoring-Auftrag. Insbesondere sind feste Grenzen wie `max-method-length`, `max-class-length` oder `max-file-length` ungeeignet, wenn ihre Überschreitung als unmittelbar zu behebender Verstoß behandelt wird. Das ursprüngliche Konzept beschreibt das Risiko: Eine Methode wird wegen einer Längengrenze mechanisch geteilt, danach eine Datei wegen der nächsten Grenze in Partial-Klassen zerlegt, ohne die eigentliche Verantwortung zu prüfen.

Die erste fachliche Auswertung sollte deshalb alle nach einem nachvollziehbaren Kriterium relevanten Kandidaten liefern. Eine feste Höchstzahl würde weitere relevante Stellen verbergen. Der Agent liest die betroffenen Stellen und ihre Aufrufer, prüft Absicht und Verantwortung und bespricht erst dann mit dem Nutzer, ob überhaupt Handlungsbedarf besteht.

## Prioritäten

| Priorität | Signal | Warum es für agentische Entwicklung nützlich sein könnte | Einschätzung |
| --- | --- | --- | --- |
| 1 | Methoden als statistische Ausreißer bei Umfang und Entscheidungswegen | Lenkt den Blick auf lokal schwer zu verfolgende Abläufe; einfach zu messen und zu erklären. | Bester erster Versuch |
| 2 | Gleichzeitige Auffälligkeit in mehreren einfachen Messgrößen | Ein langer linearer Mapper und eine verzweigte Ablaufsteuerung werden unterscheidbar. | Früh ergänzen, ohne Gesamtscore |
| 3 | Über Dateien verteilte Verständniskosten | Zeigt, wenn ein Agent für eine Änderung viele Aufruf- und Zustandsübergänge verfolgen muss. | Hoher Nutzen, schwerer sauber zu messen |
| 4 | Entwicklung über vollständige Review-Läufe | Wiederholtes Wachstum oder neu entstehende Abhängigkeiten können wichtiger sein als ein einmalig hoher Wert. | Nach stabiler Messung und Historie |
| 5 | Konzentration von Entscheidungen und Abhängigkeiten | Macht zentrale Engstellen auf Modul- oder Klassenebene sichtbar. | Ergänzender Überblick |
| 6 | Duplikate und auseinanderlaufende ähnliche Implementierungen | Agenten können lokale Muster kopieren, die später unterschiedlich weiterentwickelt werden. | Nur mit konkreten, überprüfbaren Belegen |
| 7 | Klassen- und Dateiumfang sowie Kohäsion | Kann auf vermischte Verantwortungen hinweisen, ist allein aber besonders leicht fehlzuinterpretieren. | Später, zunächst Kontextmetrik |
| 8 | Test- und Dokumentationsbezug | Fehlende Absicherung oder widersprüchliche Erklärungen können agentische Änderungen erschweren. | Potenziell wertvoll, aber hohe Gefahr unbelegter Aussagen |

Die Reihenfolge bewertet den erwarteten Nutzen als **Review-Wegweiser**, nicht die Schwere eines möglichen Problems. Sie ist eine Arbeitshypothese und sollte anhand echter Review-Entscheidungen angepasst werden.

## 1. Erster Versuch: Methoden-Ausreißer

### Ausgabe statt Grenzwert

Für eine klar definierte Vergleichsgruppe werden Anzahl der Methoden, Median und ausgewählte Perzentile sowie **alle Methoden, die das Relevanzkriterium erfüllen**, ausgegeben. Als erster Ansatz bietet sich ein konfigurierbares Perzentil X pro Messgröße an. Das Perzentil entscheidet dann über die Aufnahme in den Bericht, nicht darüber, ob der Code „schlecht“ ist. Die genaue Definition von Relevanz, insbesondere bei mehreren Messgrößen, bleibt zu klären. Jede Zeile nennt Symbol, Quellort, Messwerte und Rang. Bei kleinen Gruppen sind Rang und Gruppengröße aussagekräftiger als ein scheinbar präzises Perzentil. Der arithmetische Durchschnitt kann zusätzlich erscheinen, sollte wegen großer Ausreißer aber nicht der Leitwert sein.

Ein mögliches Berichtsformat mit rein fiktiven Vergleichswerten:

```text
Produktionsmethoden in Projekt X: 312
Codezeilen: Median 12, P90 37, P95 55
Beispielauswahl: alle Methoden mit Codezeilen >= P95

Rang  Symbol                         Codezeilen  Entscheidungen  Ort
1     ReviewRunner.RunAsync          ...         ...             src/.../ReviewRunner.cs:32
...   jede weitere Methode, die das Kriterium erfüllt
```

`ReviewRunner.RunAsync()` ist ein konkreter Prüf-Kandidat: In dem bei dieser Diskussion gelesenen Arbeitsstand umfasste die Methode ungefähr 140 Quellzeilen und verband Validierung, Regelausführung, Finding-Abgleich und Commit. Daraus folgt **nicht**, dass sie geteilt werden muss. Eine Prüfung könnte ergeben, dass ein zusammenhängender Ablauf an einer Stelle besser verständlich ist; sie könnte ebenso eine vermischte Verantwortung aufdecken. Die Zeilenzahl ist eine Momentaufnahme und muss bei einem späteren Review neu gemessen werden.

### Einfache Messgrößen

- **Umfang:** Quellspanne und Codezeilen getrennt ausweisen. Vor einer Umsetzung festlegen, wie Leerzeilen, Kommentare, lokale Funktionen, Lambdas und mehrzeilige Ausdrücke gezählt werden.
- **Entscheidungswege:** Sichtbare Verzweigungen und Schleifen zählen, etwa `if`, `switch`-Arme, bedingte Ausdrücke, Schleifen und `catch`. Die genaue Definition muss dokumentiert und deterministisch sein. Für den ersten Versuch ist ein nachvollziehbarer Zähler besser als ein undurchsichtiger „Komplexitätswert“.
- **Verschachtelung:** Als zusätzlicher Hinweis denkbar, wenn die reine Anzahl der Entscheidungen die Leseschwierigkeit nicht ausreichend erklärt.

Eine lange, lineare Mapping-Methode kann unproblematisch sein. Eine kürzere Methode mit vielen Entscheidungen kann deutlich mehr Aufmerksamkeit verlangen. Deshalb sollten die relevanten Methoden für Umfang und Entscheidungswege nebeneinander sichtbar sein; Methoden, die bei beidem auffallen, können hervorgehoben werden. Ob ein Kriterium allein genügt oder mehrere kombiniert werden, ist offen. Kein zusammengewichteter Qualitätsscore in der ersten Version.

### Vergleichsgruppe und Stabilität

- Produktionscode, Testcode und generierter Code getrennt betrachten. Generierten Code üblicherweise nicht in die menschliche Review-Liste aufnehmen.
- Methoden mit Methoden vergleichen; Konstruktoren, Accessors und lokale Funktionen nur nach einer bewussten Definition einbeziehen. Klassen und Dateien bekommen eigene Verteilungen.
- Projekt und Solution als mögliche Ebenen ausweisen. Eine große Solution mit sehr verschiedenen Projekttypen kann eine globale Verteilung verzerren.
- Gruppengröße, Zählweise, Gleichstände und Sortierung im Bericht offenlegen. Gleiche Messwerte brauchen eine deterministische Reihenfolge.
- Keine feste Obergrenze für die Ausgabe. Alle Methoden, die das konfigurierte Kriterium erfüllen, erscheinen im Bericht; auch Gleichstände an der Auswahlgrenze dürfen nicht willkürlich abgeschnitten werden.
- Ein Perzentil als Auswahlgrenze muss samt Berechnungsmethode, Vergleichsgruppe und wirksamem Wert im Bericht stehen. Bei sehr kleinen oder nahezu gleichförmigen Gruppen kann eine reine Perzentilauswahl wenig trennscharf sein; der Umgang damit bleibt zu klären.

## 2. Mehrere schwache Signale gemeinsam betrachten

Ein Einzelwert ist meist mehrdeutig. Umfang, Entscheidungswege und gegebenenfalls Verschachtelung können gemeinsam zeigen, **warum** eine Methode oben auf der Liste steht. Die Ausgabe sollte die Rohwerte und die konkrete Code-Stelle enthalten. Eine Methode nur deshalb als dringend zu markieren, weil sie in mehreren oberen Perzentilen liegt, wäre bereits eine Wertung, die erst mit Review-Erfahrung gerechtfertigt wäre.

Weitere Kombinationen für spätere Versuche: großer lokaler Ablauf plus viele externe Aufrufe; große Klasse plus viele verschiedene Abhängigkeitsbereiche; starke Änderung seit dem letzten Lauf plus hohe strukturelle Auffälligkeit.

## 3. Verteilte Verständniskosten

Agenten müssen Änderungen häufig über Dateien, Aufrufketten und Zustandsübergänge hinweg verstehen. Mögliche Beobachtungen sind ungewöhnlich viele direkt benötigte Typen, tiefe oder breite Aufrufketten, Weitergabe von veränderlichem Zustand und eine fachliche Operation, deren wesentliche Schritte über viele Bereiche verteilt sind. Ein Graphwert allein genügt nicht: Der Bericht müsste die tatsächlich betroffenen Symbole und Beziehungen zeigen, damit der Agent die Aussage prüfen kann. Framework- und Infrastrukturcode braucht möglicherweise andere Vergleichsgruppen als fachlicher Code.

## 4. Veränderungen statt nur Momentaufnahmen

Mit mehreren vollständigen Review-Läufen lassen sich Fragen stellen wie: Welche Methode wächst über mehrere Änderungen? Welche Klasse erhält immer neue Abhängigkeiten? Wo konzentrieren sich neue Entscheidungswege? Solche Trends können einen sinnvollen Review-Anlass liefern, obwohl der absolute Wert unauffällig ist. Ein unveränderter historischer Ausreißer kann dagegen bewusst akzeptiert sein. Dafür müssen Messdefinition und Symbolzuordnung über Läufe stabil genug sein; umbenannte oder verschobene Symbole dürfen nicht vorschnell als neue Entwicklung interpretiert werden. Die Idee sollte ohne Git als Laufzeitvoraussetzung funktionieren, passend zur bestehenden Produktkonzeption.

## 5. Konzentration und Verantwortungen

Auf Projekt- und Modulniveau wäre interessant, ob ein kleiner Teil der Klassen einen großen Teil der Entscheidungen, Aufrufbeziehungen oder Änderungen trägt. Das ist zunächst eine Landkarte für die Navigation. Für eine Aussage über zu viele Verantwortungen braucht es konkrete Evidenz, etwa unabhängige Aufrufergruppen oder nicht zusammenhängende Abhängigkeitsbereiche. Reine Klassen- oder Dateilänge liefert diese Evidenz nicht. Eine große Datei mit einem klaren Zweck kann völlig angemessen sein.

## 6. Ähnliche Implementierungen und Drift

Für agentische Entwicklung besonders relevant ist kopierter oder nachgebauter Code: Ein Agent folgt einem vorhandenen Muster, später ändern sich die Kopien unterschiedlich. Ein Hinweis sollte betroffene Methoden nebeneinander und die konkrete strukturelle Ähnlichkeit zeigen. Reine Textähnlichkeit erzeugt bei trivialen Mappings und Boilerplate viel Rauschen. Diese Analyse wäre eher ein späteres Experiment als die erste fachliche Regel.

## 7. Tests und Dokumentation als Kontext

Ein schwer zu ändernder Bereich kann auffallen, weil Tests seine beobachtbaren Verträge nicht gut abdecken oder weil Dokumentation und Code auseinanderlaufen. Beides wäre für Agenten nützlich, ist statisch aber schwer zuverlässig zu beweisen: Ein nicht gefundener Test ist kein Nachweis fehlender Tests; eine anders formulierte Beschreibung ist nicht automatisch falsch. Solche Hinweise brauchen besonders konkrete Quellen und vorsichtige Formulierungen.

## Offene Produktfrage: Rangliste oder dauerhaftes Finding?

Die bestehende Finding-Architektur vergibt stabile IDs und erlaubt `accepted` oder `false-positive` mit Wiederöffnung bei relevanter Änderung. Auch bei einer Perzentilgrenze ist die Auswahl relativ: Eine unveränderte Methode kann unter die Auswahlgrenze fallen, weil anderswo Code wächst, und später ohne eigene Änderung wieder darüber liegen. Würde jeder ausgewählte Kandidat als Finding gespeichert, könnten `resolved` und `reopened` allein durch Änderungen an anderen Methoden entstehen. Das könnte Entscheidungen und Berichte unnötig unruhig machen.

Für den ersten Versuch erscheint deshalb eine **Momentaufnahme im Bericht** plausibel. Dauerhafte Findings sollten erst entstehen, wenn eine Aussage mit stabiler Identität, klarer Evidenz und sinnvoller Wiederöffnungsbedingung formuliert werden kann. Ob AiNetReview dafür einen zusätzlichen Berichtstyp erhält oder die vorhandenen Regelverträge erweitert werden, ist eine spätere Produktentscheidung; dieses Dokument legt keinen neuen Vertrag fest.

## Wie wir den Nutzen prüfen könnten

Eine kleine Liste an realen Codebasen erzeugen und jeden Kandidaten von einem Agenten mit Nutzerkontext beurteilen lassen: hilfreicher Hinweis, erwartbare Ausnahme, falsch gemessen oder ohne Erkenntniswert. Festhalten, **welche konkrete Frage** die Liste ausgelöst hat und ob die Prüfung zu einer Änderung, einer begründeten Akzeptanz oder keiner Entscheidung führte. Besonders auf Fälle achten, in denen bloße Größe zu mechanischem Aufteilen verleiten würde. Erst nach solchen Beobachtungen weitere Messwerte, Gewichtungen oder dauerhafte Findings festlegen.
