import type { ActionProcedure, PlanVersion } from '@/model/types';
import burn from './burn_action_requirements.json';

// Demo people (same as the Figma "Mobile" page and the old Mobile/ web demo).
export const NURSE = { name: 'Nurse Lee', unit: 'Burn Unit', initials: 'NL' };

export const PATIENT = {
  id: 'james',
  name: 'James Carter',
  firstName: 'James',
  initials: 'JC',
  frequency: 'once a day',
  discharged: 'Discharged Sep 28 · Week 2 at home',
};

export const OTHER_PATIENTS = [
  { initials: 'AR', name: 'Aisha Rahman', procedure: 'Subcutaneous Injection', meta: 'Discharged Oct 6 · Week 1 at home' },
  { initials: 'TB', name: 'Tom Becker', procedure: 'Surgical Drain Care', meta: 'Recorded today · not yet discharged', flag: 'Instruction needs review' },
];

/**
 * The burn case as the headset AI produced it (burn_action_requirements.json, copied from the branch
 * feature/periodical-sampling-with-localVLM), plus the demo content shown in Figma:
 * supplies, the "Call the nurse" flag, a check and a flagged watch-out on A4, and three safety rules.
 */
function demoPlan(): ActionProcedure {
  const plan = JSON.parse(JSON.stringify(burn)) as ActionProcedure;
  plan.procedure_title = 'Burn Dressing Change';
  plan.supplies = ['Mild soap', 'Burn cream', 'Q-tips', 'Non-stick pad', 'Gauze roll', 'Tape'].map((displayName) => ({
    itemId: displayName.toLowerCase().replace(/[^a-z0-9]+/g, '_'),
    displayName,
    keywords: [displayName.toLowerCase()],
  }));

  const last = plan.steps[plan.steps.length - 1];
  last.step_name = 'If the Burn Gets Worse';
  last.actions[0].notify_nurse = true;

  const a4 = plan.steps[1].actions[1];
  a4.preconditions = [
    { requirement_id: 'A4_PRE1', condition: 'Gloves are on.', source: 'graph_dependency', source_reference: 'A2', requires_expert_review: false },
  ];
  a4.error_criteria = [
    {
      criterion_id: 'A4_E1',
      error_type: 'technique_error',
      violation_condition: 'The dressing is being ripped off quickly.',
      compliance_condition: 'Peel it off slowly — don’t rip it.',
      applicability_condition: '',
      related_requirement_ids: [],
      source: 'inferred',
      source_reference: 'Gently remove the old dressing.',
      requires_expert_review: true,
    },
  ];

  plan.procedure_error_criteria = [
    {
      criterion_id: 'G1',
      error_type: 'wrong_object',
      violation_condition: 'The pad or cotton swab touches a surface that is not clean.',
      compliance_condition: 'Clean pad and Q-tip never touch non-clean surfaces.',
      applicability_condition: '',
      related_requirement_ids: [],
      relevant_action_ids: ['A7', 'A8'],
      source: 'expert_prior',
      source_reference: 'Burn unit dressing protocol',
      requires_expert_review: false,
    },
    {
      criterion_id: 'G2',
      error_type: 'technique_error',
      violation_condition: 'A blister is broken.',
      compliance_condition: 'Blisters stay intact — none are popped.',
      applicability_condition: '',
      related_requirement_ids: [],
      relevant_action_ids: ['A5', 'A6', 'A7'],
      source: 'explicit_instruction',
      source_reference: 'do not break any blisters',
      requires_expert_review: false,
    },
    {
      criterion_id: 'G3',
      error_type: 'technique_error',
      violation_condition: 'Bare hands touch the burn or the new dressing.',
      compliance_condition: 'Gloves stay on from removing the old dressing until the gauze is taped.',
      applicability_condition: '',
      related_requirement_ids: [],
      relevant_action_ids: ['A4', 'A5', 'A6', 'A7', 'A8', 'A9', 'A10'],
      source: 'expert_prior',
      source_reference: 'Burn unit dressing protocol',
      requires_expert_review: false,
    },
  ];
  return plan;
}

export function demoVersion(): PlanVersion {
  return {
    patient_id: PATIENT.id,
    version: 1,
    updated_at: new Date().toISOString(),
    updated_by: 'ai',
    plan: demoPlan(),
    changed_step_ids: [],
  };
}
