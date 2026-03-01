import { jest } from '@jest/globals';
import { render, screen } from '@testing-library/angular';
import { of } from 'rxjs';
import { App } from './app';
import { StatsService } from './stats.service';

// Both HourlyStats and DailyStats (imported by App) call StatsService on init.
const mockStatsService = {
  getHourly: jest.fn().mockReturnValue(of([])),
  getDaily: jest.fn().mockReturnValue(of([])),
};

async function renderComponent() {
  return render(App, {
    providers: [{ provide: StatsService, useValue: mockStatsService }],
  });
}

describe('App', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should create the app', async () => {
    const { fixture } = await renderComponent();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the brand name', async () => {
    await renderComponent();
    expect(screen.getByText('Claude Code Proxy')).toBeInTheDocument();
  });

  it('should render the footer', async () => {
    await renderComponent();
    expect(screen.getByText('ClaudeCodeProxy — usage recorded locally')).toBeInTheDocument();
  });

  it('should render both stats sections', async () => {
    await renderComponent();
    expect(screen.getByText(/Requests per Hour/)).toBeInTheDocument();
    expect(screen.getByText(/Requests per Day/)).toBeInTheDocument();
  });
});
