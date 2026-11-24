// action_requirements_v1, as defined by the headset (Assets/_Project/Runtime/Procedures/ActionProcedure.cs),
// plus the optional mobile fields decided in docs/SCHEMA_mobile.md (supplies, notify_nurse).
// Field names stay snake_case so the JSON round-trips to the headset unchanged.

export const SCHEMA_VERSION = 'action_requirements_v1';

export type RequirementSource = 'explicit_instruction' | 'graph_dependency' | 'expert_prior' | 'inferred' | 'author_edit';

export type ErrorType = 'wrong_object' | 'spatial_error' | 'technique_error' | 'measurement_error' | 'timing_parameter_error';

export interface ActionRequirement {
  requirement_id: string;
  condition: string;
  source: RequirementSource;
  source_reference: string;
  requires_expert_review: boolean;
}

export interface ActionErrorCriterion {
  criterion_id: string;
  /** Empty while the server's AI has not filled it in yet (nurse-added "Watch out" items). */
  error_type: ErrorType | '';
  violation_condition: string;
  compliance_condition: string;
  applicability_condition: string;
  related_requirement_ids: string[];
  /** Global rules (procedure_error_criteria) only. */
  relevant_action_ids?: string[];
  source: RequirementSource;
  source_reference: string;
  requires_expert_review: boolean;
}

export interface ConditionalActionDependency {
  action_id: string;
  condition: string;
}

export interface ProcedureAction {
  action_id: string;
  instruction: string;
  original_text: string;
  dependencies: string[];
  conditional_dependencies: ConditionalActionDependency[];
  applicability_condition: string;
  actor: string[];
  tools: string[];
  materials: string[];
  contact_media: string[];
  targets: string[];
  associated_constraints: string[];
  manual_completion_required: boolean;
  preconditions: ActionRequirement[];
  postconditions: ActionRequirement[];
  error_criteria: ActionErrorCriterion[];
  /** Mobile: this action is the "Call the nurse if" card on Safety Rules. Set by the server. */
  notify_nurse?: boolean;
}

export interface ActionStep {
  step_id: string;
  step_name: string;
  step_description: string;
  dependencies: string[];
  actions: ProcedureAction[];
}

export interface SupplyItem {
  itemId: string;
  displayName: string;
  keywords: string[];
}

export interface ActionProcedure {
  schema_version: string;
  procedure_title: string;
  task_goal: string;
  instruction: string;
  steps: ActionStep[];
  procedure_error_criteria: ActionErrorCriterion[];
  context_notes: string[];
  review_notes: string[];
  /** Mobile: items the headset looks for at the start of a session. */
  supplies?: SupplyItem[];
}

/** One stored version of a patient's care plan, as the server returns it. */
export interface PlanVersion {
  patient_id: string;
  version: number;
  updated_at: string;
  updated_by: 'ai' | 'nurse';
  plan: ActionProcedure;
  /** Step ids that changed compared with the previous version. */
  changed_step_ids: string[];
}
