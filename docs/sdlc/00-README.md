# Agentic Crypto Trading Platform — Documentation Pack

Version: 1.0
Status: Architecture Baseline
Primary Stack: .NET 10, Angular 22, Python, PostgreSQL/TimescaleDB, Redis, RabbitMQ, Docker, Kubernetes/Azure

## Purpose
This documentation defines the product, architecture, engineering, security, AI-agent, trading, data, API, testing, deployment, and operational requirements for a production-grade agentic crypto trading platform.

## Core Principle
AI agents may analyze and propose trades. They must not directly control exchange funds. Every proposed trade must pass deterministic risk controls before reaching the isolated Execution Service.

## Documents
1. Product Requirements — `01-PRD.md`
2. Business Requirements — `02-BRD.md`
3. System Requirements — `03-SRS.md`
4. Solution Architecture — `04-Solution-Architecture.md`
5. Microservice Architecture — `05-Microservice-Architecture.md`
6. Service Specifications — `06-Service-Specifications.md`
7. Database Design — `07-Database-Design.md`
8. API Contracts — `08-API-Contracts.md`
9. Event Contracts — `09-Event-Contracts.md`
10. Agentic AI Design — `10-Agentic-AI-Architecture.md`
11. Trading & Risk Specification — `11-Trading-Risk-Specification.md`
12. Security Design — `12-Security-Architecture.md`
13. Backtesting & Model Evaluation — `13-Backtesting-ML-Evaluation.md`
14. Frontend Architecture — `14-Angular-Architecture.md`
15. DevOps & CI/CD — `15-DevOps-CICD.md`
16. Testing Strategy — `16-Testing-Strategy.md`
17. Observability — `17-Observability.md`
18. Disaster Recovery — `18-DR-BCP.md`
19. Operations Runbook — `19-Operations-Runbook.md`
20. SDLC & Engineering Standards — `20-SDLC.md`
21. ADRs — `21-ADRs.md`
22. Threat Model — `22-Threat-Model.md`
23. MVP Roadmap & Backlog — `23-MVP-Roadmap.md`
24. Traceability Matrix — `24-Requirements-Traceability.md`
25. Glossary — `25-Glossary.md`

## Scope
MVP starts with Binance, BTCUSDT/ETHUSDT, spot/paper trading, 15m/1h timeframes, deterministic strategies, and AI-assisted analysis. Futures, leverage, multi-exchange support, and autonomous strategy mutation are deferred.
