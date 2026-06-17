import type { InteractiveOption } from '../../lib/chat-api';
import styles from './chat.module.css';

interface Props {
  options: InteractiveOption[];
  disabled?: boolean;
  onSelect: (option: InteractiveOption) => void;
}

export default function OptionChips({ options, disabled, onSelect }: Props) {
  return (
    <div className={styles.chips}>
      {options.map((opt) => (
        <button
          key={opt.id}
          className={`${styles.chip} ${disabled ? styles.chipDisabled : ''}`}
          onClick={() => !disabled && onSelect(opt)}
          disabled={disabled}
        >
          {opt.title}
        </button>
      ))}
    </div>
  );
}
