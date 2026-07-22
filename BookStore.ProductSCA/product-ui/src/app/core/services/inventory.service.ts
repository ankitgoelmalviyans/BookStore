import { Injectable } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private baseUrl = `${environment.inventoryApiUrl}/inventory`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<any[]> {
    return this.http.get<any[]>(this.baseUrl);
  }

  // context is opt-in per caller (e.g. SUPPRESS_404_TOAST) — a 404 here is exceptional when
  // viewing a known product's inventory page, but routine when checking availability before
  // add-to-cart, so this service doesn't hardcode either behavior itself.
  getByProductId(productId: string, context?: HttpContext): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/${productId}`, { context });
  }
}
