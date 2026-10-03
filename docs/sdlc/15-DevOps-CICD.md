# DevOps and CI/CD

## Repository
Monorepo with independently buildable services.

## Pipeline
PR -> lint -> build -> unit tests -> integration tests -> contract tests -> security scan -> container build -> container scan -> deploy dev -> smoke tests -> approval -> production.

## Containers
One Dockerfile per deployable service.

## Kubernetes
Use Deployment, Service, ConfigMap, Secret references, HPA, PDB, NetworkPolicy and readiness/liveness probes.

## Environments
Development, Test, Staging/Paper, Production.

## Deployment Safety
- No automatic live trading enablement.
- Production execution requires explicit configuration.
- Database migrations are reviewed and versioned.
- Rollback procedure documented.

## Secrets
Use workload identity/managed identity and external secret manager.
