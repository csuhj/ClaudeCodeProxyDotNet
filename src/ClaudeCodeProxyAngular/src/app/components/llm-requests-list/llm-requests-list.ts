import { Component, OnInit, Output, EventEmitter, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RequestsService, LlmRequestSummary } from '../../services/requests.service';

@Component({
  selector: 'app-llm-requests-list',
  imports: [DatePipe],
  templateUrl: './llm-requests-list.html',
  styleUrl: './llm-requests-list.scss',
})
export class LlmRequestsList implements OnInit {
  private readonly requestsService = inject(RequestsService);

  readonly data = signal<LlmRequestSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  @Output() requestSelected = new EventEmitter<number>();

  ngOnInit(): void {
    this.requestsService.getRecent().subscribe({
      next: (requests) => {
        this.data.set(requests);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load LLM requests. Is the proxy running?');
        this.loading.set(false);
        console.error(err);
      },
    });
  }

  formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(1)}s`;
  }

  truncatePath(path: string): string {
    if (path.length <= 60) return path;
    return path.slice(0, 57) + '…';
  }

  statusClass(code: number): string {
    if (code >= 200 && code < 300) return 'status-ok';
    if (code >= 400) return 'status-err';
    return '';
  }
}
