import { HttpInterceptorFn } from '@angular/common/http';

// Module-level, not a class field — functional interceptors are plain functions, but this still
// only runs once per app since ES modules are singletons, so one CorrelationId per session.
let correlationId = '';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = localStorage.getItem('auth_token');

  // Persist across page reloads so one browser session shares a single CorrelationId.
  // Without localStorage the id resets on every F5, collapsing to per-request granularity.
  if (!correlationId) {
    correlationId = localStorage.getItem('correlation_id') ?? crypto.randomUUID();
    try {
      localStorage.setItem('correlation_id', correlationId);
    } catch {
      // Keep the in-memory correlation id if persistence is unavailable.
    }
  }

  const headers: { [key: string]: string } = {
    'X-Correlation-Id': correlationId
  };

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  return next(req.clone({ setHeaders: headers }));
};
