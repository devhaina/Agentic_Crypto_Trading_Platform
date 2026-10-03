# Docker

## Where the Dockerfiles actually live

Each service's `Dockerfile` sits next to its source, at
`services/<service>/Dockerfile` (and `ai/agent-platform/Dockerfile`,
`frontend/Dockerfile`) — not in this directory. That is a deliberate
convention, not an oversight: a Dockerfile next to the code it builds is
easier to keep in sync with that code, and `docker build -f
services/risk-service/Dockerfile .` reads naturally next to
`git log -- services/risk-service/`.

This directory is reserved for genuinely **shared** Docker assets that don't
belong to one service — a common `.dockerignore` fragment, a base image
Dockerfile if the platform ever introduces one, or multi-service Compose
fragments beyond the root `docker-compose.yml`. None exist yet in Phase 1.

## Where to actually look

- Per-service Dockerfiles: `services/*/Dockerfile`, `ai/agent-platform/Dockerfile`, `frontend/Dockerfile`
- Local orchestration: `docker-compose.yml` (repository root)
- Kubernetes: `infrastructure/kubernetes/`
