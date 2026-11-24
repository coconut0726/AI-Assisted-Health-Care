import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { actionNeedsReview, nurseCallAction, sentenceCase } from '@/model/plan';
import type { ActionProcedure, ActionStep, ProcedureAction } from '@/model/types';
import { Card, Icon, Label, LinkText, Pill, T } from '@/ui/components';
import { color } from '@/ui/theme';

// Shared by the nurse (N2, N3, N5) and patient (P1, P2) screens. Patient screens pass no edit handlers.

export const CONDITION_COLOR = { before: color.lav, done: color.green, watch: color.amber } as const;

export function SuppliesCard({ plan, onEdit }: { plan: ActionProcedure; onEdit?: () => void }) {
  const items = plan.supplies ?? [];
  return (
    <Card style={{ gap: 8 }}>
      <View style={styles.row}>
        <T w="semibold" style={{ flex: 1 }}>
          Supplies
        </T>
        {onEdit && <LinkText onPress={onEdit}>Edit</LinkText>}
      </View>
      <T size={13} c={color.muted} style={{ marginTop: -6 }}>
        Have these ready before you start
      </T>
      <View style={styles.chips}>
        {items.length === 0 ? (
          <T size={13} c={color.muted}>
            No supplies listed yet.
          </T>
        ) : (
          items.map((i) => (
            <View key={i.itemId} style={styles.chip}>
              <T size={13} c={color.primaryDark}>
                {i.displayName}
              </T>
            </View>
          ))
        )}
      </View>
    </Card>
  );
}

function ConditionRow({ label, c, text }: { label: string; c: string; text: string }) {
  return (
    <View style={styles.condRow}>
      <View style={{ width: 68 }}>
        <Label c={c}>{label}</Label>
      </View>
      <T size={13} c="#4a554c" style={{ flex: 1 }}>
        {text}
      </T>
    </View>
  );
}

export function ActionCard({ action, onPress }: { action: ProcedureAction; onPress?: () => void }) {
  const tags: { label: string; tone: 'lav' | 'neutral' }[] = [];
  if (action.applicability_condition.trim()) tags.push({ label: `Only if ${action.applicability_condition.trim()}`, tone: 'lav' });
  if (action.manual_completion_required) tags.push({ label: 'Caregiver confirms', tone: 'neutral' });
  const body = (
    <>
      <View style={styles.row}>
        <T size={14} style={{ flex: 1 }}>
          {action.instruction}
        </T>
        {onPress && <Icon name="chevron" size={16} />}
      </View>
      {tags.length > 0 && (
        <View style={styles.chips}>
          {tags.map((t) => (
            <Pill key={t.label} tone={t.tone}>
              {t.label}
            </Pill>
          ))}
        </View>
      )}
      {action.preconditions.map((q) => (
        <ConditionRow key={q.requirement_id} label="Before" c={CONDITION_COLOR.before} text={q.condition} />
      ))}
      {action.postconditions.map((q) => (
        <ConditionRow key={q.requirement_id} label="Done when" c={CONDITION_COLOR.done} text={q.condition} />
      ))}
      {action.error_criteria.map((e) => (
        <ConditionRow key={e.criterion_id} label="Watch out" c={CONDITION_COLOR.watch} text={e.compliance_condition} />
      ))}
    </>
  );
  if (!onPress) return <View style={styles.action}>{body}</View>;
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.action, pressed && { opacity: 0.8 }]}>
      {body}
    </Pressable>
  );
}

function StepCard({
  step,
  index,
  open,
  onToggle,
  badge,
  onActionPress,
}: {
  step: ActionStep;
  index: number;
  open: boolean;
  onToggle: () => void;
  badge?: { label: string; tone: 'amber' | 'lav' };
  onActionPress?: (a: ProcedureAction) => void;
}) {
  const count = step.actions.length;
  return (
    <Card style={{ gap: 10 }}>
      <Pressable accessibilityRole="button" accessibilityState={{ expanded: open }} onPress={onToggle} style={styles.stepHead}>
        <View style={styles.num}>
          <T w="semibold" size={13} c={color.primaryDark}>
            {index + 1}
          </T>
        </View>
        <T w="semibold" style={{ flex: 1 }} numberOfLines={2}>
          {step.step_name}
        </T>
        {badge ? <Pill tone={badge.tone}>{badge.label}</Pill> : !open && <T size={13} c={color.muted}>{`${count} action${count === 1 ? '' : 's'}`}</T>}
        <Icon name={open ? 'down' : 'chevron'} size={18} />
      </Pressable>
      {open && step.actions.map((a) => <ActionCard key={a.action_id} action={a} onPress={onActionPress ? () => onActionPress(a) : undefined} />)}
    </Card>
  );
}

export function StepList({
  plan,
  badgeFor,
  initiallyOpen,
  onActionPress,
}: {
  plan: ActionProcedure;
  badgeFor: (step: ActionStep) => { label: string; tone: 'amber' | 'lav' } | undefined;
  initiallyOpen?: string[];
  onActionPress?: (a: ProcedureAction) => void;
}) {
  const [open, setOpen] = useState<Set<string>>(() => new Set(initiallyOpen ?? []));
  const toggle = (id: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  return (
    <>
      {plan.steps.map((step, i) => (
        <StepCard
          key={step.step_id}
          step={step}
          index={i}
          open={open.has(step.step_id)}
          onToggle={() => toggle(step.step_id)}
          badge={badgeFor(step)}
          onActionPress={onActionPress}
        />
      ))}
    </>
  );
}

export function reviewBadge(step: ActionStep) {
  return step.actions.some(actionNeedsReview) ? ({ label: 'Please review', tone: 'amber' } as const) : undefined;
}

export function SafetyRules({ plan, caption, onEdit }: { plan: ActionProcedure; caption: string; onEdit?: (criterionId: string) => void }) {
  const call = nurseCallAction(plan);
  return (
    <>
      <T size={13} c={color.muted}>
        {caption}
      </T>
      {plan.procedure_error_criteria.length === 0 && (
        <Card>
          <T size={14} c={color.muted}>
            No rules for the whole session yet.
          </T>
        </Card>
      )}
      {plan.procedure_error_criteria.map((rule) => (
        <Card key={rule.criterion_id} style={styles.ruleCard}>
          <T size={14} style={{ flex: 1 }}>
            {rule.compliance_condition}
          </T>
          {onEdit && <LinkText onPress={() => onEdit(rule.criterion_id)}>Edit</LinkText>}
        </Card>
      ))}
      {call && call.applicability_condition.trim() !== '' && (
        <View style={styles.callCard}>
          <T w="semibold" size={13} c={color.red}>
            Call the nurse if
          </T>
          <T size={14} c={color.red}>
            {sentenceCase(call.applicability_condition)}.
          </T>
        </View>
      )}
    </>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  chip: { backgroundColor: color.primaryBg, borderRadius: 999, paddingHorizontal: 12, paddingVertical: 5 },
  stepHead: { flexDirection: 'row', alignItems: 'center', gap: 10, minHeight: 28 },
  num: { width: 26, height: 26, borderRadius: 13, backgroundColor: color.primaryBg, alignItems: 'center', justifyContent: 'center' },
  action: { backgroundColor: color.bg, borderRadius: 16, paddingHorizontal: 12, paddingVertical: 10, gap: 6 },
  condRow: { flexDirection: 'row', gap: 6 },
  ruleCard: { flexDirection: 'row', alignItems: 'center', gap: 12, borderRadius: 20 },
  callCard: { backgroundColor: color.redBg, borderRadius: 20, padding: 14, gap: 4 },
});
