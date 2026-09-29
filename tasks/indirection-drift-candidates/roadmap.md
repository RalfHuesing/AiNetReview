# Roadmap: Indirection drift candidates

The [ready concept](Konzept.md) is the binding contract for every point. Complete the points in order. Each implementation point includes its relevant tests, documentation check, verification, and commit. Do not start another workflow step from this roadmap.

- [x] **1 — Classify transparent forwarding methods**
  - Intention: Give the analysis a precise, reusable per-run representation of statically bound forwarding edges.
  - Scope: Implement the Roslyn syntax and semantic classification, source declaration lookup, generic-symbol normalization, project ownership, and deterministic declaration locations defined under “Eligible declarations and symbols” and conditions 1–5 of the concept. Keep this component internal to the new review analysis; add focused FastTests for accepted body forms and each exclusion, including generated/test source, interface and virtual targets, linked documents, and unavailable semantic information.
  - Not: No graph findings, report-format change, production registration, historical comparison, or runtime-dispatch resolution in this point.
  - Acceptance: Tests demonstrate that each accepted edge has one exact source target and that rejected shapes create no edge; failures to obtain required syntax or semantic models follow the concept's analysis-failure contract. Run affected tests and the required build gate before committing.

- [x] **2 — Select paths and produce findings**
  - Intention: Turn classified edges into deterministic, reviewable candidates.
  - Scope: Implement the `IReviewAnalysis` and its descriptor without production registration, plus the per-project graph, root and cycle rules, maximal traversal, fixed qualification conditions, and `FindingDraft` contract from “Paths and finding selection” and “Finding and report contract”. Include ordered evidence for every member and all related symbols. Add FastTests for boundaries, longer and converging paths, cycles, deterministic ordering, no finding cap, identity, and repeated scans. Add runner tests for baseline selection through every participating file and exact symbol relationships.
  - Not: Do not register the analysis in production or change Markdown rendering here. Do not add a score, configurable threshold, or baseline read to the analysis.
  - Acceptance: Direct analysis and runner tests validate the complete finding data against the loaded snapshot, including one finding per qualifying root and no finding for rejected paths. Run affected tests and the required build gate before committing.

- [x] **3 — Expose the analysis in the product**
  - Intention: Make the new candidates usable in normal audits without changing existing analyses.
  - Scope: Add explicit production registration; update repository-root `ainetreview.json` and generated-config tests. Add the dedicated “Forwarding path” Markdown rendering that preserves evidence order and prints the three explanatory metrics. Add reporter and host integration tests for nonempty/empty output, configuration, failure/cancellation publication, and unchanged formats of existing analyses. Update affected current-state `docs/` pages and indexes in the same commit.
  - Not: No new CLI command, baseline format, runtime call resolution, build gate, or automatic code rewrite.
  - Acceptance: A normal configured host review produces ordered path reports in both applicable views, omits an empty analysis file, and preserves publication behavior on failure or cancellation. The repository build, affected FastTests, and IntegrationTests pass before committing.

- [ ] **Audit — Verify the ready concept against implementation and a real audit**
  - Intention: Confirm that the delivered signal is accurate, useful, and within the approved scope.
  - Scope: Read the completed diff and the concept; inspect the classifier, graph, finding, configuration, documentation, and report contracts. Run the required gates and at least one real repository audit; trace reported paths back to source and classify them as useful prompts, intentional layers, or measurement errors. Record concise evidence in the completion response and report actionable gaps for at most one correction point under the workflow.
  - Not: No scope expansion, metric-driven refactoring, speculative trend claim, or automatic correction during this read-only audit.
  - Acceptance: Every Must item and verification condition in the concept is evidenced, every Not item remains excluded, no unresolved defect remains, and the roadmap checkboxes reflect only work actually verified.
