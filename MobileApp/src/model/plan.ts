import type { ActionErrorCriterion, ActionProcedure, ActionRequirement, ProcedureAction } from './types';

// Read helpers and the edit rules the headset enforces (ActionProcedureJson.Validate).
// The server runs the full validation on save; the phone only keeps the nurse from breaking the rules below.

export function allActions(plan: ActionProcedure): ProcedureAction[] {
  return plan.steps.flatMap((s) => s.actions);
}

export function findAction(plan: ActionProcedure, actionId: string) {
  for (const [stepIndex, step] of plan.steps.entries()) {
    const action = step.actions.find((a) => a.action_id === actionId);
    if (action) return { step, stepIndex, action };
  }
  return null;
}

export function actionNeedsReview(a: ProcedureAction): boolean {
  return [...a.preconditions, ...a.postconditions, ...a.error_criteria].some((x) => x.requires_expert_review);
}

export function nurseCallAction(plan: ActionProcedure): ProcedureAction | undefined {
  return allActions(plan).find((a) => a.notify_nurse);
}

export function sentenceCase(text: string): string {
  const t = text.trim();
  return t ? t[0].toUpperCase() + t.slice(1) : t;
}

/** Text that counts as the same claim (mirrors ActionProcedureJson.ClaimKey closely enough for a warning). */
export function claimKey(text: string): string {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
}

function usedIds(plan: ActionProcedure): Set<string> {
  const ids = new Set<string>();
  for (const a of allActions(plan)) {
    for (const q of [...a.preconditions, ...a.postconditions]) ids.add(q.requirement_id);
    for (const e of a.error_criteria) ids.add(e.criterion_id);
  }
  for (const e of plan.procedure_error_criteria) ids.add(e.criterion_id);
  return ids;
}

export function newId(plan: ActionProcedure, prefix: string, taken: Set<string> = new Set()): string {
  const ids = usedIds(plan);
  for (let n = 1; ; n++) {
    const id = `${prefix}${n}`;
    if (!ids.has(id) && !taken.has(id)) return id;
  }
}

/** A requirement after the nurse saved it: edited text becomes an author edit, and the review flag is cleared. */
export function reviewedRequirement(q: ActionRequirement, text: string): ActionRequirement {
  const edited = text !== q.condition;
  const authored = edited || q.source === 'inferred';
  return {
    ...q,
    condition: text,
    source: authored ? 'author_edit' : q.source,
    source_reference: authored ? text : q.source_reference,
    requires_expert_review: false,
  };
}

export function reviewedCriterion(e: ActionErrorCriterion, text: string): ActionErrorCriterion {
  const edited = text !== e.compliance_condition;
  const authored = edited || e.source === 'inferred';
  return {
    ...e,
    compliance_condition: text,
    // The server's AI rewrites error_type and violation_condition for edited items.
    violation_condition: edited ? '' : e.violation_condition,
    error_type: edited ? '' : e.error_type,
    source: authored ? 'author_edit' : e.source,
    source_reference: authored ? text : e.source_reference,
    requires_expert_review: false,
  };
}

export function newRequirement(id: string, text: string): ActionRequirement {
  return { requirement_id: id, condition: text, source: 'author_edit', source_reference: text, requires_expert_review: false };
}

export function newCriterion(id: string, text: string, global = false): ActionErrorCriterion {
  return {
    criterion_id: id,
    error_type: '',
    violation_condition: '',
    compliance_condition: text,
    applicability_condition: '',
    related_requirement_ids: [],
    ...(global ? { relevant_action_ids: [] } : {}),
    source: 'author_edit',
    source_reference: text,
    requires_expert_review: false,
  };
}

/** Problems that would make the headset reject the plan. Empty when the plan can be saved. */
export function planProblems(plan: ActionProcedure): string[] {
  const problems: string[] = [];
  for (const [i, step] of plan.steps.entries()) {
    for (const a of step.actions) {
      const where = `Step ${i + 1}: “${a.instruction}”`;
      if (!a.instruction.trim()) problems.push(`Step ${i + 1}: an action has no “What to do”.`);
      if (!a.manual_completion_required && a.postconditions.length === 0)
        problems.push(`${where} needs a “Done when” check, or turn on “Caregiver confirms”.`);
      if (a.manual_completion_required && a.postconditions.length > 0)
        problems.push(`${where} can’t have “Done when” checks while “Caregiver confirms” is on.`);
      for (const q of [...a.preconditions, ...a.postconditions]) if (!q.condition.trim()) problems.push(`${where} has an empty check.`);
      for (const e of a.error_criteria) if (!e.compliance_condition.trim()) problems.push(`${where} has an empty watch-out.`);
    }
  }
  const local = new Set(
    allActions(plan).flatMap((a) => [
      ...a.preconditions.map((q) => claimKey(q.condition)),
      ...a.postconditions.map((q) => claimKey(q.condition)),
      ...a.error_criteria.map((e) => claimKey(e.compliance_condition)),
    ]),
  );
  for (const rule of plan.procedure_error_criteria) {
    if (!rule.compliance_condition.trim()) problems.push('A safety rule is empty.');
    else if (local.has(claimKey(rule.compliance_condition)))
      problems.push(`The safety rule “${rule.compliance_condition}” repeats a check inside an action. Keep only one.`);
  }
  return problems;
}

/** Step ids whose content differs between two plans (drives "Updated" on the patient app). */
export function changedStepIds(before: ActionProcedure, after: ActionProcedure): string[] {
  const old = new Map(before.steps.map((s) => [s.step_id, JSON.stringify(s)]));
  return after.steps.filter((s) => old.get(s.step_id) !== JSON.stringify(s)).map((s) => s.step_id);
}

export function itemIdFor(displayName: string): string {
  return displayName.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
}
