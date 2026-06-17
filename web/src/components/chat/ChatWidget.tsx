import { useState } from 'react';
import ChatPanel from './ChatPanel';
import styles from './chat.module.css';

export default function ChatWidget() {
  const [open, setOpen] = useState(false);

  return (
    <>
      {open && <ChatPanel onClose={() => setOpen(false)} />}
      <button
        className={`${styles.fab} ${open ? styles.fabHideMobile : ''}`}
        onClick={() => setOpen(!open)}
        aria-label={open ? 'Close chat' : 'Open chat'}
      >
        {open ? (
          <svg viewBox="0 0 24 24">
            <path d="M18.3 5.71a1 1 0 00-1.42 0L12 10.59 7.12 5.71A1 1 0 105.7 7.12L10.59 12l-4.88 4.88a1 1 0 101.42 1.42L12 13.41l4.88 4.88a1 1 0 001.42-1.42L13.41 12l4.88-4.88a1 1 0 000-1.41z" />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" fill="none">
            <path
              d="M21 11.5C21 16.75 16.75 21 11.5 21C9.83 21 8.26 20.56 6.89 19.78L2 21L3.22 16.11C2.44 14.74 2 13.17 2 11.5C2 6.25 6.25 2 11.5 2C16.75 2 21 6.25 21 11.5Z"
              stroke="white"
              strokeWidth="1.8"
              fill="none"
            />
            <circle cx="7.5" cy="11.5" r="1.2" fill="white" />
            <circle cx="11.5" cy="11.5" r="1.2" fill="white" />
            <circle cx="15.5" cy="11.5" r="1.2" fill="white" />
          </svg>
        )}
      </button>
    </>
  );
}
