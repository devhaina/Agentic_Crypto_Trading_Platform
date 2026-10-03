/** Production configuration. See environment.ts for how the URL is resolved. */

const runtime = typeof window !== 'undefined' ? window.__AGENTIVA_CONFIG__ : undefined;

export const environment = {
  production: true,
  apiBaseUrl: runtime?.apiBaseUrl ?? '/api-gateway',
  environment: runtime?.environment ?? 'production',
  healthPollIntervalMs: 30_000,
} as const;
