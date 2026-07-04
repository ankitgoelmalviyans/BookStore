import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler } from '@angular/common/http';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {

  private correlationId: string = '';

  intercept(req: HttpRequest<any>, next: HttpHandler) {
    const token = localStorage.getItem('auth_token');

    // Generate a correlation ID for this browser session's request chain
    if (!this.correlationId) {
      this.correlationId = crypto.randomUUID();
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
