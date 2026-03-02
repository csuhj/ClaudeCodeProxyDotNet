import { jest } from '@jest/globals';
import { render, screen, fireEvent } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { RequestDetailPanel } from './request-detail-panel';
import { RequestsService, LlmRequestDetail } from '../../services/requests.service';

const SSE_BODY = [
  'event: message_start',
  'data: {"type":"message_start","message":{"model":"claude-sonnet-4-6","usage":{"input_tokens":80}}}',
  '',
  'event: content_block_delta',
  'data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}',
  '',
  'event: content_block_delta',
  'data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" streaming!"}}',
  '',
  'event: message_delta',
  'data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}',
  '',
  'event: message_stop',
  'data: {"type":"message_stop"}',
].join('\n');

const mockDetail: LlmRequestDetail = {
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
  requestHeaders: '{"content-type":"application/json"}',
  requestBody: JSON.stringify({
    model: 'claude-sonnet-4-6',
    max_tokens: 1024,
    system: 'You are a helpful assistant.',
    messages: [
      { role: 'user', content: 'What is the capital of France?' },
      { role: 'assistant', content: 'The capital of France is Paris.' },
    ],
    tools: [{ name: 'get_weather', description: 'Gets weather data.' }],
  }),
  responseHeaders: '{"content-type":"application/json"}',
  responseBody: JSON.stringify({
    stop_reason: 'end_turn',
    content: [{ type: 'text', text: 'The response from the assistant.' }],
    usage: { input_tokens: 100, output_tokens: 50 },
  }),
  isStreaming: false,
};

const streamingDetail: LlmRequestDetail = {
  ...mockDetail,
  isStreaming: true,
  responseHeaders: '{"content-type":"text/event-stream"}',
  responseBody: SSE_BODY,
};

const mockRequestsService = {
  getDetail: jest.fn(),
};

async function renderComponent(requestId = 1) {
  return render(RequestDetailPanel, {
    componentInputs: { requestId },
    providers: [{ provide: RequestsService, useValue: mockRequestsService }],
  });
}

