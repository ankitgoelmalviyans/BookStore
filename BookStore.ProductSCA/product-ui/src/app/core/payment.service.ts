import { Injectable } from '@angular/core';
import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Payment } from './models/payment.model';
import { SUPPRESS_404_TOAST } from './http-context-tokens';

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private baseUrl = `${environment.paymentApiUrl}/payment`;

  constructor(private http: HttpClient) {}

  // A 404 here is expected, not exceptional — the saga charges asynchronously after
  // InventoryReserved, so a just-placed order legitimately has no Payment row yet. Callers get
  // `null` for "not yet" instead of having to catch an error for a routine state.
  // SUPPRESS_404_TOAST tells ErrorInterceptor this specific 404 shouldn't show the generic toast
  // — scoped to just this request, not a blanket "ignore all 404s" rule.
  getByOrderId(orderId: string): Observable<Payment | null> {
    return this.http
      .get<Payment>(`${this.baseUrl}/${orderId}`, {
        context: new HttpContext().set(SUPPRESS_404_TOAST, true)
      })
      .pipe(catchError((err: HttpErrorResponse) => (err.status === 404 ? of(null) : throwError(() => err))));
  }
}
