import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { environment } from '../../environments/environment';

interface Message {
  role: 'user' | 'assistant';
  content: string;
}

@Component({
  selector: 'app-help-assistant',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './help-assistant.component.html',
  styleUrls: ['./help-assistant.component.css']
})
export class HelpAssistantComponent {
  isOpen = signal(false);
  isLoading = signal(false);
  question = '';
  messages = signal<Message[]>([]);

  private readonly foundryEndpoint = environment.foundryAgentEndpoint;
  private readonly foundryKey = environment.foundryApiKey;

  toggleChat(): void {
    this.isOpen.update(v => !v);
    if (this.isOpen() && this.messages().length === 0) {
      this.messages.set([{
        role: 'assistant',
        content: 'Hi! I am the BookStore Help Assistant. Ask me anything about orders, returns, payments, or your account.'
      }]);
    }
  }

  async ask(): Promise<void> {
    const userQuestion = this.question.trim();
    if (!userQuestion || this.isLoading()) return;

    this.messages.update(msgs => [...msgs, {
      role: 'user',
      content: userQuestion
    }]);

    this.question = '';
    this.isLoading.set(true);

    // Calls the Foundry Agent directly via fetch() rather than Angular's HttpClient — the app's
    // global interceptors (auth.interceptor, error.interceptor) attach the BookStore session JWT
    // to every HttpClient request and log the user out on a 401. Both are wrong for a third-party
    // AI endpoint carrying its own bearer key: the interceptor would overwrite that key with the
    // user's session token, and an unrelated Foundry auth failure would sign the user out of
    // BookStore entirely.
    try {
      const response = await fetch(this.foundryEndpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${this.foundryKey}`
        },
        body: JSON.stringify({
          messages: this.messages().map(m => ({
            role: m.role,
            content: m.content
          }))
        })
      });

      if (!response.ok) {
        throw new Error(`Foundry agent request failed with status ${response.status}`);
      }

      const data = await response.json();
      const answer = data?.choices?.[0]?.message?.content
        ?? data?.output?.[0]?.content?.[0]?.text
        ?? 'Sorry, I could not get an answer. Please try again.';

      this.messages.update(msgs => [...msgs, {
        role: 'assistant',
        content: answer
      }]);
    } catch {
      this.messages.update(msgs => [...msgs, {
        role: 'assistant',
        content: 'Something went wrong. Please try again later.'
      }]);
    } finally {
      this.isLoading.set(false);
    }
  }

  onEnter(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.ask();
    }
  }

  clearChat(): void {
    this.messages.set([{
      role: 'assistant',
      content: 'Hi! I am the BookStore Help Assistant. Ask me anything about orders, returns, payments, or your account.'
    }]);
  }
}
