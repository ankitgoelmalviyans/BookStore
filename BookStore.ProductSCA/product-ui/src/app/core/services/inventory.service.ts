import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private baseUrl = `${environment.inventoryApiUrl}/inventory`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<any[]> {
    return this.http.get<any[]>(this.baseUrl);
  }

  getByProductId(productId: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/${productId}`);
  }
}
