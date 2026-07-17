import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Payment } from './models/payment.model';

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private baseUrl = `${environment.paymentApiUrl}/payment`;

  constructor(private http: HttpClient) {}

  // A 404 here is expected, not exceptional — the saga charges asynchronously after
  // InventoryReserved, so a just-placed order legitimately has no Payment row yet. Callers get
  // `null` for "not yet" instead of having to catch an error for a routine state.
  getByOrderId(orderId: string): Observable<Payment | null> {
    return this.http.get<Payment>(`${this.baseUrl}/${orderId}`).pipe(
      catchError((err: HttpErrorResponse) => (err.status === 404 ? of(null) : throwError(() => err)))
    );
  }
}
