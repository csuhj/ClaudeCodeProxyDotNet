import { Component, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HourlyStats } from './components/hourly-stats/hourly-stats';
import { DailyStats } from './components/daily-stats/daily-stats';
import { LlmRequestsList } from './components/llm-requests-list/llm-requests-list';
import { RequestDetailPanel } from './components/request-detail-panel/request-detail-panel';

@Component({
  selector: 'app-root',
  imports: [DatePipe, HourlyStats, DailyStats, LlmRequestsList, RequestDetailPanel],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly now = new Date();
  readonly selectedRequestId = signal<number | null>(null);

  onRequestSelected(id: number): void {
    this.selectedRequestId.set(id);
  }

  onDetailClosed(): void {
    this.selectedRequestId.set(null);
  }
}
