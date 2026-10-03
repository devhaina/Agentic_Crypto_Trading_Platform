import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';

import { routes } from './app.routes';
import { correlationInterceptor } from './core/interceptors/correlation.interceptor';
import { idempotencyInterceptor } from './core/interceptors/idempotency.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // Zoneless change detection: the dashboard is signal-driven throughout, so
    // zone.js has nothing to do here beyond adding payload and overhead.
    provideZonelessChangeDetection(),

    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),

    provideHttpClient(
      withFetch(),

      // Order matters: correlation first so the identifier is present on every
      // request, then idempotency, which only touches state-changing calls.
      withInterceptors([correlationInterceptor, idempotencyInterceptor]),
    ),
  ],
};
