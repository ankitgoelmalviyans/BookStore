import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { HelpAssistantAskResponse, HelpAssistantMessage } from '../models/help-assistant.model';

// Talks only to BookStore.HelpAssistantService — an anonymous, isolated microservice with its own
// Foundry project. The Angular app never sees a Foundry endpoint or credential; this is the same
// isolation AiSearchService has from AiService, just for a separate agent/project.
@Injectable({ providedIn: 'root' })
export class HelpAssistantService {
  private baseUrl = `${environment.helpAssistantApiUrl}/help-assistant`;

  constructor(private http: HttpClient) {}

  ask(messages: HelpAssistantMessage[]): Observable<HelpAssistantAskResponse> {
    return this.http.post<HelpAssistantAskResponse>(`${this.baseUrl}/ask`, { messages });
  }
}
