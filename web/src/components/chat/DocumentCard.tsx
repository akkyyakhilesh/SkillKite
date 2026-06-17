import type { DocumentInfo } from '../../lib/chat-api';
import styles from './chat.module.css';

interface Props {
  document: DocumentInfo;
}

export default function DocumentCard({ document }: Props) {
  return (
    <div className={styles.docCard}>
      <div className={styles.docIcon}>PDF</div>
      <div className={styles.docInfo}>
        <div className={styles.docName}>{document.filename}</div>
        <div className={styles.docSize}>Personalized guide</div>
      </div>
      <a
        className={styles.docDownload}
        href={document.url}
        target="_blank"
        rel="noopener noreferrer"
        download
        aria-label="Download PDF"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
          <polyline points="7 10 12 15 17 10" />
          <line x1="12" y1="15" x2="12" y2="3" />
        </svg>
      </a>
    </div>
  );
}
