# Identity & Token Strategy

## Local development (Phase 1–10): symmetric JWT

`JwtAuthenticationExtensions` in `Agentiva.BuildingBlocks.ServiceDefaults`
validates tokens with a single shared `HS256` key (`Jwt__SigningKey`), read
identically by every service. This is simple and it is explicitly **not**
production-appropriate: a symmetric secret means any service that can
*validate* a token can also *mint* one, so a single compromised service could
forge an administrator identity across the whole platform. It is acceptable
while everything runs as one trusted developer's Docker Compose stack.

Generate a local key:

```bash
openssl rand -base64 64
```

## Production (Phase 11): asymmetric OIDC

Replace the symmetric scheme with a real OIDC provider (Azure AD B2C, Auth0,
Keycloak, or the eventual real Identity Service once it issues tokens itself).
Services then hold only the provider's **public** key or JWKS endpoint — never
a key that can mint a token — so a compromised service can validate but never
forge. `TokenValidationParameters.IssuerSigningKey` moves from a
`SymmetricSecurityKey` to a `JsonWebKeySet`-backed resolver; no other part of
`JwtAuthenticationExtensions` changes.

## Roles and policies

Four roles (`AgentivaRoles`): `administrator`, `trader`, `operator`, `viewer`.
Four policies (`AgentivaPolicies`) map onto them: `can-trade` (create
intents, cancel orders), `can-operate` (kill switch, alert acknowledgement),
`can-administer` (risk policy changes, exchange accounts), `can-view` (every
authenticated role). The gateway's `appsettings.json` `ReverseProxy.Routes`
section applies these per-route, split by HTTP method so a GET and a POST to
the same path can require different roles.

## MFA

Not implemented in Phase 1. The policy model above is MFA-*ready*: a
`can-administer` or `can-trade` claim check is where an MFA-completed claim
would be asserted once the Identity Service issues one.
