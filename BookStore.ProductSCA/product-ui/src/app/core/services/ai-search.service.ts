import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BookSearchResult } from '../models/ai-search.model';

@Injectable({ providedIn: 'root' })
export class AiSearchService {
  private baseUrl = `${environment.aiApiUrl}/ai`;

  constructor(private http: HttpClient) {}

  search(query: string, topK = 5): Observable<BookSearchResult> {
    return this.http.post<BookSearchResult>(`${this.baseUrl}/search`, { query, topK });
  }
}
