import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private lastCorrelationId: string | null = null;

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const token = localStorage.getItem('auth_token');

    const headers: { [key: string]: string } = {};
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
    if (this.lastCorrelationId) {
      headers['X-Correlation-Id'] = this.lastCorrelationId;
    }

    if (Object.keys(headers).length > 0) {
      req = req.clone({ setHeaders: headers });
    }

    return next.handle(req).pipe(
      tap(event => {
        if (event instanceof HttpResponse) {
          const correlationId = event.headers.get('X-Correlation-Id');
          if (correlationId) {
            this.lastCorrelationId = correlationId;
          }
        }
      })
    );
  }
}
