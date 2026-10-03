# Threat Model

## Assets
- Exchange API credentials
- Trading authority
- Portfolio state
- User identity
- Audit history
- Market data
- AI prompts/outputs

## Threats

### Credential Theft
Mitigation: external secret manager, least privilege, IP restrictions, no withdrawal permission.

### Prompt Injection
Mitigation: sanitize/limit external content, source allowlisting, tool allowlisting, structured outputs.

### Duplicate Orders
Mitigation: idempotency, unique clientOrderId, exchange reconciliation.

### Data Poisoning
Mitigation: source validation, anomaly detection, multiple data checks.

### Insider Abuse
Mitigation: RBAC, MFA, audit trails, approvals, separation of duties.

### Exchange Outage
Mitigation: circuit breakers, stop-new-trading mode, reconciliation.

### Model Hallucination
Mitigation: deterministic strategies/risk, schema validation, no direct execution tools.

### Supply Chain
Mitigation: dependency scanning, signed images, SBOM, pinned versions.

### Unauthorized Configuration
Mitigation: RBAC, audit, approval workflows, configuration versioning.
