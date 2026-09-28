# Relaunch-Diskussion

Die [Neuausrichtung des AiNetReview-Konzepts](AiNetReview-Konzept.md) hat `status: draft`. Sie beschreibt einen zustandslosen EXE-zu-Markdown-Ablauf ohne Storage und MCP. Die bisherigen Epics und die Roadmap enthalten noch überholte Verträge; sie sind bis zur konsistenten Überarbeitung nicht als Auftrag für weitere Umsetzung freigegeben.

Für einen neuen Chat zuerst das unveränderte [Relaunch-Konzept](Relaunch-Konzept.md) als Motivation und dann das [AiNetReview-Konzept](AiNetReview-Konzept.md) lesen. Die vier Epics und die [Roadmap](roadmap.md) dokumentieren noch die bisherige Spezifikation und den bisherigen Fortschritt. Nach der Konzeptentscheidung werden überholte Aussagen ersetzt und betroffene Querverweise geprüft.

Dieses Task-Verzeichnis gehört zum neuen Repository `C:\Daten\Entwicklung\Ralf\AiNetReview\`. Der vorhandene, getestete AiNetLinter-Code liegt im **anderen** Repository `C:\Daten\Entwicklung\Ralf\AiNetLinter\`. Die Quellcode-Vorlagen in Epic 4 beziehen sich auf diesen absoluten Pfad. Umsetzung und neue Dateien gehören ausschließlich in AiNetReview.

Bei passenden Infrastrukturaufgaben gilt die [Referenzstrategie aus Epic 4](epics/04-Umsetzung-und-Abnahme.md#ainetlinter-als-referenz): getestete Ansätze und Tests in AiNetLinter zuerst prüfen, AiNetReview-Verträge eigenständig umsetzen.

Der Agent nennt bei Architekturfragen seine Empfehlung mit Begründung und weist auf Sackgassen hin. Er hält die Spezifikation bis zum definierten DoD vollständig: keine offenen Varianten oder ungelösten Produktfragen. Änderungen werden im zuständigen Epic und im Fortschrittsstand der Roadmap nachgeführt. Das ursprüngliche Nutzerkonzept bleibt unangetastet. Diese README beschreibt ausschließlich den Gesprächs- und Dokumentationsablauf.