describe('RequestDetailPanel', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows loading state while data is pending', async () => {
    mockRequestsService.getDetail.mockReturnValue(new Subject<LlmRequestDetail>());

    await renderComponent();

    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('hides loading after data arrives', async () => {
    mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

    await renderComponent();

    expect(screen.queryByText('Loading...')).not.toBeInTheDocument();
  });

  it('shows error message when request fails', async () => {
    mockRequestsService.getDetail.mockReturnValue(
      throwError(() => new Error('Network error'))
    );

    await renderComponent();

    expect(screen.getByText('Failed to load request detail.')).toBeInTheDocument();
  });

  it('emits closed event when close button is clicked', async () => {
    mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

    const { fixture } = await renderComponent();
    const spy = jest.spyOn(fixture.componentInstance.closed, 'emit');

    fireEvent.click(screen.getByText(/Close/));

    expect(spy).toHaveBeenCalledTimes(1);
  });

  it('calls getDetail with the provided requestId', async () => {
    mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

    await renderComponent(42);

    expect(mockRequestsService.getDetail).toHaveBeenCalledWith(42);
  });

  it('re-fetches when requestId input changes', async () => {
    mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

    const { rerender } = await renderComponent(1);
    expect(mockRequestsService.getDetail).toHaveBeenCalledWith(1);

    await rerender({ componentInputs: { requestId: 2 } });
    expect(mockRequestsService.getDetail).toHaveBeenCalledWith(2);
    expect(mockRequestsService.getDetail).toHaveBeenCalledTimes(2);
  });

  describe('metadata section', () => {
    it('renders formatted timestamp', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText(/15 Jan 2024/)).toBeInTheDocument();
    });

    it('renders HTTP method and path together', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('POST /v1/messages')).toBeInTheDocument();
    });

    it('renders status code', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('200')).toBeInTheDocument();
    });

    it('renders duration', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('1.2s')).toBeInTheDocument();
    });

    it('renders token summary', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText(/In: 100/)).toBeInTheDocument();
    });
  });

  describe('request body — formatted view', () => {
    it('shows model and max_tokens params', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText(/1024/)).toBeInTheDocument();
    });

    it('shows system prompt', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('You are a helpful assistant.')).toBeInTheDocument();
    });

    it('renders user message bubble', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('What is the capital of France?')).toBeInTheDocument();
    });

    it('renders assistant message bubble', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('The capital of France is Paris.')).toBeInTheDocument();
    });

    it('shows tools collapsible with tool name', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText(/Tools \(1\)/)).toBeInTheDocument();
    });

    it('shows "No request body" when body is null', async () => {
      const detail = { ...mockDetail, requestBody: null };
      mockRequestsService.getDetail.mockReturnValue(of(detail));

      await renderComponent();

      expect(screen.getByText('No request body.')).toBeInTheDocument();
    });
  });

  describe('request body — raw view toggle', () => {
    it('shows raw JSON when the first Raw button is clicked', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      const { container } = await renderComponent();

      const rawBtns = screen.getAllByText('Raw');
      fireEvent.click(rawBtns[0]);

      const pre = container.querySelector('.request-section pre.raw-view');
      expect(pre?.textContent).toContain('max_tokens');
      expect(pre?.textContent).toContain('1024');
    });

    it('button label switches to Formatted after clicking Raw', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      const rawBtns = screen.getAllByText('Raw');
      fireEvent.click(rawBtns[0]);

      expect(screen.getAllByText('Formatted').length).toBeGreaterThan(0);
    });
  });

  describe('response body — non-streaming formatted view', () => {
    it('shows stop reason', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText(/end_turn/)).toBeInTheDocument();
    });

    it('shows response text content', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('The response from the assistant.')).toBeInTheDocument();
    });

    it('shows "No response body captured" when body is null', async () => {
      const detail = { ...mockDetail, responseBody: null };
      mockRequestsService.getDetail.mockReturnValue(of(detail));

      await renderComponent();

      expect(screen.getByText('No response body captured.')).toBeInTheDocument();
    });

    it('shows raw JSON in pre block when Raw button is clicked', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      const { container } = await renderComponent();

      const rawBtns = screen.getAllByText('Raw');
      fireEvent.click(rawBtns[1]);

      const pre = container.querySelector('.response-section pre.raw-view');
      expect(pre?.textContent).toContain('stop_reason');
    });
  });

  describe('response body — streaming formatted view', () => {
    it('shows reconstructed assistant text from text_delta events', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(streamingDetail));

      await renderComponent();

      expect(screen.getByText('Hello streaming!')).toBeInTheDocument();
    });

    it('shows model from message_start event', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(streamingDetail));

      await renderComponent();

      expect(screen.getAllByText(/claude-sonnet-4-6/).length).toBeGreaterThan(0);
    });

    it('shows stop reason from message_delta event', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(streamingDetail));

      await renderComponent();

      expect(screen.getByText(/end_turn/)).toBeInTheDocument();
    });

    it('shows raw SSE lines in pre block when Raw button is clicked', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(streamingDetail));

      const { container } = await renderComponent();

      const rawBtns = screen.getAllByText('Raw');
      fireEvent.click(rawBtns[1]);

      const pre = container.querySelector('.response-section pre.raw-view');
      expect(pre?.textContent).toContain('message_start');
      expect(pre?.textContent).toContain('text_delta');
    });
  });

  describe('headers section', () => {
    it('renders Request Headers and Response Headers summaries', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      expect(screen.getByText('Request Headers')).toBeInTheDocument();
      expect(screen.getByText('Response Headers')).toBeInTheDocument();
    });

    it('shows header key-value pairs when details are opened', async () => {
      mockRequestsService.getDetail.mockReturnValue(of(mockDetail));

      await renderComponent();

      // Both request and response header sections render content-type
      expect(screen.getAllByText('content-type').length).toBeGreaterThanOrEqual(1);
    });
  });
});
