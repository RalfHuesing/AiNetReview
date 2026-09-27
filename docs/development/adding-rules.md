# Adding a Rule

Implement `IReviewRule` in a dedicated folder under `src/AiNetReview.Core/Rules/`. Each rule exposes an immutable `RuleDescriptor` with its stable ID, title, positive behavior version, purpose, measurement, review questions, and option descriptors. An option descriptor supplies its JSON default and validation; configured values are combined with defaults into ordinally sorted `RuleOptions`.

`ExecuteAsync` receives a `ReviewContext`, resolved `RuleOptions`, and a cancellation token, and returns a `RuleResult` whose findings collection is immutable. Rule implementations must not keep mutable state between runs. `template-noop` demonstrates this contract by returning a complete empty result.

Register product rules explicitly through `ServiceRegistration.AddAiNetReviewRules`. `ServiceRegistration.AddAiNetReviewServices` registers the shared `RuleRegistry` and `CatalogWriter` separately. The registry rejects invalid or duplicate rule IDs and exposes its rules in ordinal ID order. The integration test project can add its fixture rule through an explicit test-only DI registration; the production registration contains only `template-noop`.
