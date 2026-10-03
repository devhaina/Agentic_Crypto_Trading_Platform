# Database Access Scoping

## Local development

One PostgreSQL role, `agentiva`, owns every database (see
`infrastructure/postgres/init/01-create-databases.sql`). Every service
connects as the same role. This is a deliberate local-only simplification: it
makes `docker compose up` work with one password in one `.env` file.

## Production

Issue one least-privilege role per service, each granted connect and schema
privileges on only its own database:

```sql
CREATE ROLE risk_service_app LOGIN PASSWORD '...';
GRANT CONNECT ON DATABASE risk_db TO risk_service_app;
GRANT USAGE, CREATE ON SCHEMA risk TO risk_service_app;
GRANT ALL ON ALL TABLES IN SCHEMA risk TO risk_service_app;
-- No GRANT of any kind on any other service's database.
```

The physical isolation (database-per-service) already prevents a cross-
service join at the SQL level; per-service roles add the second layer —
prevention even if a connection string were leaked, since that credential
still could not open another service's database.

Each service's Kubernetes `ExternalSecret` (see
`infrastructure/kubernetes/base/secrets.yaml`) should source its own
`POSTGRES_PASSWORD` value from a distinct vault key, not the shared one Phase
1 uses.
