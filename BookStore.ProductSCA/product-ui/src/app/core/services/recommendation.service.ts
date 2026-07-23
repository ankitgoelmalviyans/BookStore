import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CoPurchasePartner } from '../models/recommendation.model';

@Injectable({ providedIn: 'root' })
export class RecommendationService {
  private baseUrl = `${environment.recommendationApiUrl}/recommendation`;

  constructor(private http: HttpClient) {}

  getRecommendations(productId: string, top = 5): Observable<CoPurchasePartner[]> {
    return this.http.get<CoPurchasePartner[]>(`${this.baseUrl}/${productId}`, {
      params: { top }
    });
  }
}
