import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { resetSplash } from './components/Splash';
import { initialState } from './data/demo';
import type { DemoState, Message, Requirement, Step } from './types';

// Demo-only persistence: state lives in localStorage, so a nurse tab and a patient tab
// in the same browser stay in sync through the `storage` event. No backend.
const KEY = 'ahc-demo-v1';

function load(): DemoState {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return JSON.parse(raw) as DemoState;
  } catch {
    // fall through to a fresh demo
  }
  return initialState();
}

function save(state: DemoState) {
  try {
    localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    // storage unavailable (private mode): the demo still works within this tab
  }
}

export function patientMessages(s: DemoState): Message[] {
  return s.messages.filter((m) => m.kind === 'text' || s.reportSent);
}

interface Store {
  state: DemoState;
  saveStep: (step: Step) => void;
  saveRequirement: (req: Requirement) => void;
  markInstructionSeen: () => void;
  markMessagesSeen: () => void;
  sendReport: () => void;
  sendMessage: (text: string) => void;
  reset: () => void;
}

const Ctx = createContext<Store | null>(null);

export function DemoProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<DemoState>(load);

  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === KEY || e.key === null) setState(load());
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const update = useCallback((fn: (s: DemoState) => DemoState) => {
    setState((prev) => {
      const next = fn(prev);
      if (next !== prev) save(next);
      return next;
    });
  }, []);

  const store = useMemo<Store>(() => {
    const now = () => new Date().toISOString();
    return {
      state,
      saveStep: (step) =>
        update((s) => {
          const old = s.instruction.steps.find((x) => x.id === step.id);
          if (old && JSON.stringify(old) === JSON.stringify(step)) return s;
          const version = s.instruction.version + 1;
          return {
            ...s,
            instruction: { ...s.instruction, version, steps: s.instruction.steps.map((x) => (x.id === step.id ? step : x)) },
            changes: [...s.changes, { version, kind: 'step', id: step.id, at: now() }],
          };
        }),
      saveRequirement: (req) =>
        update((s) => {
          const old = s.instruction.requirements.find((x) => x.id === req.id);
          if (old && JSON.stringify(old) === JSON.stringify(req)) return s;
          const version = s.instruction.version + 1;
          return {
            ...s,
            instruction: { ...s.instruction, version, requirements: s.instruction.requirements.map((x) => (x.id === req.id ? req : x)) },
            changes: [...s.changes, { version, kind: 'requirement', id: req.id, at: now() }],
          };
        }),
      markInstructionSeen: () =>
        update((s) => (s.patientSeenVersion === s.instruction.version ? s : { ...s, patientSeenVersion: s.instruction.version })),
      markMessagesSeen: () =>
        update((s) => {
          const n = patientMessages(s).length;
          return s.patientSeenMessages === n ? s : { ...s, patientSeenMessages: n };
        }),
      sendReport: () => update((s) => (s.reportSent ? s : { ...s, reportSent: true, reportSentAt: now() })),
      sendMessage: (text) =>
        update((s) => ({
          ...s,
          messages: [...s.messages, { id: 'm' + Date.now(), kind: 'text', text, day: 'Today', at: now() }],
        })),
      reset: () => {
        update(() => initialState());
        resetSplash(); // replay the first-launch screen too
      },
    };
  }, [state, update]);

  return <Ctx.Provider value={store}>{children}</Ctx.Provider>;
}

export function useDemo(): Store {
  const s = useContext(Ctx);
  if (!s) throw new Error('useDemo must be used inside DemoProvider');
  return s;
}

export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
}
