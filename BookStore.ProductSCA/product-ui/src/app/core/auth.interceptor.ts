import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler } from '@angular/common/http';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {

  private correlationId: string = '';

  intercept(req: HttpRequest<any>, next: HttpHandler) {
    const token = localStorage.getItem('auth_token');

    // Persist across page reloads so one browser session shares a single CorrelationId.
    // Without localStorage the id resets on every F5, collapsing to per-request granularity.
    if (!this.correlationId) {
      this.correlationId = localStorage.getItem('correlation_id') ?? crypto.randomUUID();
      localStorage.setItem('correlation_id', this.correlationId);
    }

    const headers: { [key: string]: string } = {
      'X-Correlation-Id': this.correlationId
    };

    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    req = req.clone({ setHeaders: headers });

    return next.handle(req);
  }
}
