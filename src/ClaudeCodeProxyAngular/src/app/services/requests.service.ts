import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface LlmRequestSummary {
  id: number;
  timestamp: string;
  method: string;
  path: string;
  responseStatusCode: number;
  durationMs: number;
  model: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cacheReadTokens: number | null;
  cacheCreationTokens: number | null;
}

export interface LlmRequestDetail extends LlmRequestSummary {
  requestHeaders: string;
  requestBody: string | null;
  responseHeaders: string;
  responseBody: string | null;
  isStreaming: boolean;
}

@Injectable({ providedIn: 'root' })
export class RequestsService {
  private readonly http = inject(HttpClient);

  getRecent(from?: string, to?: string, page = 0, pageSize = 50): Observable<LlmRequestSummary[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    params = params.set('page', page.toString());
    params = params.set('pageSize', pageSize.toString());
    return this.http.get<LlmRequestSummary[]>('/api/requests', { params });
  }

  getDetail(id: number): Observable<LlmRequestDetail> {
    return this.http.get<LlmRequestDetail>(`/api/requests/${id}`);
  }
}
