import styles from './chat.module.css';

export default function ThinkingDots() {
  return (
    <>
      <div className={styles.thinking}>
        <span className={styles.dot} />
        <span className={styles.dot} />
        <span className={styles.dot} />
      </div>
      <div className={styles.thinkingLabel}>Thinking...</div>
    </>
  );
}
