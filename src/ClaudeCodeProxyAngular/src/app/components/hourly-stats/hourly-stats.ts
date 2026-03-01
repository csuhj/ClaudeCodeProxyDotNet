import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { StatsService, StatsBucket } from '../../services/stats.service';

@Component({
  selector: 'app-hourly-stats',
  imports: [DatePipe, DecimalPipe],
  templateUrl: './hourly-stats.html',
  styleUrl: './hourly-stats.scss',
})
export class HourlyStats implements OnInit {
  private readonly statsService = inject(StatsService);

  readonly data = signal<StatsBucket[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const to = new Date();
    const from = new Date(to);
    from.setDate(from.getDate() - 1);

    this.statsService
      .getHourly(from.toISOString(), to.toISOString())
      .subscribe({
        next: (buckets) => {
          this.data.set(buckets);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set('Failed to load hourly stats. Is the proxy running?');
          this.loading.set(false);
          console.error(err);
        },
      });
  }
}
