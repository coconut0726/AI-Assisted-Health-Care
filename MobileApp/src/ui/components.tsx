import { router } from 'expo-router';
import type { ReactNode } from 'react';
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type TextProps,
  type TextStyle,
  type ViewStyle,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import Svg, { Path } from 'react-native-svg';
import { color, font, shadow, shadowUp, tone as tones, type Tone } from './theme';

type Weight = keyof typeof font;

/** router.back(), or go to `fallback` when the screen was opened directly (deep link, web reload). */
export function goBack(fallback: Parameters<typeof router.replace>[0] = '/') {
  if (router.canGoBack()) router.back();
  else router.replace(fallback);
}

export function T({ w = 'regular', size = 15, c = color.text, style, ...rest }: TextProps & { w?: Weight; size?: number; c?: string }) {
  return <Text {...rest} style={[{ fontFamily: font[w], fontSize: size, color: c, lineHeight: Math.round(size * 1.35) }, style]} />;
}

const ICONS = {
  back: 'M15 18l-6-6 6-6',
  chevron: 'M9 18l6-6-6-6',
  down: 'M6 9l6 6 6-6',
  check: 'M20 6L9 17l-5-5',
  list: 'M9 6h12M9 12h12M9 18h12M4 6h.01M4 12h.01M4 18h.01',
  chat: 'M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z',
} as const;
export type IconName = keyof typeof ICONS;

export function Icon({ name, size = 20, c = color.muted }: { name: IconName; size?: number; c?: string }) {
  return (
    <Svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={c} strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round">
      <Path d={ICONS[name]} />
    </Svg>
  );
}

export function Screen({ children }: { children: ReactNode }) {
  const insets = useSafeAreaInsets();
  return <View style={[s.screen, { paddingTop: insets.top }]}>{children}</View>;
}

export function Body({ children, style }: { children: ReactNode; style?: StyleProp<ViewStyle> }) {
  return (
    <ScrollView style={{ flex: 1 }} contentContainerStyle={[s.body, style]} keyboardShouldPersistTaps="handled">
      {children}
    </ScrollView>
  );
}

export function Nav({ title, sub, back, large }: { title: string; sub?: string; back?: boolean; large?: boolean }) {
  return (
    <View style={s.nav}>
      {back && (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Back"
          onPress={() => goBack()}
          style={s.iconBtn}
        >
          <Icon name="back" c={color.text} />
        </Pressable>
      )}
      <View style={{ flex: 1 }}>
        <T w="semibold" size={large ? 26 : 21} numberOfLines={1}>
          {title}
        </T>
        {sub ? (
          <T size={13} c={color.muted} numberOfLines={1}>
            {sub}
          </T>
        ) : null}
      </View>
    </View>
  );
}

export function Tabs({ items }: { items: { label: string; active: boolean; onPress: () => void }[] }) {
  return (
    <View style={{ flexDirection: 'row' }}>
      {items.map((t) => (
        <Pressable key={t.label} accessibilityRole="tab" accessibilityState={{ selected: t.active }} onPress={t.onPress} style={[s.tab, t.active && s.tabActive]}>
          <T w={t.active ? 'semibold' : 'medium'} c={t.active ? color.primary : color.muted}>
            {t.label}
          </T>
        </Pressable>
      ))}
    </View>
  );
}

export function Segmented({ items }: { items: { label: string; active: boolean; onPress: () => void }[] }) {
  return (
    <View style={s.segmented} accessibilityRole="tablist">
      {items.map((t) => (
        <Pressable key={t.label} accessibilityRole="tab" accessibilityState={{ selected: t.active }} onPress={t.onPress} style={[s.seg, t.active && s.segActive]}>
          <T size={14} w={t.active ? 'semibold' : 'medium'} c={t.active ? color.text : color.muted}>
            {t.label}
          </T>
        </Pressable>
      ))}
    </View>
  );
}

export function Pill({ children, tone = 'neutral' }: { children: ReactNode; tone?: Tone }) {
  return (
    <View style={[s.pill, { backgroundColor: tones[tone].bg }]}>
      <T size={12} w="medium" c={tones[tone].fg}>
        {children}
      </T>
    </View>
  );
}

