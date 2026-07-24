import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HelpAssistantService } from '../core/services/help-assistant.service';
import { HelpAssistantMessage } from '../core/models/help-assistant.model';

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
  messages = signal<HelpAssistantMessage[]>([]);

  constructor(private helpAssistantService: HelpAssistantService) {}

  toggleChat(): void {
    this.isOpen.update(v => !v);
    if (this.isOpen() && this.messages().length === 0) {
      this.messages.set([{
        role: 'assistant',
        content: 'Hi! I am the BookStore Help Assistant. Ask me anything about orders, returns, payments, or your account.'
      }]);
    }
  }

  ask(): void {
    const userQuestion = this.question.trim();
    if (!userQuestion || this.isLoading()) return;

    this.messages.update(msgs => [...msgs, {
      role: 'user',
      content: userQuestion
    }]);

    this.question = '';
    this.isLoading.set(true);

    this.helpAssistantService.ask(this.messages()).subscribe({
      next: (response) => {
        this.messages.update(msgs => [...msgs, {
          role: 'assistant',
          content: response.answer
        }]);
        this.isLoading.set(false);
      },
      error: () => {
        // ErrorInterceptor already surfaces a toast for the failure — still add an inline message
        // so the chat itself doesn't look like it silently swallowed the question.
        this.messages.update(msgs => [...msgs, {
          role: 'assistant',
          content: 'Something went wrong. Please try again later.'
        }]);
        this.isLoading.set(false);
      }
    });
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
