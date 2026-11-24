import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { NURSE, OTHER_PATIENTS, PATIENT } from '@/data/demo';
import { actionNeedsReview, allActions } from '@/model/plan';
import { usePlan } from '@/store/PlanStore';
import { Banner, Body, Card, Icon, Nav, Pill, Screen, T } from '@/ui/components';
import { color } from '@/ui/theme';

// N1 Patients
export default function Patients() {
  const { current } = usePlan();
  const needsReview = current ? allActions(current.plan).some(actionNeedsReview) : false;
  return (
    <Screen>
      <Nav title="My Patients" sub={`${NURSE.unit} · ${NURSE.name}`} large back />
      <Body>
        <PatientCard
          initials={PATIENT.initials}
          name={PATIENT.name}
          procedure={current?.plan.procedure_title ?? '…'}
          meta={PATIENT.discharged}
          flag={needsReview ? { label: 'Instruction needs review', tone: 'amber' } : undefined}
          onPress={() => router.push({ pathname: '/nurse/[patientId]', params: { patientId: PATIENT.id } })}
        />
        {OTHER_PATIENTS.map((p) => (
          <PatientCard
            key={p.name}
            initials={p.initials}
            name={p.name}
            procedure={p.procedure}
            meta={p.meta}
            flag={p.flag ? { label: p.flag, tone: 'amber' } : undefined}
            disabled
          />
        ))}
        <Banner>New instructions are recorded with the headset. They appear here after the AI decomposes them into steps.</Banner>
      </Body>
    </Screen>
  );
}

function PatientCard(props: {
  initials: string;
  name: string;
  procedure: string;
  meta: string;
  flag?: { label: string; tone: 'amber' | 'primary' };
  onPress?: () => void;
  disabled?: boolean;
}) {
  return (
    <Card onPress={props.onPress} style={[styles.card, props.disabled && { opacity: 0.6 }]}>
      <View style={styles.avatar}>
        <T w="semibold" c="#fff">
          {props.initials}
        </T>
      </View>
      <View style={{ flex: 1, gap: 2 }}>
        <T w="semibold" size={16}>
          {props.name}
        </T>
        <T size={14}>{props.procedure}</T>
        <T size={13} c={color.muted}>
          {props.meta}
        </T>
        {props.flag && (
          <View style={{ paddingTop: 4 }}>
            <Pill tone={props.flag.tone}>{props.flag.label}</Pill>
          </View>
        )}
      </View>
      <Icon name="chevron" />
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { flexDirection: 'row', alignItems: 'center', gap: 14, paddingVertical: 18 },
  avatar: { width: 46, height: 46, borderRadius: 23, backgroundColor: color.avatar, alignItems: 'center', justifyContent: 'center' },
});
