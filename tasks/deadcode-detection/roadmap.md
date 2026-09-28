# Roadmap: Dead-code review candidates

Source of truth: [Konzept.md](Konzept.md). The AiNetLinter implementation and tests named there are read-only behavioral references; AiNetReview contracts take precedence. Execute the leaves in order. Each implementation leaf includes focused automated tests, checks affected `docs/` pages against the implemented state, runs the affected tests and required project gates, closes only verified checkboxes, and commits its own slice under the repository rules. Do not start implementation as part of this roadmap step.

- [x] **Milestone 1 — Complete immutable input and shared reference infrastructure** (aggregate: close only after both leaves and the audit pass)
  - [x] [M1-T1 — Conditional markup snapshot](roadmap/M1-T1-markup-snapshot.md)
  - [x] [M1-T2 — Shared semantic reference coverage](roadmap/M1-T2-reference-coverage.md)
  - [x] **M1 audit** — Read the milestone diff, its tests, current application contracts and [Konzept.md](Konzept.md). Check that markup is captured only for the configured rule, all declared coverage failures fail the run, generated C# and test references remain visible, and shared utilities contain no candidate policy. Record actionable gaps; close this box only when the milestone is verified. The audit changes no production code.

- [ ] **Milestone 2 — Candidate rule and published findings** (aggregate: close only after all leaves and the audit pass)
  - [ ] [M2-T1 — Candidate selection and direct usage](roadmap/M2-T1-candidates.md)
  - [ ] [M2-T2 — Indirect usage and suppression](roadmap/M2-T2-indirect-usage.md)
  - [ ] [M2-T3 — Registration, findings and end-to-end acceptance](roadmap/M2-T3-publication.md)
  - [ ] **M2 audit** — Read the complete feature diff, focused tests, report output, current `docs/` pages and [Konzept.md](Konzept.md). Check the candidate and exclusion boundaries, configuration defaults, safety behavior, deterministic findings, empty-run semantics and failed-run publication behavior. Record actionable gaps; close this box only when the feature is verified. The audit changes no production code.
