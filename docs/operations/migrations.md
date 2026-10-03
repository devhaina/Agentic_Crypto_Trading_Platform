# Database Migrations

## Local development: automatic

`DatabaseMigrator<TContext>` (registered via `AddAgentivaDatabaseMigrator`)
runs as a hosted service at startup and applies pending EF Core migrations
before the host finishes starting. This is what makes an unattended
`docker compose up --build` produce a working schema with no manual step.

`ServiceDefaultsExtensions` configures hosted services to start **sequentially,
not concurrently** (`HostOptions.ServicesStartConcurrently = false`)
specifically so that a seeder (e.g. `RiskPolicySeeder`) never races the
migrator — it would otherwise try to insert into a table that does not exist
yet.

## Production: a reviewed pipeline step, not automatic

Automatic migration is deliberately **off** in any multi-replica deployment.
Several replicas racing to apply the same migration at startup is how a
schema ends up locked or half-applied. Instead:

1. CI builds the service image.
2. A migration job (a Kubernetes `Job`, not a `Deployment`) runs
   `dotnet ef database update` against the target database, using the same
   connection string the service will use, **before** any replica of the new
   version is scheduled.
3. Only once that job succeeds does the deployment roll out.

`AddAgentivaDatabaseMigrator` should be **omitted** from a service's
production composition root, or gated behind a configuration flag that is
false by default — this is the one piece of Phase-1 convenience wiring that
must not travel into the Kubernetes overlays unmodified.

## Generating a new migration

```bash
dotnet dotnet-ef migrations add <Name> \
  --project services/<service>/Agentiva.<Name>.Infrastructure \
  --startup-project services/<service>/Agentiva.<Name>.Api \
  --output-dir Persistence/Migrations
```

Always inspect the generated migration before committing it:

```bash
grep -o 'numeric([0-9]*,[0-9]*)' <migration>.cs | sort | uniq -c   # every money column must be numeric(38,18)
grep -cE 'type: "(double precision|real)"' <migration>.cs          # must be 0
```
