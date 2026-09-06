# External provider protocol fixtures

The mock provider returns a profile directly. API and browser tests verify the flow, but cannot verify an issuer's token parsing. Provider tests sign synthetic payloads with a test key and validate them through the real provider and discovery stub.

For each account type, capture the claim names, JSON types and relevant value shapes observed during a controlled real-provider check. Reconstruct that shape using synthetic identifiers, emails, subjects, client IDs and nonces. Never commit a live token or credentials. Assert the JSON type in the signed payload when representation affects parsing, so helpers cannot silently normalize distinct cases.

Entra's recorded work/school shape is boolean `xms_edov: true`; its personal-account shape is string `xms_edov: "1"` (manual observation recorded in Linear on 2026-09-04). Existing Entra tests assert both signed payload types and their email-trust outcomes, alongside rejected values. These are reconstructed observed shapes, not captured real tokens.

A new provider is not ready for release until its contract has been checked against the real issuer and any observed differences have signed regression fixtures. Record the date, account type, outcome and sanitized shape in Linear; distinguish historical live evidence, a fresh live check and mocked E2E results.

MitID's sandbox observation (2026-09-04, recorded in Linear) uses string `loA` and ISO 8601 string `authenticationinstant`, without `acr` or `auth_time`. Tests assert those payload shapes. Business fixtures combine those observed technical claims with Idura's documented string `employee` / `companySignatory` discriminators, plus defensive boolean and unsupported-context cases. They are synthetic contract tests, not captured business tokens. [Idura documents private logins with neither discriminator](https://docs.idura.app/verify/e-ids/danish-mitid-erhverv/); the provider rejects either claim's presence independently of assurance. A controlled live citizen/business check remains required before release.
