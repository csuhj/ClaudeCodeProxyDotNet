import {
  Component,
  Input,
  Output,
  EventEmitter,
  OnChanges,
  SimpleChanges,
  inject,
  signal,
  computed,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RequestsService, LlmRequestDetail } from '../../services/requests.service';

interface ContentBlock {
  type: string;
  text?: string;
}

interface AnthropicMessage {
  role: string;
  content: string | ContentBlock[];
}

interface AnthropicTool {
  name: string;
  description?: string;
}

interface AnthropicRequestBody {
  model?: string;
  max_tokens?: number;
  temperature?: number;
  system?: string | ContentBlock[];
  messages?: AnthropicMessage[];
  tools?: AnthropicTool[];
}

interface AnthropicResponseBody {
  stop_reason?: string;
  content?: ContentBlock[];
  usage?: { input_tokens?: number; output_tokens?: number };
}

interface StreamingView {
  model?: string;
  assistantText: string;
  inputTokens?: number;
  outputTokens?: number;
  stopReason?: string;
}

@Component({
  selector: 'app-request-detail-panel',
  imports: [DatePipe],
  templateUrl: './request-detail-panel.html',
  styleUrl: './request-detail-panel.scss',
})
export class RequestDetailPanel implements OnChanges {
  private readonly requestsService = inject(RequestsService);

  @Input({ required: true }) requestId!: number;
  @Output() closed = new EventEmitter<void>();

  readonly detail = signal<LlmRequestDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly requestViewRaw = signal(false);
  readonly responseViewRaw = signal(false);

  readonly parsedRequestBody = computed<AnthropicRequestBody | null>(() => {
    const body = this.detail()?.requestBody;
    if (!body) return null;
    try { return JSON.parse(body); } catch { return null; }
  });

  readonly rawRequestBody = computed(() => {
    const body = this.detail()?.requestBody;
    if (!body) return '';
    try { return JSON.stringify(JSON.parse(body), null, 2); } catch { return body; }
  });

  readonly parsedResponseJson = computed<AnthropicResponseBody | null>(() => {
    const d = this.detail();
    if (!d || d.isStreaming || !d.responseBody) return null;
    try { return JSON.parse(d.responseBody); } catch { return null; }
  });

  readonly parsedStreamingResponse = computed<StreamingView | null>(() => {
    const d = this.detail();
    if (!d || !d.isStreaming || !d.responseBody) return null;
    return this.buildStreamingView(d.responseBody);
  });

  readonly rawResponseBody = computed(() => {
    const d = this.detail();
    if (!d?.responseBody) return '';
    if (d.isStreaming) return d.responseBody;
    try { return JSON.stringify(JSON.parse(d.responseBody), null, 2); } catch { return d.responseBody; }
  });

  readonly requestHeaderEntries = computed(() => this.parseHeaders(this.detail()?.requestHeaders ?? ''));
  readonly responseHeaderEntries = computed(() => this.parseHeaders(this.detail()?.responseHeaders ?? ''));

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['requestId']) {
      this.loadDetail();
    }
  }

  private loadDetail(): void {
    this.loading.set(true);
    this.error.set(null);
    this.detail.set(null);
    this.requestViewRaw.set(false);
    this.responseViewRaw.set(false);
    this.requestsService.getDetail(this.requestId).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load request detail.');
        this.loading.set(false);
        console.error(err);
      },
    });
  }

  private buildStreamingView(body: string): StreamingView {
    const events = body
      .split('\n')
      .filter(l => l.startsWith('data: '))
      .map(l => l.slice(6).trim())
      .filter(l => l && l !== '[DONE]')
      .map(l => { try { return JSON.parse(l); } catch { return null; } })
      .filter(Boolean);

    let assistantText = '';
    let inputTokens: number | undefined;
    let outputTokens: number | undefined;
    let stopReason: string | undefined;
    let model: string | undefined;

    for (const event of events) {
      if (event.type === 'message_start') {
        model = event.message?.model;
        inputTokens = event.message?.usage?.input_tokens;
      } else if (event.type === 'content_block_delta' && event.delta?.type === 'text_delta') {
        assistantText += event.delta.text ?? '';
      } else if (event.type === 'message_delta') {
        outputTokens = event.usage?.output_tokens;
        stopReason = event.delta?.stop_reason;
      }
    }

    return { model, assistantText, inputTokens, outputTokens, stopReason };
  }

  private parseHeaders(headers: string): [string, string][] {
    try {
      const obj = JSON.parse(headers);
      return Object.entries(obj) as [string, string][];
    } catch {
      return [];
    }
  }

  getSystemText(system: string | ContentBlock[] | undefined): string | null {
    if (!system) return null;
    if (typeof system === 'string') return system;
    const text = system.filter(b => b.type === 'text').map(b => b.text ?? '').join('');
    return text || null;
  }

  getMessageText(content: string | ContentBlock[]): string {
    if (typeof content === 'string') return content;
    return content.filter(b => b.type === 'text').map(b => b.text ?? '').join('');
  }

  statusClass(code: number): string {
    if (code >= 200 && code < 300) return 'status-ok';
    if (code >= 400) return 'status-err';
    return '';
  }

  formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(1)}s`;
  }

  onOverlayClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closed.emit();
    }
  }
}