export function Banner({ children, tone = 'neutral' }: { children: ReactNode; tone?: Tone }) {
  return (
    <View style={[s.banner, { backgroundColor: tones[tone].bg }]}>
      <T size={13} w="medium" c={tones[tone].fg}>
        {children}
      </T>
    </View>
  );
}

export function Card({ children, style, onPress }: { children: ReactNode; style?: StyleProp<ViewStyle>; onPress?: () => void }) {
  if (onPress)
    return (
      <Pressable onPress={onPress} style={({ pressed }) => [s.card, style, pressed && { opacity: 0.85 }]}>
        {children}
      </Pressable>
    );
  return <View style={[s.card, style]}>{children}</View>;
}

export function LinkText({ children, onPress, style, c = color.primary }: { children: ReactNode; onPress: () => void; style?: StyleProp<TextStyle>; c?: string }) {
  return (
    <Pressable onPress={onPress} hitSlop={8} accessibilityRole="button" style={{ alignSelf: 'flex-start' }}>
      <T w="semibold" size={13} c={c} style={style}>
        {children}
      </T>
    </Pressable>
  );
}

export function Button({ label, onPress, kind = 'primary', disabled }: { label: string; onPress: () => void; kind?: 'primary' | 'outline'; disabled?: boolean }) {
  const primary = kind === 'primary';
  return (
    <Pressable
      accessibilityRole="button"
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [s.btn, primary ? s.btnPrimary : s.btnOutline, (pressed || disabled) && { opacity: 0.7 }]}
    >
      <T w="semibold" c={primary ? '#fff' : color.text}>
        {label}
      </T>
    </Pressable>
  );
}

export function ActionBar({ children }: { children: ReactNode }) {
  const insets = useSafeAreaInsets();
  return <View style={[s.actionBar, { paddingBottom: Math.max(16, insets.bottom) }]}>{children}</View>;
}

export function Label({ children, c = color.muted }: { children: ReactNode; c?: string }) {
  return (
    <T w="semibold" size={12} c={c}>
      {children}
    </T>
  );
}

export const s = StyleSheet.create({
  screen: { flex: 1, backgroundColor: color.bg },
  body: { padding: 16, gap: 10, paddingBottom: 32 },
  nav: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingHorizontal: 16, paddingTop: 14, paddingBottom: 10 },
  iconBtn: { width: 36, height: 36, borderRadius: 999, backgroundColor: color.surface, alignItems: 'center', justifyContent: 'center', ...shadow },
  tab: { flex: 1, alignItems: 'center', paddingTop: 10, paddingBottom: 9, borderBottomWidth: 2, borderBottomColor: color.border },
  tabActive: { borderBottomColor: color.primary },
  segmented: { flexDirection: 'row', padding: 3, borderRadius: 999, backgroundColor: color.seg },
  seg: { flex: 1, alignItems: 'center', paddingVertical: 8, borderRadius: 999 },
  segActive: { backgroundColor: color.surface, shadowColor: '#3d6142', shadowOpacity: 0.12, shadowRadius: 4, shadowOffset: { width: 0, height: 1 }, elevation: 1 },
  pill: { paddingHorizontal: 10, paddingVertical: 3, borderRadius: 999, alignSelf: 'flex-start' },
  banner: { paddingHorizontal: 14, paddingVertical: 11, borderRadius: 16 },
  card: { backgroundColor: color.surface, borderRadius: 24, padding: 14, gap: 6, ...shadow },
  btn: { flex: 1, borderRadius: 999, paddingVertical: 12, paddingHorizontal: 18, alignItems: 'center' },
  btnPrimary: { backgroundColor: color.primary },
  btnOutline: { backgroundColor: color.surface, borderWidth: 1, borderColor: color.border },
  actionBar: { flexDirection: 'row', gap: 10, paddingTop: 12, paddingHorizontal: 16, backgroundColor: color.surface, borderTopLeftRadius: 24, borderTopRightRadius: 24, ...shadowUp },
});
