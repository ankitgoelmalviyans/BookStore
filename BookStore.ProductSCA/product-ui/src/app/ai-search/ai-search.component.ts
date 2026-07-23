import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AiSearchService } from '../core/services/ai-search.service';
import { BookSearchResult } from '../core/models/ai-search.model';

// RAG search: AiService embeds the query, retrieves the closest books by vector similarity, then
// asks an LLM to answer grounded only in those retrieved books — the answer and the books it
// cites both come back together in one result.
@Component({
  selector: 'app-ai-search',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './ai-search.component.html',
  styleUrls: ['./ai-search.component.css']
})
export class AiSearchComponent {
  query = '';
  loading = false;
  result: BookSearchResult | null = null;
  searched = false;

  constructor(private aiSearchService: AiSearchService) {}

  search(): void {
    const trimmed = this.query.trim();
    if (!trimmed || this.loading) {
      return;
    }

    this.loading = true;
    this.searched = true;
    this.aiSearchService.search(trimmed).subscribe({
      next: (result) => {
        this.result = result;
        this.loading = false;
      },
      error: () => {
        // ErrorInterceptor already surfaces a toast for the failure — just stop the spinner.
        this.result = null;
        this.loading = false;
      }
    });
  }
}
