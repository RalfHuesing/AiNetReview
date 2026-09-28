# Checksum-gebundene Ignore-Liste statt Finding-Historie

Status: Produktidee, keine verbindliche Spezifikation und kein implementiertes Verhalten. Sie ist eine mögliche Vereinfachung des bisherigen Finding- und Storage-Konzepts, falls echte Audits auf großen Repositories Bedarf für die Unterdrückung wiederholter Kandidaten zeigen.

## Motivation

Ein Audit erzeugt Berichte, die ein Agent mit dem Menschen prüft. Manche Kandidaten bleiben bewusst unverändert. Auf einem Repository mit etwa 180.000 LOC könnten dieselben akzeptierten Kandidaten bei jedem Folgelauf viel wiederholte Arbeit verursachen. Dafür braucht es möglicherweise keine vollständige Run-Historie mit Ereignissen, Snapshots und Zustandsautomat. Eine kleine, versionierbare Ignore-Liste könnte ausreichen.

## Idee

Ein Ignore-Eintrag gilt für **einen konkreten Kandidaten** und den Codezustand, in dem er akzeptiert wurde. Er enthält mindestens eine stabile Kandidatenkennung aus Regel-ID, Projektpfad, Quellpfad und betroffenem Symbol beziehungsweise Treffer sowie einen SHA-256-Hash der betroffenen Quelldatei. `file.cs + checksum` allein wäre zu grob: Es könnte unbeabsichtigt alle verschiedenen Hinweise in derselben Datei unterdrücken. Die genaue Kennung und das JSON-Schema sind noch festzulegen.

Ein Agent bekommt den Auftrag, einen geprüften Kandidaten zu ignorieren, und ruft dafür einen künftigen CLI-Befehl auf. Die EXE bestimmt die Kennung und berechnet die aktuelle Checksum selbst; der Agent muss die JSON-Datei nicht von Hand bearbeiten. Der Befehl sollte prüfen, dass sich die Quelle seit dem zugehörigen Audit nicht geändert hat. Name und Parameter des Befehls sind offen.

Bei einem **vollständig erfolgreichen Audit**:

| Zustand | Verhalten |
| --- | --- |
| Kandidat vorhanden, Datei-Checksum und Regelversion unverändert | Nur diesen Kandidaten im normalen Bericht unterdrücken. |
| Datei-Checksum geändert | Ignore-Eintrag entfernen und den Kandidaten neu bewerten. Er erscheint nur, wenn er weiterhin das Relevanzkriterium erfüllt. |
| Regelversion geändert | Ignore-Eintrag entfernen und den Kandidaten nach neuer Regelbedeutung bewerten. |
| Quelldatei nicht mehr vorhanden | Ignore-Eintrag entfernen. |
| Kandidat gerade nicht relevant, Datei unverändert | Eintrag behalten: Eine relative Perzentilgrenze kann sich ohne Änderung an diesem Code verschieben. |
| Regel in diesem Lauf nicht aktiv oder Audit fehlgeschlagen | Eintrag nicht aufgrund fehlender Beobachtung entfernen. |

Änderungen der Ignore-Datei sollen erst nach erfolgreicher Analyse atomar veröffentlicht werden, damit ein Abbruch keine akzeptierten Entscheidungen verliert. Der Bericht kann die Anzahl der unterdrückten Kandidaten nennen, ohne sie jedes Mal ausführlich aufzulisten.

## Bewusste Vereinfachung und Grenzen

Ein Hash über die **gesamte Datei** ist einfach und konservativ: Auch eine Änderung weit außerhalb des Kandidaten lässt ihn erneut erscheinen. Das kann zusätzliches Rauschen erzeugen, vermeidet zunächst aber komplizierte Normalisierung einzelner Methoden. Wenn es in der Praxis stört, kann später ein Hash des ursächlichen Codeausschnitts erwogen werden. Bei einem Hinweis, der von mehreren Dateien abhängt, reicht der Hash der primären Datei dagegen nicht aus; solche Hinweise benötigen alle relevanten Dateien oder zunächst keine Ignore-Funktion.

Wenn sich die Bedeutung einer Regel ändert, darf eine alte Akzeptanz nicht still weitergelten. Eine Regelversion im Eintrag wäre ein einfacher Mechanismus. Ob Änderungen wirksamer Regeloptionen die Akzeptanz ebenfalls verfallen lassen, ist zu entscheiden.

Die vorhandene `ainetreview.json` ist eine streng validierte Eingabekonfiguration und wird vom Tool nicht verändert. Eine **separate Ignore-JSON** erscheint deshalb passender als ein vom CLI bearbeiteter Abschnitt in dieser Datei. Ablageort, Dateiname und Git-Handhabung bleiben offen; die Liste sollte leicht versionierbar sein und keine Git-Laufzeitabhängigkeit erzeugen.

## Entscheidung anhand der Praxis

Zuerst auf einem großen Repository messen, wie viele Kandidaten ein vollständiger Audit liefert und wie viele davon im nächsten Lauf unverändert erneut geprüft werden müssten. Falls dieses Wiederholungsrauschen relevant ist, wäre die Ignore-Liste der kleinste gezielte Zustand. Ob die bisher spezifizierte Storage-Architektur dadurch ersetzt wird, verlangt eine bewusste Änderung der verbindlichen Epics; diese Notiz ändert sie nicht.
