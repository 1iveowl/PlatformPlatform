# External identity rollout

The Google backfill preserves existing table bindings. Each insert uses `ON CONFLICT DO NOTHING` without a conflict target so both unique constraints are respected, including API writes after the initial snapshot. Multiple live legacy holders remain unresolved; a single live holder takes precedence over deleted holders.

For the first deployment replacing legacy JSON writers:

1. Put external login/signup into maintenance and drain in-flight callbacks on every old API revision. Stop old Workers that can write users. A healthy new revision alone does not prove old requests have drained.
2. Apply the additive schema migrations and deploy the API that writes only the identity table. Do not route requests back to legacy revisions.
3. Deploy the new Worker. `BackfillExternalIdentities` runs for new installations; `ReconcileLegacyExternalIdentities` also runs once for installations that already recorded the original backfill. The latter includes JSON entries written after the original run. Both preserve current API bindings.
4. Check `__data_migrations_history` for successful reconciliation and review its contested-holder count before reopening external authentication. Resolve contested keys through the existing authenticated login policy, never by arbitrarily assigning a holder.

If legacy writers are reintroduced, repeat the drain and reconciliation before reopening login. Editing a recorded migration does not rerun it; deliver a new reconciliation migration for that deployment. Keep the legacy JSON column until its separately planned removal.

## PostgreSQL regression tests

Set `ACCOUNT_TEST_POSTGRES` to a disposable PostgreSQL database with schema-creation permission, then use the developer CLI's test command with `--filter FullyQualifiedName~PostgreSqlBackfillTests`. Each test creates and drops a unique schema, applies the actual identity-table migration, and uses a second connection to insert after the backfill snapshot. It checks both uniqueness constraints, late JSON writes, rollback and repeated reconciliation. Without the environment variable, these tests report an explicit skip; the ordinary SQLite suite does not replace them.

## Enabling MitID login after verification-only deployments

Keep `OAuth:MitId:LoginEnabled` and `PUBLIC_MITID_LOGIN_ENABLED` false until this sequence is complete. The original `GrantLoginToVerifiedExternalIdentities` schema UPDATE only covers rows present at schema deployment; it cannot see a verification that the old API finishes later.

1. Assess bindings created by the old parser before enabling their use for login. Stored evidence does not retain business-context claims or the original assurance text. Establish citizen context and an allowed assurance input from independent provenance, or use administrator revoke-and-reverify under the corrected parser. Do not infer provenance from the badge alone.
2. Put verification into maintenance and drain in-flight callbacks on every old API revision. Stop old Workers. Deploy the API with corrected validation and with verification granting both capabilities; keep MitID login disabled. A purpose flag absent from old binaries does not stop those binaries.
3. Start the new Worker only after old verification writers have stopped. `ReconcileVerifiedMitIdLoginCapabilities` has a new data-migration ID, so it runs even where the original schema UPDATE was already applied. It also repairs rows completed after that UPDATE. It changes only the exact MitId/Verification capability pair, preserving all evidence and unrelated providers; a revoked/deleted row is not recreated.
4. Confirm successful reconciliation in `__data_migrations_history` and zero rows from `SELECT COUNT(*) FROM external_identities WHERE provider = 'MitId' AND capabilities = 'Verification';`. Reopen verification with the corrected API and explicitly enable login only after the provenance assessment is complete. New verifications grant both capabilities immediately.

Do not return traffic to old verification writers. If they are reintroduced after the reconciliation is recorded, drain them and deliver a new reconciliation ID before enabling login again. Rerunning an applied migration body by editing its source does nothing. Login deliberately never upgrades a Verification-only row as a workaround.

`PostgreSqlBackfillTests` also executes the actual initial capability migration, inserts a late legacy verification through a separate connection, then checks final reconciliation, rollback/retry, current verification writes, revocation and exact evidence preservation.
