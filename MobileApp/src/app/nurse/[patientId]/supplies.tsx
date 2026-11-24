import { useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, StyleSheet, View } from 'react-native';
import { PATIENT } from '@/data/demo';
import { PlanStatus } from '@/features/status';
import { itemIdFor } from '@/model/plan';
import type { ActionProcedure, SupplyItem } from '@/model/types';
import { usePlan } from '@/store/PlanStore';
import { ActionBar, Body, Button, goBack, Label, LinkText, Nav, Screen, T } from '@/ui/components';
import { notify } from '@/ui/dialogs';
import { Input } from '@/ui/fields';
import { color } from '@/ui/theme';

// N10 Edit Supplies. The nurse edits names only; the server derives itemId and keywords
// (the phone sends a placeholder so the JSON stays complete when there is no server).

type Row = { key: string; orig?: SupplyItem; name: string };
let seq = 0;

export default function EditSupplies() {
  const { patientId } = useLocalSearchParams<{ patientId: string }>();
  const { current } = usePlan();
  if (!current) return <Screen><PlanStatus /></Screen>;
  return <Editor key={current.version} patientId={patientId} plan={current.plan} />;
}

function Editor({ patientId, plan }: { patientId: string; plan: ActionProcedure }) {
  const { save } = usePlan();
  const [rows, setRows] = useState<Row[]>(() => (plan.supplies ?? []).map((s) => ({ key: `s${++seq}`, orig: s, name: s.displayName })));
  const [added, setAdded] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const onSave = async () => {
    const supplies: SupplyItem[] = rows
      .map((r) => ({ ...r, name: r.name.trim() }))
      .filter((r) => r.name)
      .map((r) =>
        r.orig && r.orig.displayName === r.name
          ? r.orig
          : { itemId: itemIdFor(r.name), displayName: r.name, keywords: [r.name.toLowerCase()] },
      );
    const ids = supplies.map((s) => s.itemId);
    if (new Set(ids).size !== ids.length) {
      notify('Can’t save yet', 'Two items have the same name.');
      return;
    }
    setSaving(true);
    try {
      await save({ ...plan, supplies });
      goBack({ pathname: '/nurse/[patientId]', params: { patientId } });
    } catch (e) {
      notify('Not saved', e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Screen>
      <Nav title="Edit Supplies" sub={`${PATIENT.name} · ${plan.procedure_title}`} back />
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Body>
          <View style={styles.why}>
            <Label>Why this matters</Label>
            <T size={14}>The headset looks for each item at the start of the session and reminds the patient if something is missing.</T>
          </View>
          <Label>Items</Label>
          {rows.map((r) => (
            <Input
              key={r.key}
              multiline={false}
              value={r.name}
              placeholder="Item name"
              autoFocus={r.key === added}
              onChangeText={(name) => setRows(rows.map((x) => (x.key === r.key ? { ...x, name } : x)))}
              onRemove={() => setRows(rows.filter((x) => x.key !== r.key))}
            />
          ))}
          <LinkText
            onPress={() => {
              const k = `s${++seq}`;
              setAdded(k);
              setRows([...rows, { key: k, name: '' }]);
            }}
          >
            + Add item
          </LinkText>
        </Body>
        <ActionBar>
          <Button kind="outline" label="Cancel" onPress={() => goBack({ pathname: '/nurse/[patientId]', params: { patientId } })} />
          <Button label={saving ? 'Saving…' : 'Save & sync'} onPress={onSave} disabled={saving} />
        </ActionBar>
      </KeyboardAvoidingView>
    </Screen>
  );
}

const styles = StyleSheet.create({
  why: { backgroundColor: color.seg, borderRadius: 16, paddingHorizontal: 12, paddingVertical: 10, gap: 4 },
});
