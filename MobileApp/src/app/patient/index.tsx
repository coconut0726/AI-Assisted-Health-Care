import { useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { NURSE } from '@/data/demo';
import { SafetyRules, StepList, SuppliesCard } from '@/features/CarePlan';
import { ComingSoon, PlanStatus, stepNames } from '@/features/status';
import { usePlan } from '@/store/PlanStore';
import { Banner, Body, Icon, Nav, Screen, Segmented, T } from '@/ui/components';
import { color, shadowUp } from '@/ui/theme';

// P1 Care Steps and P2 Safety Rules (read-only), with the bottom tab bar.
export default function PatientInstruction() {
  const { current, reload, mode } = usePlan();
  const [tab, setTab] = useState<'instruction' | 'feedback'>('instruction');
  const [view, setView] = useState<'steps' | 'rules'>('steps');

  // Pick up the nurse's latest version whenever this screen comes back into view.
  useFocusEffect(
    useCallback(() => {
      if (mode === 'server') void reload();
    }, [mode, reload]),
  );

  if (!current) return <Screen><PlanStatus /></Screen>;
  const { plan } = current;
  const changed = current.updated_by === 'nurse' ? current.changed_step_ids : [];
  const when = new Date(current.updated_at);
  const time = when.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const day = when.toDateString() === new Date().toDateString() ? 'today' : `on ${when.toLocaleDateString()}`;

  return (
    <Screen>
      <Nav title="My Care Plan" sub={`${plan.procedure_title} · from ${NURSE.name}`} large />
      {tab === 'feedback' ? (
        <ComingSoon what="Feedback from your nurse" />
      ) : (
        <Body>
          <Segmented
            items={[
              { label: 'Care Steps', active: view === 'steps', onPress: () => setView('steps') },
              { label: 'Safety Rules', active: view === 'rules', onPress: () => setView('rules') },
            ]}
          />
          {view === 'steps' ? (
            <>
              {changed.length > 0 && (
                <Banner tone="lav">{`${NURSE.name} updated your instructions ${day} at ${time} — ${stepNames(plan, changed)} changed.`}</Banner>
              )}
              <SuppliesCard plan={plan} />
              <StepList
                key={current.version}
                plan={plan}
                initiallyOpen={changed.slice(0, 1)}
                badgeFor={(s) => (changed.includes(s.step_id) ? { label: 'Updated', tone: 'lav' } : undefined)}
              />
            </>
          ) : (
            <SafetyRules plan={plan} caption="Keep these right the whole time" />
          )}
        </Body>
      )}
      <BottomTabs active={tab} onChange={setTab} />
    </Screen>
  );
}

function BottomTabs({ active, onChange }: { active: 'instruction' | 'feedback'; onChange: (t: 'instruction' | 'feedback') => void }) {
  const insets = useSafeAreaInsets();
  const items = [
    { id: 'instruction' as const, label: 'Instruction', icon: 'list' as const },
    { id: 'feedback' as const, label: 'Feedback', icon: 'chat' as const },
  ];
  return (
    <View style={[styles.bar, { paddingBottom: Math.max(18, insets.bottom) }]}>
      {items.map((t) => {
        const on = t.id === active;
        return (
          <Pressable key={t.id} accessibilityRole="tab" accessibilityState={{ selected: on }} onPress={() => onChange(t.id)} style={styles.btab}>
            <Icon name={t.icon} size={22} c={on ? color.primary : color.muted} />
            <T size={12} w={on ? 'semibold' : 'medium'} c={on ? color.primary : color.muted}>
              {t.label}
            </T>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', paddingTop: 8, backgroundColor: color.surface, borderTopLeftRadius: 24, borderTopRightRadius: 24, ...shadowUp },
  btab: { flex: 1, alignItems: 'center', gap: 2 },
});
