import type { WebChatBlock, InteractiveOption } from '../../lib/chat-api';
import OptionChips from './OptionChips';
import ListMenu from './ListMenu';
import DocumentCard from './DocumentCard';
import styles from './chat.module.css';

interface Props {
  role: 'user' | 'assistant';
  blocks: (WebChatBlock & { _disabled?: boolean })[];
  timestamp: number;
  onOptionSelect: (option: InteractiveOption) => void;
}

function formatBody(text: string) {
  return text
    .replace(/\*(.+?)\*/g, '<strong>$1</strong>')
    .replace(/\n/g, '<br/>');
}

function formatTime(ts: number) {
  return new Date(ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

export default function MessageBubble({ role, blocks, timestamp, onOptionSelect }: Props) {
  const isUser = role === 'user';
  const wrapClass = isUser ? styles.msgUser : styles.msgBot;
  const bubbleClass = `${styles.bubble} ${isUser ? styles.bubbleUser : styles.bubbleBot}`;

  return (
    <div className={wrapClass}>
      {blocks.map((block, i) => {
        switch (block.type) {
          case 'text':
            return (
              <div
                key={i}
                className={bubbleClass}
                dangerouslySetInnerHTML={{ __html: formatBody(block.body) }}
              />
            );

          case 'buttons':
            return (
              <div key={i}>
                <div
                  className={bubbleClass}
                  dangerouslySetInnerHTML={{ __html: formatBody(block.body) }}
                />
                {block.options && (
                  <OptionChips
                    options={block.options}
                    disabled={!!block._disabled}
                    onSelect={onOptionSelect}
                  />
                )}
              </div>
            );

          case 'list':
            return (
              <div key={i}>
                <div
                  className={bubbleClass}
                  dangerouslySetInnerHTML={{ __html: formatBody(block.body) }}
                />
                {block.options && (
                  <ListMenu
                    options={block.options}
                    sectionTitle={block.sectionTitle}
                    disabled={!!block._disabled}
                    onSelect={onOptionSelect}
                  />
                )}
              </div>
            );

          case 'document':
            return (
              <div key={i}>
                {block.body && (
                  <div
                    className={bubbleClass}
                    dangerouslySetInnerHTML={{ __html: formatBody(block.body) }}
                  />
                )}
                {block.document && <DocumentCard document={block.document} />}
              </div>
            );

          default:
            return null;
        }
      })}
      <div className={styles.timestamp}>{formatTime(timestamp)}</div>
    </div>
  );
}
