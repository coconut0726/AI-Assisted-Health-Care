import { router, useLocalSearchParams } from 'expo-router';
import { useMemo, useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, View } from 'react-native';
import { CONDITION_COLOR } from '@/features/CarePlan';
import { PlanStatus } from '@/features/status';
import {
  findAction,
  newCriterion,
  newId,
  newRequirement,
  planProblems,
  reviewedCriterion,
  reviewedRequirement,
} from '@/model/plan';
import type { ActionErrorCriterion, ActionProcedure, ActionRequirement, ProcedureAction } from '@/model/types';
import { usePlan } from '@/store/PlanStore';
import { ActionBar, Banner, Body, Button, goBack, Label, LinkText, Nav, Screen, T } from '@/ui/components';
import { confirm, notify } from '@/ui/dialogs';
import { Input, Note, Switch } from '@/ui/fields';
import { color } from '@/ui/theme';

// N4 Edit Action (switch off) and N4b (switch on: "Caregiver confirms", no Done-when checks).

type Item<O> = { key: string; orig?: O; text: string };

let keySeq = 0;
const key = () => `k${++keySeq}`;

export default function EditAction() {
  const { patientId, actionId } = useLocalSearchParams<{ patientId: string; actionId: string }>();
  const { current } = usePlan();
  const found = current ? findAction(current.plan, actionId) : null;
  if (!current) return <Screen><PlanStatus /></Screen>;
  if (!found)
    return (
      <Screen>
        <Nav title="Edit Action" back />
        <Body>
          <Banner tone="red">This action is no longer in the care plan.</Banner>
        </Body>
      </Screen>
    );
  return <Editor key={`${current.version}-${actionId}`} patientId={patientId} plan={current.plan} stepIndex={found.stepIndex} action={found.action} />;
}

