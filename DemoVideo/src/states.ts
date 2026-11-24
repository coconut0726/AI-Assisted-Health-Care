import { initialState } from '../../Mobile/src/data/demo';
import type { DemoState } from '../../Mobile/src/types';

// Fixed app states for the phone shots (same demo data the website uses).
const at = (h: number, m: number) => {
  const d = new Date();
  d.setHours(h, m, 0, 0);
  return d.toISOString();
};

const FIXED = 'If the old bandage is stuck, wet it with water so it comes off easily.';

/** Fresh AI draft: Step 2's first action is missing its condition. */
export const draft: DemoState = initialState();

/** Edit screen after the nurse retyped the action (shown before saving). */
export const correcting: DemoState = (() => {
  const s = initialState();
  const step = s.instruction.steps[1];
  step.actions[0] = { ...step.actions[0], voice: FIXED, tactic: 'ConditionalStatement', guidanceType: 'ContextualWidget' };
  return s;
})();

/** Saved and synced: version 2, Step 2 marked as changed. */
export const saved: DemoState = (() => {
  const s = correcting;
  return {
    ...s,
    instruction: { ...s.instruction, version: 2 },
    changes: [{ version: 2, kind: 'step', id: 's2', at: at(10, 32) }],
  };
})();

/** Weekly report forwarded and a note written. */
export const reportSent: DemoState = {
  ...saved,
  reportSent: true,
  reportSentAt: at(10, 41),
  messages: [
    ...saved.messages,
    {
      id: 'm3',
      kind: 'text',
      text: 'Please pat the burn dry instead of rubbing, and wash your hands before you touch the gauze.',
      day: 'Today',
      at: at(10, 44),
    },
  ],
  patientSeenVersion: 2,
};
