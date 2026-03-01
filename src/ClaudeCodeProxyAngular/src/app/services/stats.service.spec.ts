import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  provideHttpClientTesting,
  HttpTestingController,
} from '@angular/common/http/testing';
import { StatsService, StatsBucket } from './stats.service';

const mockBuckets: StatsBucket[] = [
  {
    timeBucket: '2024-01-15T10:00:00Z',
    requestCount: 42,
    llmRequestCount: 10,
    totalInputTokens: 1000,
    totalOutputTokens: 500,
  },
];

describe('StatsService', () => {
  let service: StatsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(StatsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('getHourly()', () => {
    it('sends GET to /api/stats/hourly with no query params', () => {
      let result: StatsBucket[] | undefined;

      service.getHourly().subscribe((data) => (result = data));

      const req = httpMock.expectOne('/api/stats/hourly');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys()).toHaveLength(0);

      req.flush(mockBuckets);
      expect(result).toEqual(mockBuckets);
    });

    it('sends from and to as query params when both are provided', () => {
      const from = '2024-01-15T00:00:00Z';
      const to = '2024-01-16T00:00:00Z';

      service.getHourly(from, to).subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/stats/hourly');
      expect(req.request.params.get('from')).toBe(from);
      expect(req.request.params.get('to')).toBe(to);
      req.flush([]);
    });

    it('only sends from when to is omitted', () => {
      service.getHourly('2024-01-15T00:00:00Z').subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/stats/hourly');
      expect(req.request.params.get('from')).toBe('2024-01-15T00:00:00Z');
      expect(req.request.params.has('to')).toBe(false);
      req.flush([]);
    });

    it('only sends to when from is omitted', () => {
      service.getHourly(undefined, '2024-01-16T00:00:00Z').subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/stats/hourly');
      expect(req.request.params.has('from')).toBe(false);
      expect(req.request.params.get('to')).toBe('2024-01-16T00:00:00Z');
      req.flush([]);
    });

    it('returns the flushed response data to subscribers', () => {
      const received: StatsBucket[] = [];
      service.getHourly().subscribe((data) => received.push(...data));

      httpMock.expectOne('/api/stats/hourly').flush(mockBuckets);

      expect(received).toEqual(mockBuckets);
    });
  });

  describe('getDaily()', () => {
    it('sends GET to /api/stats/daily with no query params', () => {
      let result: StatsBucket[] | undefined;

      service.getDaily().subscribe((data) => (result = data));

      const req = httpMock.expectOne('/api/stats/daily');
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys()).toHaveLength(0);

      req.flush(mockBuckets);
      expect(result).toEqual(mockBuckets);
    });

    it('sends from and to as query params when both are provided', () => {
      const from = '2024-01-01T00:00:00Z';
      const to = '2024-01-31T00:00:00Z';

      service.getDaily(from, to).subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/stats/daily');
      expect(req.request.params.get('from')).toBe(from);
      expect(req.request.params.get('to')).toBe(to);
      req.flush([]);
    });

    it('only sends from when to is omitted', () => {
      service.getDaily('2024-01-01T00:00:00Z').subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/stats/daily');
      expect(req.request.params.get('from')).toBe('2024-01-01T00:00:00Z');
      expect(req.request.params.has('to')).toBe(false);
      req.flush([]);
    });

    it('only sends to when from is omitted', () => {
      service.getDaily(undefined, '2024-01-31T00:00:00Z').subscribe();

      const req = httpMock.expectOne((r) => r.url === '/api/stats/daily');
      expect(req.request.params.has('from')).toBe(false);
      expect(req.request.params.get('to')).toBe('2024-01-31T00:00:00Z');
      req.flush([]);
    });

    it('returns the flushed response data to subscribers', () => {
      const received: StatsBucket[] = [];
      service.getDaily().subscribe((data) => received.push(...data));

      httpMock.expectOne('/api/stats/daily').flush(mockBuckets);

      expect(received).toEqual(mockBuckets);
    });
  });
});
