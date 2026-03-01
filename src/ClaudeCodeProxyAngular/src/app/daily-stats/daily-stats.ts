import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { DatePipe, DecimalPipe, NgStyle } from '@angular/common';
import { StatsService, StatsBucket } from '../services/stats.service';

@Component({
  selector: 'app-daily-stats',
  imports: [DatePipe, DecimalPipe, NgStyle],
  templateUrl: './daily-stats.html',
  styleUrl: './daily-stats.scss',
})
export class DailyStats implements OnInit {
  private readonly statsService = inject(StatsService);

  readonly data = signal<StatsBucket[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly maxRequests = computed(() =>
    Math.max(1, ...this.data().map((b) => b.requestCount))
  );

  ngOnInit(): void {
    const to = new Date();
    const from = new Date(to);
    from.setDate(from.getDate() - 30);

    this.statsService
      .getDaily(from.toISOString(), to.toISOString())
      .subscribe({
        next: (buckets) => {
          this.data.set(buckets);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set('Failed to load daily stats. Is the proxy running?');
          this.loading.set(false);
          console.error(err);
        },
      });
  }

  barWidthPercent(requestCount: number): number {
    return Math.round((requestCount / this.maxRequests()) * 100);
  }
}
