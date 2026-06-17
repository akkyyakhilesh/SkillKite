import { useReducer, useRef, useEffect, useState } from 'react';
import type { InteractiveOption } from '../../lib/chat-api';
import { sendMessage, getHistory } from '../../lib/chat-api';
import { chatReducer, initialChatState, getSessionKey } from '../../lib/chat-store';
import type { DisplayMessage } from '../../lib/chat-store';
import MessageBubble from './MessageBubble';
import ThinkingDots from './ThinkingDots';
import styles from './chat.module.css';

interface Props {
  onClose: () => void;
}

export default function ChatPanel({ onClose }: Props) {
  const [state, dispatch] = useReducer(chatReducer, initialChatState);
  const [inputText, setInputText] = useState('');
  const [initialized, setInitialized] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const sessionKeyRef = useRef('');
  const thinkingRef = useRef(false);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [state.messages, state.isThinking]);

  useEffect(() => {
    async function init() {
      const key = getSessionKey();
      sessionKeyRef.current = key;

      try {
        const history = await getHistory(key);
        if (history.messages && history.messages.length > 0) {
          const msgs: DisplayMessage[] = history.messages.map((m, i) => ({
            id: `hist-${i}`,
            role: m.role.toLowerCase() as 'user' | 'assistant',
            blocks: [{ type: 'text' as const, body: m.content, options: null, buttonLabel: null, sectionTitle: null, document: null }],
            timestamp: new Date(m.createdAt).getTime(),
          }));
          dispatch({ type: 'load_history', messages: msgs });
          setInitialized(true);
          return;
        }
      } catch { /* no history, start fresh */ }

      try {
        dispatch({ type: 'set_thinking', value: true });
        const res = await sendMessage(key, 'Hi');
        dispatch({ type: 'add_bot_response', blocks: res.blocks });
      } catch (err) {
        dispatch({ type: 'set_error', error: 'Could not connect. Please try again.' });
      }
      setInitialized(true);
    }
    init();
  }, []);

  async function send(text: string, displayText?: string) {
    if (!text.trim() || thinkingRef.current) return;
    const key = sessionKeyRef.current;

    thinkingRef.current = true;
    dispatch({ type: 'add_user_message', text: (displayText ?? text).trim() });
    dispatch({ type: 'set_thinking', value: true });
    setInputText('');

    try {
      const res = await sendMessage(key, text.trim());
      dispatch({ type: 'add_bot_response', blocks: res.blocks });
    } catch (err: any) {
      if (err?.message === 'rate_limited') {
        dispatch({ type: 'set_error', error: 'Too many messages — wait a moment and try again.' });
      } else {
        dispatch({ type: 'set_error', error: 'Something went wrong. Please try again.' });
      }
    }
    thinkingRef.current = false;
    inputRef.current?.focus();
  }

  function handleOptionSelect(option: InteractiveOption, messageId: string) {
    dispatch({ type: 'disable_options', messageId });
    send(option.id, option.title);
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    send(inputText);
  }

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <div className={styles.avatar}>🪁</div>
        <div className={styles.headerText}>
          <h3>SkillKite</h3>
          <p>AI career guide · Free</p>
        </div>
        <button className={styles.closeBtn} onClick={onClose} aria-label="Close chat">✕</button>
      </div>

      <div className={styles.messages}>
        {state.messages.map((msg) => (
          <MessageBubble
            key={msg.id}
            role={msg.role}
            blocks={msg.blocks}
            timestamp={msg.timestamp}
            onOptionSelect={(opt) => handleOptionSelect(opt, msg.id)}
          />
        ))}
        {state.isThinking && <ThinkingDots />}
        <div ref={messagesEndRef} />
      </div>

      {state.error && <div className={styles.error}>{state.error}</div>}

      <form className={styles.inputArea} onSubmit={handleSubmit}>
        <input
          ref={inputRef}
          className={styles.input}
          type="text"
          placeholder="Type a message..."
          value={inputText}
          onChange={(e) => setInputText(e.target.value)}
          disabled={state.isThinking}
        />
        <button
          className={styles.sendBtn}
          type="submit"
          disabled={state.isThinking || !inputText.trim()}
          aria-label="Send message"
        >
          <svg viewBox="0 0 24 24">
            <path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z" />
          </svg>
        </button>
      </form>
    </div>
  );
}
