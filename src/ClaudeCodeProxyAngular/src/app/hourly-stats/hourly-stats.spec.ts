import { jest } from '@jest/globals';
import { render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { HourlyStats } from './hourly-stats';
import { StatsService, StatsBucket } from '../stats.service';

const mockBuckets: StatsBucket[] = [
  {
    timeBucket: '2024-01-15T10:00:00.000Z',
    requestCount: 5,
    llmRequestCount: 2,
    totalInputTokens: 200,
    totalOutputTokens: 100,
  },
];

const mockStatsService = {
  getHourly: jest.fn(),
};

async function renderComponent() {
  return render(HourlyStats, {
    providers: [{ provide: StatsService, useValue: mockStatsService }],
  });
}

describe('HourlyStats', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows loading state while data is pending', async () => {
    mockStatsService.getHourly.mockReturnValue(new Subject<StatsBucket[]>());

    await renderComponent();

    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('hides loading state after data arrives', async () => {
    mockStatsService.getHourly.mockReturnValue(of(mockBuckets));

    await renderComponent();

    expect(screen.queryByText('Loading...')).not.toBeInTheDocument();
  });

  it('renders table headers', async () => {
    mockStatsService.getHourly.mockReturnValue(of(mockBuckets));

    await renderComponent();

    expect(screen.getByText('Hour (UTC)')).toBeInTheDocument();
    expect(screen.getByText('Requests')).toBeInTheDocument();
    expect(screen.getByText('LLM Calls')).toBeInTheDocument();
    expect(screen.getByText('Input Tokens')).toBeInTheDocument();
    expect(screen.getByText('Output Tokens')).toBeInTheDocument();
  });

  it('renders a row of bucket data', async () => {
    mockStatsService.getHourly.mockReturnValue(of(mockBuckets));

    await renderComponent();

    expect(screen.getByText('15 Jan 10:00')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('200')).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument();
  });

  it('renders all bucket rows when there are multiple', async () => {
    const twoBuckets: StatsBucket[] = [
      { ...mockBuckets[0] },
      {
        timeBucket: '2024-01-15T11:00:00.000Z',
        requestCount: 3,
        llmRequestCount: 1,
        totalInputTokens: 100,
        totalOutputTokens: 50,
      },
    ];
    mockStatsService.getHourly.mockReturnValue(of(twoBuckets));

    await renderComponent();

    expect(screen.getByText('15 Jan 10:00')).toBeInTheDocument();
    expect(screen.getByText('15 Jan 11:00')).toBeInTheDocument();
  });

  it('shows error message when the request fails', async () => {
    mockStatsService.getHourly.mockReturnValue(
      throwError(() => new Error('Network error'))
    );

    await renderComponent();

    expect(
      screen.getByText('Failed to load hourly stats. Is the proxy running?')
    ).toBeInTheDocument();
  });

  it('shows empty state when the response is an empty array', async () => {
    mockStatsService.getHourly.mockReturnValue(of([]));

    await renderComponent();

    expect(screen.getByText('No data recorded yet.')).toBeInTheDocument();
  });

  it('calls getHourly with a 24-hour date range', async () => {
    mockStatsService.getHourly.mockReturnValue(of([]));

    await renderComponent();

    expect(mockStatsService.getHourly).toHaveBeenCalledTimes(1);
    const [from, to] = mockStatsService.getHourly.mock.calls[0] as [string, string];
    const diffHours =
      (new Date(to).getTime() - new Date(from).getTime()) / (1000 * 60 * 60);
    expect(diffHours).toBeCloseTo(24, 0);
  });
});
