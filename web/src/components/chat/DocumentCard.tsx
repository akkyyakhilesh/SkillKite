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
      >
        Download
      </a>
    </div>
  );
}
