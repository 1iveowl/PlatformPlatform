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
