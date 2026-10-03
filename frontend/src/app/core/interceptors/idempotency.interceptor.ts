import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Attaches an idempotency key to every state-changing request.
 *
 * The backend requires this header on any command that can cause a financial
 * transaction and rejects the request without it. Generating it here, rather
 * than in each component, means a double-clicked button or a browser retry
 * cannot create two trading intents — the second request carries the same key
 * and the backend replays the first outcome.
 *
 * The key is derived from the request URL and body so that an identical retry
 * reuses it while a genuinely different request gets a fresh one. A random key
 * per call would defeat the purpose entirely, since a retry would then look
 * like a new command.
 */
export const idempotencyInterceptor: HttpInterceptorFn = (request, next) => {
  const mutating = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method);

  if (!mutating || request.headers.has('Idempotency-Key')) {
    return next(request);
  }

  const fingerprint = `${request.method}:${request.urlWithParams}:${stableStringify(request.body)}`;

  return next(
    request.clone({
      setHeaders: { 'Idempotency-Key': `web-${hash(fingerprint)}` },
    }),
  );
};

/**
 * Serialises a value with object keys in a stable order.
 *
 * `JSON.stringify` preserves insertion order, so two logically identical
 * bodies built in a different order would hash differently and defeat the
 * de-duplication this interceptor exists to provide.
 */
function stableStringify(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }

  if (typeof value !== 'object') {
    return String(value);
  }

  if (Array.isArray(value)) {
    return `[${value.map(stableStringify).join(',')}]`;
  }

  const entries = Object.entries(value as Record<string, unknown>)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, nested]) => `${key}:${stableStringify(nested)}`);

  return `{${entries.join(',')}}`;
}

/**
 * FNV-1a, 32-bit.
 *
 * Not a cryptographic hash and does not need to be: the key only has to be
 * stable for an identical request and distinct for a different one. The
 * authoritative duplicate protection is the backend's uniquely indexed
 * idempotency table, which this merely feeds.
 */
function hash(input: string): string {
  let value = 0x811c9dc5;

  for (let i = 0; i < input.length; i++) {
    value ^= input.charCodeAt(i);
    value = Math.imul(value, 0x01000193);
  }

  return (value >>> 0).toString(16).padStart(8, '0');
}
