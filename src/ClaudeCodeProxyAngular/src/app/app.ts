import { Component, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HourlyStats } from './components/hourly-stats/hourly-stats';
import { DailyStats } from './components/daily-stats/daily-stats';
import { LlmRequestsList } from './components/llm-requests-list/llm-requests-list';

@Component({
  selector: 'app-root',
  imports: [DatePipe, HourlyStats, DailyStats, LlmRequestsList],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly now = new Date();
  readonly selectedRequestId = signal<number | null>(null);
}
