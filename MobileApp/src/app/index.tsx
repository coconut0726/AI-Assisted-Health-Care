import { router } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { SERVER_URL } from '@/api/client';
import { NURSE, PATIENT } from '@/data/demo';
import { usePlan } from '@/store/PlanStore';
import { Banner, Body, Button, Card, Icon, Screen, T } from '@/ui/components';
import { confirm } from '@/ui/dialogs';
import { resetSplash } from '@/ui/Splash';
import { color, shadow } from '@/ui/theme';

// Role picker for the demo build. A real install would sign in as one role instead.
export default function Home() {
  const { mode, reset } = usePlan();

  const confirmReset = () =>
    confirm('Reset demo?', 'This puts the care plan back to the version the AI made.', 'Reset', () => {
      void reset();
      void resetSplash();
    });

  return (
    <Screen>
      <Body>
        <View style={styles.hero}>
          <View style={styles.glass}>
            <T size={12} w="medium" c="#fff">
              Mobile demo
            </T>
          </View>
          <T w="semibold" size={28} c="#fff">
            AI-Assisted Home Care
          </T>
          <T c="#fff" style={{ opacity: 0.92 }}>
            A nurse demonstrates a care procedure once. AI turns it into step-by-step guidance for home and reports back on how it is going.
          </T>
        </View>
        <T w="semibold" size={14} c={color.muted} style={{ marginTop: 4 }}>
          Open the app as
        </T>
        <RoleCard initials={NURSE.initials} title="Nurse" text="Review and correct care plans, read AI weekly reports, send feedback" onPress={() => router.push('/nurse')} />
        <RoleCard initials={PATIENT.initials} title="Caregiver / patient" text="Read the care plan, see what changed, receive feedback" onPress={() => router.push('/patient')} />
        <Banner>
          {mode === 'server'
            ? `Connected to the care-plan server at ${SERVER_URL}.`
            : 'No server set: the care plan is kept on this phone. Set EXPO_PUBLIC_SERVER_URL to use the PC server.'}
        </Banner>
        {mode === 'on-device' && <Button kind="outline" label="Reset demo" onPress={confirmReset} />}
      </Body>
    </Screen>
  );
}

function RoleCard({ initials, title, text, onPress }: { initials: string; title: string; text: string; onPress: () => void }) {
  return (
    <Card onPress={onPress} style={styles.role}>
      <View style={styles.avatar}>
        <T w="semibold" c="#fff">
          {initials}
        </T>
      </View>
      <View style={{ flex: 1, gap: 3 }}>
        <T w="semibold" size={16}>
          {title}
        </T>
        <T size={13} c={color.muted}>
          {text}
        </T>
      </View>
      <Icon name="chevron" />
    </Card>
  );
}

const styles = StyleSheet.create({
  hero: { backgroundColor: '#5f8d64', borderRadius: 28, padding: 22, gap: 10, ...shadow },
  glass: { backgroundColor: 'rgba(255,255,255,0.24)', borderRadius: 999, paddingHorizontal: 12, paddingVertical: 4, alignSelf: 'flex-start' },
  role: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  avatar: { width: 46, height: 46, borderRadius: 23, backgroundColor: color.avatar, alignItems: 'center', justifyContent: 'center' },
});
