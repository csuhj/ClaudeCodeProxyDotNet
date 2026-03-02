import { jest } from '@jest/globals';
import { render, screen, fireEvent } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { LlmRequestsList } from './llm-requests-list';
import { RequestsService, LlmRequestSummary } from '../../services/requests.service';

const mockRequest: LlmRequestSummary = {
  id: 1,
  timestamp: '2024-01-15T10:05:30.000Z',
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

const mockRequestsService = {
  getRecent: jest.fn(),
};

async function renderComponent() {
  return render(LlmRequestsList, {
    providers: [{ provide: RequestsService, useValue: mockRequestsService }],
  });
}

describe('LlmRequestsList', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows loading state while data is pending', async () => {
    mockRequestsService.getRecent.mockReturnValue(new Subject<LlmRequestSummary[]>());

    await renderComponent();

    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('hides loading state after data arrives', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([mockRequest]));

    await renderComponent();

    expect(screen.queryByText('Loading...')).not.toBeInTheDocument();
  });

  it('renders table headers', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([mockRequest]));

    await renderComponent();

    expect(screen.getByText('Time (UTC)')).toBeInTheDocument();
    expect(screen.getByText('Model')).toBeInTheDocument();
    expect(screen.getByText('Path')).toBeInTheDocument();
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Duration')).toBeInTheDocument();
    expect(screen.getByText('Input Tokens')).toBeInTheDocument();
    expect(screen.getByText('Output Tokens')).toBeInTheDocument();
  });

  it('renders a row with timestamp, model, path, status, and duration', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([mockRequest]));

    await renderComponent();

    expect(screen.getByText('15 Jan 10:05:30')).toBeInTheDocument();
    expect(screen.getByText('claude-sonnet-4-6')).toBeInTheDocument();
    expect(screen.getByText('/v1/messages')).toBeInTheDocument();
    expect(screen.getByText('200')).toBeInTheDocument();
    expect(screen.getByText('1.2s')).toBeInTheDocument();
  });

  it('renders input and output token counts', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([mockRequest]));

    await renderComponent();

    expect(screen.getAllByText('100').length).toBeGreaterThan(0);
    expect(screen.getAllByText('50').length).toBeGreaterThan(0);
  });

  it('shows — for null model', async () => {
    const req = { ...mockRequest, model: null };
    mockRequestsService.getRecent.mockReturnValue(of([req]));

    await renderComponent();

    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows — for null token counts', async () => {
    const req = { ...mockRequest, inputTokens: null, outputTokens: null };
    mockRequestsService.getRecent.mockReturnValue(of([req]));

    await renderComponent();

    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(2);
  });

  it('formats sub-second duration as ms', async () => {
    const req = { ...mockRequest, durationMs: 456 };
    mockRequestsService.getRecent.mockReturnValue(of([req]));

    await renderComponent();

    expect(screen.getByText('456ms')).toBeInTheDocument();
  });

  it('truncates long paths to 60 characters with ellipsis', async () => {
    const longPath = '/v1/' + 'a'.repeat(80);
    const req = { ...mockRequest, path: longPath };
    mockRequestsService.getRecent.mockReturnValue(of([req]));

    await renderComponent();

    const cell = screen.getByText(/…$/);
    expect(cell.textContent!.length).toBeLessThanOrEqual(61);
  });

  it('emits requestSelected with the request id when a row is clicked', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([mockRequest]));

    const { fixture } = await renderComponent();
    const spy = jest.spyOn(fixture.componentInstance.requestSelected, 'emit');

    const row = screen.getByText('claude-sonnet-4-6').closest('tr')!;
    fireEvent.click(row);

    expect(spy).toHaveBeenCalledWith(1);
  });

  it('shows error message when the request fails', async () => {
    mockRequestsService.getRecent.mockReturnValue(
      throwError(() => new Error('Network error'))
    );

    await renderComponent();

    expect(
      screen.getByText('Failed to load LLM requests. Is the proxy running?')
    ).toBeInTheDocument();
  });

  it('shows empty state when the response is an empty array', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([]));

    await renderComponent();

    expect(
      screen.getByText('No LLM requests in the last 24 hours.')
    ).toBeInTheDocument();
  });

  it('calls getRecent with no arguments', async () => {
    mockRequestsService.getRecent.mockReturnValue(of([]));

    await renderComponent();

    expect(mockRequestsService.getRecent).toHaveBeenCalledTimes(1);
    expect(mockRequestsService.getRecent).toHaveBeenCalledWith();
  });

  it('renders all rows when there are multiple requests', async () => {
    const second = { ...mockRequest, id: 2, model: 'claude-haiku-4-5' };
    mockRequestsService.getRecent.mockReturnValue(of([mockRequest, second]));

    await renderComponent();

    expect(screen.getByText('claude-sonnet-4-6')).toBeInTheDocument();
    expect(screen.getByText('claude-haiku-4-5')).toBeInTheDocument();
  });
});
