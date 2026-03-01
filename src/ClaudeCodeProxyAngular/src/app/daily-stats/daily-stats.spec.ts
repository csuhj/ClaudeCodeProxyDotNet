import { jest } from '@jest/globals';
import { render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { DailyStats } from './daily-stats';
import { StatsService, StatsBucket } from '../stats.service';

const mockBuckets: StatsBucket[] = [
  {
    timeBucket: '2024-01-15T00:00:00.000Z',
    requestCount: 42,
    llmRequestCount: 10,
    totalInputTokens: 1000,
    totalOutputTokens: 500,
  },
];

const mockStatsService = {
  getDaily: jest.fn(),
};

async function renderComponent() {
  return render(DailyStats, {
    providers: [{ provide: StatsService, useValue: mockStatsService }],
  });
}

describe('DailyStats', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows loading state while data is pending', async () => {
    mockStatsService.getDaily.mockReturnValue(new Subject<StatsBucket[]>());

    await renderComponent();

    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('hides loading state after data arrives', async () => {
    mockStatsService.getDaily.mockReturnValue(of(mockBuckets));

    await renderComponent();

    expect(screen.queryByText('Loading...')).not.toBeInTheDocument();
  });

  it('renders table headers', async () => {
    mockStatsService.getDaily.mockReturnValue(of(mockBuckets));

    await renderComponent();

    expect(screen.getByText('Day (UTC)')).toBeInTheDocument();
    expect(screen.getByText('Requests')).toBeInTheDocument();
    expect(screen.getByText('LLM Calls')).toBeInTheDocument();
    expect(screen.getByText('Input Tokens')).toBeInTheDocument();
    expect(screen.getByText('Output Tokens')).toBeInTheDocument();
  });

  it('renders a row of bucket data', async () => {
    mockStatsService.getDaily.mockReturnValue(of(mockBuckets));

    await renderComponent();

    // '15 Jan 2024' format is unique to the table (chart uses 'dd MMM' without year)
    expect(screen.getByText('15 Jan 2024')).toBeInTheDocument();
    // requestCount (42) appears in both the table cell and the chart bar span
    expect(screen.getAllByText('42').length).toBeGreaterThan(0);
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.getByText('1,000')).toBeInTheDocument();
    expect(screen.getByText('500')).toBeInTheDocument();
  });

  it('renders all bucket rows when there are multiple', async () => {
    const twoBuckets: StatsBucket[] = [
      { ...mockBuckets[0] },
      {
        timeBucket: '2024-01-16T00:00:00.000Z',
        requestCount: 7,
        llmRequestCount: 3,
        totalInputTokens: 300,
        totalOutputTokens: 150,
      },
    ];
    mockStatsService.getDaily.mockReturnValue(of(twoBuckets));

    await renderComponent();

    expect(screen.getByText('15 Jan 2024')).toBeInTheDocument();
    expect(screen.getByText('16 Jan 2024')).toBeInTheDocument();
  });

  it('renders the chart section', async () => {
    mockStatsService.getDaily.mockReturnValue(of(mockBuckets));

    const { container } = await renderComponent();

    expect(screen.getByText('Daily Requests')).toBeInTheDocument();
    expect(container.querySelector('.chart')).toBeInTheDocument();
  });

  it('sets the chart bar to 100% width for the sole bucket', async () => {
    mockStatsService.getDaily.mockReturnValue(of(mockBuckets));

    const { container } = await renderComponent();

    const bar = container.querySelector('.chart-bar') as HTMLElement;
    expect(bar.style.width).toBe('100%');
  });

  it('scales chart bars relative to the highest request count', async () => {
    const twoBuckets: StatsBucket[] = [
      { ...mockBuckets[0], requestCount: 100 },
      { ...mockBuckets[0], timeBucket: '2024-01-16T00:00:00.000Z', requestCount: 50 },
    ];
    mockStatsService.getDaily.mockReturnValue(of(twoBuckets));

    const { container } = await renderComponent();

    const bars = container.querySelectorAll<HTMLElement>('.chart-bar');
    expect(bars[0].style.width).toBe('100%');
    expect(bars[1].style.width).toBe('50%');
  });

  it('shows error message when the request fails', async () => {
    mockStatsService.getDaily.mockReturnValue(
      throwError(() => new Error('Network error'))
    );

    await renderComponent();

    expect(
      screen.getByText('Failed to load daily stats. Is the proxy running?')
    ).toBeInTheDocument();
  });

  it('shows empty state when the response is an empty array', async () => {
    mockStatsService.getDaily.mockReturnValue(of([]));

    await renderComponent();

    expect(screen.getByText('No data recorded yet.')).toBeInTheDocument();
  });

  it('calls getDaily with a 30-day date range', async () => {
    mockStatsService.getDaily.mockReturnValue(of([]));

    await renderComponent();

    expect(mockStatsService.getDaily).toHaveBeenCalledTimes(1);
    const [from, to] = mockStatsService.getDaily.mock.calls[0] as [string, string];
    const diffDays =
      (new Date(to).getTime() - new Date(from).getTime()) / (1000 * 60 * 60 * 24);
    expect(diffDays).toBeCloseTo(30, 0);
  });
});
