# Relaunch-Diskussion

Die [Neuausrichtung des AiNetReview-Konzepts](AiNetReview-Konzept.md) hat `status: draft`. Sie beschreibt einen zustandslosen EXE-zu-Markdown-Ablauf ohne Storage und MCP. Die vier Epics sind entsprechend überarbeitet, aber noch nicht freigegeben. Die überholte Roadmap wurde entfernt; eine neue entsteht erst im gesondert aufzurufenden Workflow-Schritt.

Für einen neuen Chat zuerst das unveränderte [Relaunch-Konzept](Relaunch-Konzept.md) als historische Motivation und dann das [AiNetReview-Konzept](AiNetReview-Konzept.md) mit den vier verlinkten Epics lesen. Das Relaunch-Konzept enthält frühere Ideen zu Storage und MCP und ist dafür nicht mehr maßgeblich.

Dieses Task-Verzeichnis gehört zum neuen Repository `C:\Daten\Entwicklung\Ralf\AiNetReview\`. Der vorhandene, getestete AiNetLinter-Code liegt im **anderen** Repository `C:\Daten\Entwicklung\Ralf\AiNetLinter\`. Die Quellcode-Vorlagen in Epic 4 beziehen sich auf diesen absoluten Pfad. Umsetzung und neue Dateien gehören ausschließlich in AiNetReview.

Bei passenden Infrastrukturaufgaben gilt die [Referenzstrategie aus Epic 4](epics/04-Umsetzung-und-Abnahme.md#ainetlinter-als-referenz): getestete Ansätze und Tests in AiNetLinter zuerst prüfen, AiNetReview-Verträge eigenständig umsetzen.

Der Agent nennt bei Architekturfragen seine Empfehlung mit Begründung und weist auf Sackgassen hin. Er hält die Spezifikation bis zum definierten DoD vollständig: keine offenen Varianten oder ungelösten Produktfragen. Produktverträge stehen im zuständigen Epic; eine neue Roadmap folgt erst nach Freigabe des Konzepts. Das ursprüngliche Nutzerkonzept bleibt unangetastet. Diese README beschreibt ausschließlich den Gesprächs- und Dokumentationsablauf.
