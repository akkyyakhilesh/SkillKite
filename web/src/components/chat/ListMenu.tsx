import type { InteractiveOption } from '../../lib/chat-api';
import styles from './chat.module.css';

interface Props {
  options: InteractiveOption[];
  sectionTitle?: string | null;
  disabled?: boolean;
  onSelect: (option: InteractiveOption) => void;
}

export default function ListMenu({ options, sectionTitle, disabled, onSelect }: Props) {
  return (
    <div className={styles.listMenu}>
      {sectionTitle && <div className={styles.listHeader}>{sectionTitle}</div>}
      {options.map((opt) => (
        <div
          key={opt.id}
          className={`${styles.listItem} ${disabled ? styles.listItemDisabled : ''}`}
          onClick={() => !disabled && onSelect(opt)}
          role="button"
          tabIndex={disabled ? -1 : 0}
          onKeyDown={(e) => { if (!disabled && (e.key === 'Enter' || e.key === ' ')) onSelect(opt); }}
        >
          <div className={styles.listItemTitle}>{opt.title}</div>
          {opt.description && <div className={styles.listItemDesc}>{opt.description}</div>}
        </div>
      ))}
    </div>
  );
}
