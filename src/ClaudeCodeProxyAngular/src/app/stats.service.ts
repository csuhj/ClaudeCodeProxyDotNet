import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface StatsBucket {
  timeBucket: string;
  requestCount: number;
  llmRequestCount: number;
  totalInputTokens: number;
  totalOutputTokens: number;
}

@Injectable({ providedIn: 'root' })
export class StatsService {
  private readonly http = inject(HttpClient);

  getHourly(from?: string, to?: string): Observable<StatsBucket[]> {
    return this.http.get<StatsBucket[]>('/api/stats/hourly', {
      params: this.buildParams(from, to),
    });
  }

  getDaily(from?: string, to?: string): Observable<StatsBucket[]> {
    return this.http.get<StatsBucket[]>('/api/stats/daily', {
      params: this.buildParams(from, to),
    });
  }

  private buildParams(from?: string, to?: string): HttpParams {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return params;
  }
}
