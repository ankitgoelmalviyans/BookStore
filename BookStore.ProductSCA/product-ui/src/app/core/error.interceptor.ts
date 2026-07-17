import { Injectable } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from './auth.service';
import { SUPPRESS_404_TOAST } from './http-context-tokens';

// Centralises the two things every request would otherwise fail silently on: an expired/invalid
// token (401 — log the user out rather than leave the page stuck showing stale/empty data) and
// unexpected failures (5xx/network/404 — surface *something* instead of the `.subscribe()` calls
// throughout the app that have no error handler of their own). A 404 is only suppressed when the
// caller explicitly opts in via the SUPPRESS_404_TOAST context token (e.g.
// PaymentService.getByOrderId, where "not found yet" is a normal state) — every other 404 still
// gets the generic toast, since elsewhere a 404 usually does mean something's wrong.
@Injectable()
export class ErrorInterceptor implements HttpInterceptor {
  constructor(private auth: AuthService, private snackBar: MatSnackBar) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(
      catchError((err: HttpErrorResponse) => {
        const suppressThis404 = err.status === 404 && req.context.get(SUPPRESS_404_TOAST);
        if (err.status === 401) {
          this.auth.logout();
        } else if (!suppressThis404) {
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
