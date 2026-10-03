# OpenAPI Contracts

Every .NET service generates its own OpenAPI document live, via Swashbuckle
(`Agentiva.BuildingBlocks.ServiceDefaults.OpenApi`), served at
`/openapi/v1.json` and explorable at `/swagger`. That live document — not a
copy — is the authoritative contract while a service is running.

This directory is where **versioned, exported snapshots** of those documents
belong, for contract testing and for diffing a change in CI before it ships
(`.github/workflows/ci.yml`'s `validate-manifests` job is the natural home for
a `docker compose exec <service> curl .../openapi/v1.json` export step).
Phase 1 does not populate it: with most services still skeletons, a frozen
snapshot would need re-exporting on every phase anyway.

To export one manually against a running stack:

```bash
curl -s http://localhost:8084/openapi/v1.json | jq . > contracts/openapi/risk-service.v1.json
```
