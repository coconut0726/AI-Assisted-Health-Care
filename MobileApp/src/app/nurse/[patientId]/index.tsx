import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { View } from 'react-native';
import { PATIENT } from '@/data/demo';
import { reviewBadge, SafetyRules, StepList, SuppliesCard } from '@/features/CarePlan';
import { ComingSoon, PlanStatus, stepNames } from '@/features/status';
import { actionNeedsReview } from '@/model/plan';
import { usePlan } from '@/store/PlanStore';
import { Banner, Body, Nav, Screen, Segmented, Tabs } from '@/ui/components';

// N2 Care Steps, N3 Safety Rules, and N5 (N2 with the "saved" banner, after Save & sync).
export default function NurseInstruction() {
  const { patientId, saved } = useLocalSearchParams<{ patientId: string; saved?: string }>();
  const { current } = usePlan();
  const [tab, setTab] = useState<'instruction' | 'feedback'>('instruction');
  const [view, setView] = useState<'steps' | 'rules'>('steps');

  if (!current) return <Screen><PlanStatus /></Screen>;
  const { plan } = current;
  const changed = stepNames(plan, current.changed_step_ids);
  const firstOpen = saved
    ? current.changed_step_ids.slice(0, 1)
    : plan.steps.filter((s) => s.actions.some(actionNeedsReview)).slice(0, 1).map((s) => s.step_id);

  return (
    <Screen>
      <Nav title={PATIENT.name} sub={`${plan.procedure_title} · ${PATIENT.frequency}`} back />
      <Tabs
        items={[
          { label: 'Instruction', active: tab === 'instruction', onPress: () => setTab('instruction') },
          { label: 'Home Care Feedback', active: tab === 'feedback', onPress: () => setTab('feedback') },
        ]}
      />
      {tab === 'feedback' ? (
        <ComingSoon what="Home care feedback" />
      ) : (
        <Body>
          <Segmented
            items={[
              { label: 'Care Steps', active: view === 'steps', onPress: () => setView('steps') },
              { label: 'Safety Rules', active: view === 'rules', onPress: () => setView('rules') },
            ]}
          />
          {view === 'steps' ? (
            <View style={{ gap: 10 }}>
              {saved && (
                <Banner tone="green">
                  {`✓ Saved and synced to ${PATIENT.firstName}’s app and headset.`}
                  {changed ? ` ${PATIENT.firstName} will see “${changed} updated”.` : ''}
                </Banner>
              )}
              {!saved && (
                <SuppliesCard plan={plan} onEdit={() => router.push({ pathname: '/nurse/[patientId]/supplies', params: { patientId } })} />
              )}
              <StepList
                key={`${current.version}-${saved ?? ''}`}
                plan={plan}
                badgeFor={reviewBadge}
                initiallyOpen={firstOpen}
                onActionPress={(a) =>
                  router.push({ pathname: '/nurse/[patientId]/action/[actionId]', params: { patientId, actionId: a.action_id } })
                }
              />
            </View>
          ) : (
            <SafetyRules plan={plan} caption="Checked the whole time, in every home session" />
          )}
        </Body>
      )}
    </Screen>
  );
}
