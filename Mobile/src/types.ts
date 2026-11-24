// Mirrors the task → step → subgoal → action output of the Unity Decompose tool,
// flattened to one expected state per step and given stable ids so edits can be tracked.

export type Tactic =
  | 'Command'
  | 'StatusCheck'
  | 'QuantitativeSpecification'
  | 'ConditionalStatement'
  | 'Rationale';

export type GuidanceType = 'ContextualWidget' | 'Movement' | 'HandGesture';

export interface Action {
  id: string;
  voice: string;
  tactic: Tactic;
  guidanceType: GuidanceType;
  guidanceParam: string;
}

export interface Step {
  id: string;
  title: string;
  sourceSpan: string;
  expectedState: string;
  actions: Action[];
}

export type RequirementType = 'Temporal' | 'Continuous' | 'State';

export interface Requirement {
  id: string;
  type: RequirementType;
  text: string;
  scope: string;
}

export interface Instruction {
  procedureTitle: string;
  frequency: string;
  version: number;
  steps: Step[];
  requirements: Requirement[];
  redFlags: string;
}

export type Verdict = 'Satisfied' | 'Violated' | 'Unknown';

export interface Finding {
  id: string;
  verdict: Verdict;
  requirement: string;
  frequency: string;
  evidence: string[];
  nurseNote: string;
  patientNote: string;
}

export interface WeeklyReport {
  title: string;
  range: string;
  sessionsRecorded: string;
  counts: Record<Verdict, number>;
  findings: Finding[];
}

export interface Message {
  id: string;
  kind: 'text' | 'report';
  text?: string;
  day: string;
  at: string;
}

export interface Change {
  version: number;
  kind: 'step' | 'requirement';
  id: string;
  at: string;
}

export interface DemoState {
  instruction: Instruction;
  changes: Change[];
  patientSeenVersion: number;
  report: WeeklyReport;
  reportSent: boolean;
  reportSentAt?: string;
  messages: Message[];
  patientSeenMessages: number;
}
