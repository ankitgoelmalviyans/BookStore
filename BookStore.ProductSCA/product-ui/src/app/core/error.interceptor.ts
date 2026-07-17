import { Injectable } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from './auth.service';

// Centralises the two things every request would otherwise fail silently on: an expired/invalid
// token (401 — log the user out rather than leave the page stuck showing stale/empty data) and
// unexpected failures (5xx/network — surface *something* instead of the `.subscribe()` calls
// throughout the app that have no error handler of their own). 404s are deliberately left alone:
// callers like PaymentService.getByOrderId() treat "not found" as a normal state, not an error to
// toast about.
@Injectable()
export class ErrorInterceptor implements HttpInterceptor {
  constructor(private auth: AuthService, private snackBar: MatSnackBar) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(
      catchError((err: HttpErrorResponse) => {
        if (err.status === 401) {
          this.auth.logout();
        } else if (err.status !== 404) {
          const message = err.status === 0
            ? 'Network error — check your connection and try again.'
            : `Something went wrong (${err.status}). Please try again.`;
          this.snackBar.open(message, 'Dismiss', { duration: 5000 });
        }
        return throwError(() => err);
      })
    );
  }
}
