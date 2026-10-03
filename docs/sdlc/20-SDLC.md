# SDLC and Engineering Standards

## Workflow
Requirements -> Architecture -> ADR -> Implementation -> Tests -> Security review -> Staging -> Approval -> Production -> Monitoring.

## Branching
Prefer trunk-based development or short-lived feature branches.

## Pull Requests
Require:
- tests
- security review for sensitive changes
- architecture impact
- migration review
- API/event contract review

## Coding Standards
- nullable reference types enabled
- analyzers enabled
- async/await
- cancellation tokens
- dependency injection
- immutable event contracts
- no secrets
- no floating-point financial calculations

## Definition of Done
Code compiled, tests passed, security checks passed, telemetry added, documentation updated, migration reviewed, rollback considered.
