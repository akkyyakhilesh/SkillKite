import type { WebChatBlock } from './chat-api';

const SESSION_KEY_STORAGE = 'sk_session_key';

export function getSessionKey(): string {
  if (typeof window === 'undefined') return '';
  let key = localStorage.getItem(SESSION_KEY_STORAGE);
  if (!key || !key.startsWith('W')) {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let id = 'W';
    for (let i = 0; i < 14; i++) {
      id += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    key = id;
    localStorage.setItem(SESSION_KEY_STORAGE, key);
  }
  return key;
}

export interface DisplayMessage {
  id: string;
  role: 'user' | 'assistant';
  blocks: WebChatBlock[];
  timestamp: number;
}

export interface ChatState {
  messages: DisplayMessage[];
  isThinking: boolean;
  error: string | null;
}

export type ChatAction =
  | { type: 'add_user_message'; text: string }
  | { type: 'add_bot_response'; blocks: WebChatBlock[] }
  | { type: 'set_thinking'; value: boolean }
  | { type: 'set_error'; error: string | null }
  | { type: 'load_history'; messages: DisplayMessage[] }
  | { type: 'disable_options'; messageId: string };

let nextId = 0;

export function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case 'add_user_message':
      return {
        ...state,
        messages: [
          ...state.messages,
          {
            id: `msg-${++nextId}`,
            role: 'user',
            blocks: [{ type: 'text', body: action.text, options: null, buttonLabel: null, sectionTitle: null, document: null }],
            timestamp: Date.now(),
          },
        ],
        error: null,
      };

    case 'add_bot_response':
      return {
        ...state,
        messages: [
          ...state.messages,
          {
            id: `msg-${++nextId}`,
            role: 'assistant',
            blocks: action.blocks,
            timestamp: Date.now(),
          },
        ],
        isThinking: false,
      };

    case 'set_thinking':
      return { ...state, isThinking: action.value };

    case 'set_error':
      return { ...state, error: action.error, isThinking: false };

    case 'load_history':
      return { ...state, messages: action.messages };

    case 'disable_options': {
      return {
        ...state,
        messages: state.messages.map((m) =>
          m.id === action.messageId
            ? { ...m, blocks: m.blocks.map((b) => (b.options ? { ...b, _disabled: true } as any : b)) }
            : m
        ),
      };
    }

    default:
      return state;
  }
}

export const initialChatState: ChatState = {
  messages: [],
  isThinking: false,
  error: null,
};
