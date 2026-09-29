Deine Rolle: .agents\agent-workflow\01-konzept-planung.md

Denke bitte mit, 360 Grad View im Sinne der Anwendung.
Wir werden noch mehr Review-Analysen machen, ich will das Fundament solide haben.

Untenstehend meine unsortierten Ideen.
Du erstellst unser tasks\ ein verzeichnis mit passendem Namen und darin überarbeiten wir iterativ und interaktiv das Konzept.md.


Ideen:

Eine Quelle kann mehrere Review-Analysen treffen.
In jedem Finding muss das ersichtlich sein.
Beispiel: foo.cs:468 auch in analyses/bar.md und ...


Finding MD files werden durch Tool aufrufe bearbeitet nicht durch Agenten.
Jeder Finding Eintrag bekommt eine eindeutige ID.
Beispiel: id4585, foo.cs:568, ... Red: analyses/bar.md:7645, ...

Edit via exe mit Parameter:
"Fixed id6546, id7654"
"Ignored id7654"
"False-positive id4686"
Dann werden die Einträge entfernt durch das Programm und eine baseline.json geschrieben (bei nicht fixed)

Baseline:
Brauchen wir weil sonst massives Rauschen.
Es gibt eine MD File die für jede id ein sha liefert. Der wird dann eingetragen.
Der Sha würde vorab berechnet.
Der Sha ist eine Checksumme der Code Stelle.
Wenn Code geändert wurde tritt die Stelle wieder auf.
Baseline File wird automatisch bereinigt, wenn Code nicht mehr vorhanden oder keine Findings mehr - entfernen.

Index.md
Beschreibt den .exe Parameter edit flow, 
Konkrete Beispiele.
Hat eine Handlungsanweisung allgemein wie: "das sind Signale, analysiere im Sinne der Anwendung, 360 Grad View, keine workarounds,.."

Jede Review-Analyse muss darf erstmal nicht direkt MD Text Erzeugen, das muss gesammelt werden. Weil wir vielleicht erst am Ende feststellen wo das Symbol noch bei einer anderen Review-Analyse Auftritt.
Es ergeben sich eventuell Hotspots?
Foo.Bar() tritt an zig stellen auf = Hotspot?
Danach sortieren?


Index.md "prompt": "konzentriere dich auf ein Finding  nach dem anderen, kein Versuch alles am Stück zu erfassen." Das ist vielleicht aber zu eng. Ziel: Fokus auf eine Sache nach der anderen, nicht alles gleichzeitig.