import { useState, useEffect } from 'react';
import styles from './chat.module.css';

export default function ThinkingDots() {
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    const id = setInterval(() => setElapsed(s => s + 1), 1000);
    return () => clearInterval(id);
  }, []);

  const label = elapsed < 5
    ? 'Thinking...'
    : elapsed < 15
      ? 'Working on it...'
      : 'Generating your guide — hang tight...';

  return (
    <>
      <div className={styles.thinking}>
        <span className={styles.dot} />
        <span className={styles.dot} />
        <span className={styles.dot} />
      </div>
      <div className={styles.thinkingLabel}>{label}</div>
    </>
  );
}
