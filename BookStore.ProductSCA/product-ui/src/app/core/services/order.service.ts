import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OrderDetail, OrderSummary, PlaceOrderCommand } from '../models/order.model';

@Injectable({ providedIn: 'root' })
export class OrderService {
  private baseUrl = `${environment.orderApiUrl}/order`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<OrderSummary[]> {
    return this.http.get<OrderSummary[]>(this.baseUrl);
  }

  getById(id: string): Observable<OrderDetail> {
    return this.http.get<OrderDetail>(`${this.baseUrl}/${id}`);
  }

  placeOrder(command: PlaceOrderCommand): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(this.baseUrl, command);
  }
}
