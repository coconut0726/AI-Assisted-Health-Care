import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

const ICONS = {
  back: 'M15 18l-6-6 6-6',
  chevron: 'M9 18l6-6-6-6',
  send: 'M12 19V5M5 12l7-7 7 7',
  check: 'M20 6L9 17l-5-5',
  plus: 'M12 5v14M5 12h14',
  trash: 'M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6',
  list: 'M9 6h12M9 12h12M9 18h12M4 6h.01M4 12h.01M4 18h.01',
  chat: 'M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z',
  home: 'M3 11l9-8 9 8M5 10v10h14V10',
} as const;

export type IconName = keyof typeof ICONS;

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={ICONS[name]} />
    </svg>
  );
}

export function Screen({ children }: { children: ReactNode }) {
  return <div className="screen">{children}</div>;
}

export function Nav({ title, sub, back, large }: { title: string; sub?: string; back?: string; large?: boolean }) {
  return (
    <header className="nav">
      {back && (
        <Link to={back} className="icon-btn" aria-label="Back">
          <Icon name="back" />
        </Link>
      )}
      <div className="nav-text">
        <h1 className={large ? 'nav-title large' : 'nav-title'}>{title}</h1>
        {sub && <p className="nav-sub">{sub}</p>}
      </div>
    </header>
  );
}

export function Tabs({ items }: { items: { label: string; to: string; active: boolean }[] }) {
  return (
    <nav className="tabs">
      {items.map((t) => (
        <Link key={t.label} to={t.to} replace className={t.active ? 'tab active' : 'tab'}>
          {t.label}
        </Link>
      ))}
    </nav>
  );
}

export function Segmented({ items }: { items: { label: string; active: boolean; onSelect: () => void }[] }) {
  return (
    <div className="segmented" role="tablist">
      {items.map((t) => (
        <button key={t.label} role="tab" aria-selected={t.active} className={t.active ? 'seg active' : 'seg'} onClick={t.onSelect}>
          {t.label}
        </button>
      ))}
    </div>
  );
}

export type Tone = 'primary' | 'neutral' | 'red' | 'amber' | 'green' | 'lav';

export function Pill({ children, tone = 'neutral' }: { children: ReactNode; tone?: Tone }) {
  return <span className={`pill tone-${tone}`}>{children}</span>;
}

export function Banner({ children, tone, action }: { children: ReactNode; tone: Tone; action?: ReactNode }) {
  return (
    <div className={`banner tone-${tone}`}>
      <span>{children}</span>
      {action}
    </div>
  );
}

export function Body({ children }: { children: ReactNode }) {
  return <main className="body">{children}</main>;
}

export function BottomTabs({ active, unread }: { active: 'instruction' | 'feedback'; unread?: boolean }) {
  return (
    <nav className="bottom-tabs">
      <Link to="/patient/instruction" replace className={active === 'instruction' ? 'btab active' : 'btab'}>
        <Icon name="list" size={22} />
        <span>Instruction</span>
      </Link>
      <Link to="/patient/feedback" replace className={active === 'feedback' ? 'btab active' : 'btab'}>
        <span className="btab-icon">
          <Icon name="chat" size={22} />
          {unread && <span className="dot" />}
        </span>
        <span>Feedback</span>
      </Link>
    </nav>
  );
}

export function DayChip({ children }: { children: ReactNode }) {
  return (
    <div className="day-chip">
      <Pill>{children}</Pill>
    </div>
  );
}

export function Bubble({ text, time, mine }: { text: string; time: string; mine: boolean }) {
  return (
    <div className={mine ? 'msg mine' : 'msg'}>
      <div className="bubble">
        <p>{text}</p>
        <time>{time}</time>
      </div>
    </div>
  );
}
