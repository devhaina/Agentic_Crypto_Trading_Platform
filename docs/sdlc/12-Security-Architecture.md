# Security Architecture

## Identity
Use OIDC/OAuth2-compatible identity. Short-lived access tokens and secure refresh mechanisms.

## Authorization
RBAC:
- ADMIN
- TRADER
- VIEWER
- AUDITOR

## Secrets
Store exchange credentials in Azure Key Vault or equivalent. Never store secrets in Git, databases as plaintext, logs, event payloads or frontend configuration.

## Exchange API Key
- Trading only
- Withdrawals disabled
- IP allowlisting where supported
- Dedicated account/sub-account
- Minimum permissions

## Network
Place Execution Service and data stores in private network segments. Expose only gateway/approved ingress.

## Application Security
- TLS everywhere
- Input validation
- Rate limiting
- CSRF protection where applicable
- Secure headers
- Dependency scanning
- Container scanning
- SAST/DAST
- Audit logging

## AI Security
- Prompt injection defense for external content
- Source allowlisting
- Tool allowlisting
- Structured outputs
- No secret-bearing context
- No arbitrary code execution
- No direct financial tools

## Threats
Credential theft, duplicate orders, prompt injection, data poisoning, model hallucination, insider misuse, exchange outage, replayed events, API abuse.
