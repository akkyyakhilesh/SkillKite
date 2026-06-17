export interface InteractiveOption {
  id: string;
  title: string;
  description?: string | null;
}

export interface DocumentInfo {
  url: string;
  filename: string;
}

export interface WebChatBlock {
  type: 'text' | 'buttons' | 'list' | 'document';
  body: string;
  options?: InteractiveOption[] | null;
  buttonLabel?: string | null;
  sectionTitle?: string | null;
  document?: DocumentInfo | null;
}

export interface StartResponse {
  sessionKey: string;
  sessionId: string;
}

export interface MessageResponse {
  ok: boolean;
  blocks: WebChatBlock[];
}

export interface HistoryMessage {
  role: string;
  content: string;
  createdAt: string;
}

export interface HistoryResponse {
  sessionId: string | null;
  status?: string;
  messages: HistoryMessage[];
}

const API_BASE = 'https://bot.skillkite.in';
const TIMEOUT_MS = 180_000;

function fetchWithTimeout(url: string, init?: RequestInit): Promise<Response> {
  const controller = new AbortController();
  const id = setTimeout(() => controller.abort(), TIMEOUT_MS);
  return fetch(url, { ...init, signal: controller.signal }).finally(() => clearTimeout(id));
}

export async function startSession(sessionKey: string): Promise<StartResponse> {
  const res = await fetchWithTimeout(`${API_BASE}/api/web-chat/start`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionKey }),
  });
  if (!res.ok) throw new Error(`Start failed: ${res.status}`);
  return res.json();
}

export async function sendMessage(
  sessionKey: string,
  text: string,
  name?: string
): Promise<MessageResponse> {
  const res = await fetchWithTimeout(`${API_BASE}/api/web-chat/message`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Session-Key': sessionKey,
    },
    body: JSON.stringify({ sessionKey, text, name }),
  });
  if (res.status === 429)
    throw new Error('rate_limited');
  if (!res.ok) throw new Error(`Message failed: ${res.status}`);
  return res.json();
}

export async function getHistory(sessionKey: string): Promise<HistoryResponse> {
  const res = await fetchWithTimeout(`${API_BASE}/api/web-chat/history/${encodeURIComponent(sessionKey)}`, {
    headers: { 'X-Session-Key': sessionKey },
  });
  if (!res.ok) throw new Error(`History failed: ${res.status}`);
  return res.json();
}