function Editor({ patientId, plan, stepIndex, action }: { patientId: string; plan: ActionProcedure; stepIndex: number; action: ProcedureAction }) {
  const { save } = usePlan();
  const step = plan.steps[stepIndex];
  const [instruction, setInstruction] = useState(action.instruction);
  const [pre, setPre] = useState<Item<ActionRequirement>[]>(() => action.preconditions.map((q) => ({ key: key(), orig: q, text: q.condition })));
  const [post, setPost] = useState<Item<ActionRequirement>[]>(() => action.postconditions.map((q) => ({ key: key(), orig: q, text: q.condition })));
  const [err, setErr] = useState<Item<ActionErrorCriterion>[]>(() => action.error_criteria.map((e) => ({ key: key(), orig: e, text: e.compliance_condition })));
  const [manual, setManual] = useState(action.manual_completion_required);
  const [onlyIf, setOnlyIf] = useState<string | null>(action.applicability_condition.trim() ? action.applicability_condition : null);
  const [saving, setSaving] = useState(false);

  const quote = useMemo(() => action.original_text.trim(), [action.original_text]);

  const setManualChecked = (on: boolean) => {
    if (!on) {
      setManual(false);
      return;
    }
    if (post.some((p) => p.text.trim())) {
      confirm(
        'Remove the Done-when checks?',
        'With “Caregiver confirms” on, the caregiver confirms this action in the headset instead of a photo check.',
        'Remove',
        () => {
          setPost([]);
          setManual(true);
        },
      );
    } else {
      setPost([]);
      setManual(true);
    }
  };

  const removePost = (k: string) => {
    const next = post.filter((p) => p.key !== k);
    setPost(next);
    if (next.length === 0) setManual(true); // an action without Done-when checks must be confirmed by the caregiver
  };

  const build = (): ActionProcedure => {
    const draft: ActionProcedure = JSON.parse(JSON.stringify(plan));
    const taken = new Set<string>();
    const id = (prefix: string) => {
      const v = newId(draft, prefix, taken);
      taken.add(v);
      return v;
    };
    const reqs = (items: Item<ActionRequirement>[], prefix: string) =>
      items
        .map((i) => ({ ...i, text: i.text.trim() }))
        .filter((i) => i.text)
        .map((i) => (i.orig ? reviewedRequirement(i.orig, i.text) : newRequirement(id(prefix), i.text)));
    const next: ProcedureAction = {
      ...action,
      instruction: instruction.trim(),
      applicability_condition: (onlyIf ?? '').trim(),
      manual_completion_required: manual,
      preconditions: reqs(pre, `${action.action_id}_PRE`),
      postconditions: manual ? [] : reqs(post, `${action.action_id}_P`),
      error_criteria: err
        .map((i) => ({ ...i, text: i.text.trim() }))
        .filter((i) => i.text)
        .map((i) => (i.orig ? reviewedCriterion(i.orig, i.text) : newCriterion(id(`${action.action_id}_E`), i.text))),
    };
    const s = draft.steps[stepIndex];
    s.actions = s.actions.map((a) => (a.action_id === action.action_id ? next : a));
    return draft;
  };

  const onSave = async () => {
    const draft = build();
    const problems = planProblems(draft);
    if (problems.length) {
      notify('Can’t save yet', problems.join('\n\n'));
      return;
    }
    setSaving(true);
    try {
      await save(draft);
      router.dismissTo({ pathname: '/nurse/[patientId]', params: { patientId, saved: '1' } });
    } catch (e) {
      notify('Not saved', e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Screen>
      <Nav title="Edit Action" sub={`Step ${stepIndex + 1} · ${step.step_name}`} back />
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Body>
          {quote !== '' && (
            <View style={styles.source}>
              <Label>What you said in the recording</Label>
              <T size={14}>“{quote}”</T>
            </View>
          )}

          <View style={styles.field}>
            <Label>What to do</Label>
            <Input value={instruction} onChangeText={setInstruction} placeholder="What the caregiver does" />
          </View>

          <ItemList
            label="Before starting"
            c={CONDITION_COLOR.before}
            items={pre}
            setItems={setPre}
            addLabel="+ Add a check before"
            placeholder="Something that must be true first"
          />

          {manual ? (
            <View style={styles.field}>
              <Label c={CONDITION_COLOR.done}>Done when</Label>
              <Note tone="info">No photo check — the caregiver confirms it in the headset. Turn off “Caregiver confirms” below to add one.</Note>
            </View>
          ) : (
            <ItemList
              label="Done when"
              c={CONDITION_COLOR.done}
              items={post}
              setItems={setPost}
              onRemove={removePost}
              addLabel="+ Add a done-when check"
              placeholder="What a photo shows when it’s done"
            />
          )}

          <ItemList
            label="Watch out"
            c={CONDITION_COLOR.watch}
            items={err}
            setItems={setErr}
            addLabel="+ Add a watch-out"
            placeholder="What must not go wrong"
          />

          <View style={styles.toggle}>
            <View style={{ flex: 1, gap: 2 }}>
              <T w="medium" size={14}>
                Caregiver confirms this action
              </T>
              <T size={12} c={color.muted}>
                {manual
                  ? 'On: the caregiver confirms this action in the headset instead of a photo check.'
                  : 'Turn on when a photo can’t show it (e.g. washing hands). This removes the Done-when checks.'}
              </T>
            </View>
            <Switch value={manual} onChange={setManualChecked} label="Caregiver confirms this action" />
          </View>

          {onlyIf === null ? (
            <LinkText onPress={() => setOnlyIf('')}>+ Only do this if…</LinkText>
          ) : (
            <View style={styles.field}>
              <Label c={color.lav}>Only do this if…</Label>
              <Input value={onlyIf} onChangeText={setOnlyIf} placeholder="e.g. it sticks" autoFocus={!action.applicability_condition} onRemove={() => setOnlyIf(null)} />
            </View>
          )}
        </Body>
        <ActionBar>
          <Button kind="outline" label="Cancel" onPress={() => goBack({ pathname: '/nurse/[patientId]', params: { patientId } })} />
          <Button label={saving ? 'Saving…' : 'Save & sync'} onPress={onSave} disabled={saving} />
        </ActionBar>
      </KeyboardAvoidingView>
    </Screen>
  );
}

function ItemList<O extends { requires_expert_review: boolean }>({
  label,
  c,
  items,
  setItems,
  onRemove,
  addLabel,
  placeholder,
}: {
  label: string;
  c: string;
  items: Item<O>[];
  setItems: (next: Item<O>[]) => void;
  onRemove?: (key: string) => void;
  addLabel: string;
  placeholder: string;
}) {
  const [justAdded, setJustAdded] = useState<string | null>(null);
  return (
    <View style={styles.field}>
      <Label c={c}>{label}</Label>
      {items.map((item) => (
        <View key={item.key} style={{ gap: 6 }}>
          <Input
            value={item.text}
            placeholder={placeholder}
            autoFocus={item.key === justAdded}
            onChangeText={(text) => setItems(items.map((i) => (i.key === item.key ? { ...i, text } : i)))}
            onRemove={() => (onRemove ? onRemove(item.key) : setItems(items.filter((i) => i.key !== item.key)))}
          />
          {item.orig?.requires_expert_review && <Note>AI isn’t sure about this one — please check the wording.</Note>}
        </View>
      ))}
      <LinkText
        onPress={() => {
          const k = key();
          setJustAdded(k);
          setItems([...items, { key: k, text: '' }]);
        }}
      >
        {addLabel}
      </LinkText>
    </View>
  );
}

const styles = StyleSheet.create({
  source: { backgroundColor: color.seg, borderRadius: 16, paddingHorizontal: 12, paddingVertical: 10, gap: 4 },
  field: { gap: 6 },
  toggle: { flexDirection: 'row', alignItems: 'center', gap: 12, backgroundColor: color.surface, borderRadius: 16, paddingHorizontal: 14, paddingVertical: 10 },
});
