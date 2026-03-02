import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController,
} from '@angular/common/http/testing';
import { RequestsService, LlmRequestSummary, LlmRequestDetail } from './requests.service';

const mockSummary: LlmRequestSummary = {
  id: 1,
  timestamp: '2024-01-15T10:00:00Z',
  method: 'POST',
  path: '/v1/messages',
  responseStatusCode: 200,
  durationMs: 1234,
  model: 'claude-sonnet-4-6',
  inputTokens: 100,
  outputTokens: 50,
  cacheReadTokens: 0,
  cacheCreationTokens: 0,
};

const mockDetail: LlmRequestDetail = {
  ...mockSummary,
  requestHeaders: '{"content-type":"application/json"}',
  requestBody: '{"model":"claude-sonnet-4-6","messages":[]}',
  responseHeaders: '{"content-type":"application/json"}',
  responseBody: '{"id":"msg_1","type":"message"}',
  isStreaming: false,
};

describe('RequestsService', () => {
  let service: RequestsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RequestsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('getRecent()', () => {
    it('sends GET to /api/requests with page and pageSize when no from/to given', () => {
      let result: LlmRequestSummary[] | undefined;

      service.getRecent().subscribe((data) => (result = data));

      const req = httpMock.expectOne((r) => r.url === '/api/requests');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.has('from')).toBe(false);
      expect(req.request.params.has('to')).toBe(false);
      expect(req.request.params.get('page')).toBe('0');
      expect(req.request.params.get('pageSize')).toBe('50');

      req.flush([mockSummary]);
      expect(result).toEqual([mockSummary]);
    });

    it('sends from and to as query params when both are provided', () => {
      const from = '2024-01-15T00:00:00Z';
      const to = '2024-01-16T00:00:00Z';

      service.getRecent(from, to).subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/requests');
      expect(req.request.params.get('from')).toBe(from);
      expect(req.request.params.get('to')).toBe(to);
      req.flush([]);
    });

    it('sends custom page and pageSize', () => {
      service.getRecent(undefined, undefined, 2, 25).subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/requests');
      expect(req.request.params.get('page')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('25');
      req.flush([]);
    });

    it('returns the flushed response data to subscribers', () => {
      const received: LlmRequestSummary[] = [];
      service.getRecent().subscribe((data) => received.push(...data));

      httpMock.expectOne((r) => r.url === '/api/requests').flush([mockSummary]);

      expect(received).toEqual([mockSummary]);
    });
  });

  describe('getDetail()', () => {
    it('sends GET to /api/requests/{id}', () => {
      let result: LlmRequestDetail | undefined;

      service.getDetail(42).subscribe((data) => (result = data));

      const req = httpMock.expectOne('/api/requests/42');
      expect(req.request.method).toBe('GET');

      req.flush(mockDetail);
      expect(result).toEqual(mockDetail);
    });

    it('uses the provided id in the URL', () => {
      service.getDetail(99).subscribe();

      httpMock.expectOne('/api/requests/99').flush(mockDetail);
    });
  });
});
