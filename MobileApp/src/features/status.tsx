import { ActivityIndicator, View } from 'react-native';
import type { ActionProcedure } from '@/model/types';
import { usePlan } from '@/store/PlanStore';
import { Banner, Body, Button, T } from '@/ui/components';
import { color } from '@/ui/theme';

/** Shown instead of a screen's body while the plan loads or when the server can't be reached. */
export function PlanStatus() {
  const { loading, error, reload } = usePlan();
  if (loading)
    return (
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator color={color.primary} />
      </View>
    );
  return (
    <Body>
      <Banner tone="red">Couldn’t load the care plan. {error}</Banner>
      <Button kind="outline" label="Try again" onPress={() => void reload()} />
    </Body>
  );
}

/** "Step 2" / "Steps 2 and 4" for a list of step ids. */
export function stepNames(plan: ActionProcedure, stepIds: string[]): string {
  const nums = plan.steps.flatMap((s, i) => (stepIds.includes(s.step_id) ? [i + 1] : []));
  if (nums.length === 0) return '';
  if (nums.length === 1) return `Step ${nums[0]}`;
  return `Steps ${nums.slice(0, -1).join(', ')} and ${nums[nums.length - 1]}`;
}

export function ComingSoon({ what }: { what: string }) {
  return (
    <Body>
      <Banner>{what} isn’t in this build yet. It comes after the care plan screens.</Banner>
      <T size={13} c={color.muted}>
        Figma: N6–N9 and P3–P4.
      </T>
    </Body>
  );
}
