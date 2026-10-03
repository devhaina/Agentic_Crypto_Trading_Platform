import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Stamps a correlation identifier on every outbound request.
 *
 * This is the client end of the platform's end-to-end tracing. One user action
 * in the dashboard produces a single identifier that the gateway propagates
 * through the trading service, the risk gate, the execution service and into
 * RabbitMQ — so a support question about one click can be answered from one
 * log query rather than by correlating timestamps across fourteen services.
 */
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  // crypto.randomUUID is available in every browser this dashboard supports and
  // needs no dependency.
  const correlationId =
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID()
      : `web-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

  return next(
    request.clone({
      setHeaders: { 'X-Correlation-Id': correlationId },
    }),
  );
};
