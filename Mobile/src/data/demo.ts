import type { DemoState, GuidanceType, Instruction, RequirementType, Tactic, Verdict, WeeklyReport } from '../types';

export const PATIENT = {
  id: 'james',
  name: 'James Carter',
  firstName: 'James',
  initials: 'JC',
  discharged: 'Discharged Sep 28 · Week 2 at home',
};

export const NURSE = { name: 'Nurse Lee', unit: 'Burn Unit' };

export const OTHER_PATIENTS = [
  { initials: 'AR', name: 'Aisha Rahman', procedure: 'Subcutaneous Injection', meta: 'Discharged Oct 6 · Week 1 at home' },
  { initials: 'TB', name: 'Tom Becker', procedure: 'Surgical Drain Care', meta: 'Recorded today · not yet discharged', flag: 'Instruction needs review' },
];

export const TACTIC_LABEL: Record<Tactic, string> = {
  Command: 'Command',
  StatusCheck: 'Status check',
  QuantitativeSpecification: 'Quantitative',
  ConditionalStatement: 'Conditional',
  Rationale: 'Rationale',
};

export const GUIDANCE_LABEL: Record<GuidanceType, string> = {
  ContextualWidget: 'Contextual widget',
  Movement: 'Movement',
  HandGesture: 'Hand gesture',
};

export const REQUIREMENT_TYPES: RequirementType[] = ['Temporal', 'Continuous', 'State'];

// The decomposition as the AI produced it. Step 2's first action is the AI error the nurse fixes in the demo:
// it drops the condition ("if the old bandage is stuck").
const INSTRUCTION: Instruction = {
  procedureTitle: 'Burn Dressing Change',
  frequency: 'once a day',
  version: 1,
  steps: [
    {
      id: 's1',
      title: 'Preparation & Hand Hygiene',
      sourceSpan: 'So once a day you’re gonna change this, wash your hands first,',
      expectedState: 'Hands are clean before handling supplies.',
      actions: [
        { id: 'a1', voice: 'Change this dressing once a day.', tactic: 'QuantitativeSpecification', guidanceType: 'ContextualWidget', guidanceParam: 'Frequency: once per day' },
        { id: 'a2', voice: 'Wash your hands thoroughly first.', tactic: 'Command', guidanceType: 'Movement', guidanceParam: 'wash' },
      ],
    },
    {
      id: 's2',
      title: 'Old Bandage Removal',
      sourceSpan: 'and if the old bandage is stuck, just wet it with some water so it comes off easy, don’t rip it.',
      expectedState: 'Old bandage is off without tearing the skin.',
      actions: [
        { id: 'a3', voice: 'Wet the bandage with water.', tactic: 'Command', guidanceType: 'Movement', guidanceParam: 'wet' },
        { id: 'a4', voice: 'Don’t rip it off.', tactic: 'Command', guidanceType: 'ContextualWidget', guidanceParam: 'warning' },
      ],
    },
    {
      id: 's3',
      title: 'Clean the Burn',
      sourceSpan: 'Then wash the burn real gently with mild soap and water and pat it dry, no rubbing.',
      expectedState: 'Burn is clean and patted dry.',
      actions: [
        { id: 'a5', voice: 'Wash gently with mild soap and water.', tactic: 'Command', guidanceType: 'Movement', guidanceParam: 'wash' },
        { id: 'a6', voice: 'Pat dry — no rubbing.', tactic: 'Command', guidanceType: 'HandGesture', guidanceParam: 'pat' },
      ],
    },
    {
      id: 's4',
      title: 'Apply Cream',
      sourceSpan: 'Put a thin layer of the cream on with a clean Q-tip,',
      expectedState: 'A thin layer of cream covers the burn.',
      actions: [
        { id: 'a7', voice: 'Apply a thin layer with a clean Q-tip.', tactic: 'QuantitativeSpecification', guidanceType: 'Movement', guidanceParam: 'spread' },
      ],
    },
    {
      id: 's5',
      title: 'Cover & Secure',
      sourceSpan: 'then the non-stick pad goes right on the burn, and wrap the gauze roll around it and tape it.',
      expectedState: 'Pad covers burn; gauze wrapped and taped.',
      actions: [
        { id: 'a8', voice: 'Place the non-stick pad on the burn.', tactic: 'Command', guidanceType: 'Movement', guidanceParam: 'place' },
        { id: 'a9', voice: 'Wrap the gauze roll around it and tape it.', tactic: 'Command', guidanceType: 'Movement', guidanceParam: 'wrap' },
      ],
    },
  ],
  requirements: [
    { id: 'r1', type: 'Temporal', text: 'Hand hygiene is completed before touching clean supplies.', scope: 'Step 1 → Steps 2–5' },
    { id: 'r2', type: 'Temporal', text: 'Burn is patted dry before cream is applied.', scope: 'Step 3 → Step 4' },
    { id: 'r3', type: 'Continuous', text: 'No rubbing while cleaning or drying the burn.', scope: 'During Step 3' },
    { id: 'r4', type: 'Continuous', text: 'Clean pad and Q-tip never touch non-clean surfaces.', scope: 'During Steps 4–5' },
    { id: 'r5', type: 'State', text: 'Cream layer is thin and covers the whole burn.', scope: 'End of Step 4' },
    { id: 'r6', type: 'State', text: 'Blisters stay intact — none are popped.', scope: 'Whole procedure' },
  ],
  redFlags: 'The burn gets redder, oozes pus, smells bad, or you get a fever.',
};

