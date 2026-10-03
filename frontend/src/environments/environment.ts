/**
 * Runtime configuration.
 *
 * `apiBaseUrl` is read from a global injected by the container entrypoint at
 * startup, falling back to the build-time default. That matters because the
 * Angular bundle is built once and the same image is promoted through
 * environments — a baked-in URL would mean rebuilding the image per
 * environment, and an image that differs between staging and production is no
 * longer the artefact that was tested.
 *
 * See `infrastructure/docker/frontend-entrypoint.sh`, which writes
 * `window.__AGENTIVA_CONFIG__` into index.html at container start.
 */

declare global {
  interface Window {
    __AGENTIVA_CONFIG__?: {
      apiBaseUrl?: string;
      environment?: string;
    };
  }
}

const runtime = typeof window !== 'undefined' ? window.__AGENTIVA_CONFIG__ : undefined;

export const environment = {
  production: false,

  /** Base URL of the API gateway. Every request goes through it. */
  apiBaseUrl: runtime?.apiBaseUrl ?? 'http://localhost:8080',

  environment: runtime?.environment ?? 'local',

  /** How often the platform status page re-polls service health. */
  healthPollIntervalMs: 15_000,
} as const;
