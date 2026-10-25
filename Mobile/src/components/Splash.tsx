import { useEffect, useState } from 'react';
import { NURSE } from '../data/demo';
import Mascot from './Mascot';

const SEEN_KEY = 'ahc-splash-seen';
const DURATION_MS = 3200; // matches the Figma timeline of "S0 Splash · First launch"
const FADE_MS = 400;

export function splashSeen(): boolean {
  try {
    return localStorage.getItem(SEEN_KEY) === '1';
  } catch {
    return false;
  }
}

export function resetSplash() {
  try {
    localStorage.removeItem(SEEN_KEY);
  } catch {
    // storage unavailable: the splash simply shows on every load
  }
  window.dispatchEvent(new Event('ahc-show-splash'));
}

/** First-launch loading screen. Tap to skip; it is remembered so it only shows once. */
export default function Splash({ onDone }: { onDone: () => void }) {
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    const t = window.setTimeout(() => setLeaving(true), DURATION_MS);
    return () => window.clearTimeout(t);
  }, []);

  useEffect(() => {
    if (!leaving) return;
    try {
      localStorage.setItem(SEEN_KEY, '1');
    } catch {
      // ignore
    }
    const t = window.setTimeout(onDone, FADE_MS);
    return () => window.clearTimeout(t);
  }, [leaving, onDone]);

  return (
    <div className={leaving ? 'splash leaving' : 'splash'} onClick={() => setLeaving(true)} role="status" aria-live="polite">
      <div className="splash-art">
        <div className="splash-halo" />
        <div className="splash-mascot">
          <Mascot size={200} />
        </div>
      </div>
      <h1 className="splash-title">AI-Assisted Home Care</h1>
      <p className="splash-sub">Your care guide is getting ready…</p>
      <div className="splash-dots" aria-hidden="true">
        <span />
        <span />
        <span />
      </div>
      <p className="splash-foot">
        {NURSE.unit} · {NURSE.name}
      </p>
      <span className="splash-skip">Tap to skip</span>
    </div>
  );
}