const REPORT: WeeklyReport = {
  title: 'Week 2',
  range: 'Oct 5 – 11',
  sessionsRecorded: '7 of 7',
  counts: { Satisfied: 4, Violated: 2, Unknown: 1 } as Record<Verdict, number>,
  findings: [
    {
      id: 'f1',
      verdict: 'Violated',
      requirement: 'No rubbing while cleaning or drying the burn.',
      frequency: '4 of 7 sessions · Step 3',
      evidence: ['Oct 8 · 9:14', 'Oct 10 · 9:02'],
      nurseNote: 'Towel moved back and forth across the burn instead of patting.',
      patientNote: 'Pat the burn dry with light presses. Do not wipe back and forth.',
    },
    {
      id: 'f2',
      verdict: 'Violated',
      requirement: 'Hand hygiene is completed before touching clean supplies.',
      frequency: '2 of 7 sessions · Step 1',
      evidence: ['Oct 6 · 9:20', 'Oct 9 · 8:57'],
      nurseNote: 'Gauze roll was picked up before hand washing was observed.',
      patientNote: 'Wash your hands first, before you pick up the gauze or pad.',
    },
    {
      id: 'f3',
      verdict: 'Unknown',
      requirement: 'Cream layer is thin and covers the whole burn.',
      frequency: '5 of 7 sessions · Step 4',
      evidence: [],
      nurseNote: 'Not enough evidence — the hand blocked the camera view of the burn.',
      patientNote: 'We could not see the burn clearly. Try to keep it in view while applying cream.',
    },
  ],
};

function todayAt(h: number, m: number): string {
  const d = new Date();
  d.setHours(h, m, 0, 0);
  return d.toISOString();
}

export function initialState(): DemoState {
  return {
    instruction: structuredClone(INSTRUCTION),
    changes: [],
    patientSeenVersion: 1,
    report: structuredClone(REPORT),
    reportSent: false,
    messages: [
      { id: 'm1', kind: 'text', text: 'Hi James, remember to keep the dressing dry between changes.', day: 'Mon, Oct 5', at: '2026-10-05T09:05:00' },
      { id: 'm2', kind: 'report', day: 'Today', at: todayAt(8, 0) },
    ],
    patientSeenMessages: 1,
  };
}
