import AsyncStorage from '@react-native-async-storage/async-storage';
import { useEffect, useState } from 'react';
import { Animated, Easing, Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { NURSE } from '@/data/demo';
import { T } from './components';
import Mascot from './Mascot';
import { color } from './theme';

const SEEN_KEY = 'ahc.splash-seen';
const DURATION_MS = 3200; // Figma timeline of "S0 Splash · First launch"
const FADE_MS = 400;

export async function splashSeen(): Promise<boolean> {
  try {
    return (await AsyncStorage.getItem(SEEN_KEY)) === '1';
  } catch {
    return false;
  }
}

export async function resetSplash() {
  await AsyncStorage.removeItem(SEEN_KEY).catch(() => undefined);
}

/** First-launch loading screen (S0). Tap to skip; remembered so it only shows once. */
export default function Splash({ onDone }: { onDone: () => void }) {
  const insets = useSafeAreaInsets();
  const [leaving, setLeaving] = useState(false);
  const opacity = useState(() => new Animated.Value(1))[0];
  const pop = useState(() => new Animated.Value(0))[0];
  const text = useState(() => new Animated.Value(0))[0];
  const float = useState(() => new Animated.Value(0))[0];
  const dots = [useState(() => new Animated.Value(0))[0], useState(() => new Animated.Value(0))[0], useState(() => new Animated.Value(0))[0]];

  useEffect(() => {
    Animated.sequence([
      Animated.spring(pop, { toValue: 1, friction: 5, tension: 70, useNativeDriver: true }),
      Animated.timing(text, { toValue: 1, duration: 400, useNativeDriver: true }),
    ]).start();
    Animated.loop(
      Animated.sequence([
        Animated.timing(float, { toValue: 1, duration: 1600, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(float, { toValue: 0, duration: 1600, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      ]),
    ).start();
    dots.forEach((d, i) =>
      Animated.loop(
        Animated.sequence([
          Animated.delay(i * 140),
          Animated.timing(d, { toValue: 1, duration: 250, useNativeDriver: true }),
          Animated.timing(d, { toValue: 0, duration: 250, useNativeDriver: true }),
          Animated.delay(500 - i * 140),
        ]),
      ).start(),
    );
    const t = setTimeout(() => setLeaving(true), DURATION_MS);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!leaving) return;
    AsyncStorage.setItem(SEEN_KEY, '1').catch(() => undefined);
    Animated.timing(opacity, { toValue: 0, duration: FADE_MS, useNativeDriver: true }).start(() => onDone());
  }, [leaving, onDone, opacity]);

  const y = float.interpolate({ inputRange: [0, 1], outputRange: [0, -8] });
  const shadowScale = float.interpolate({ inputRange: [0, 1], outputRange: [1, 0.85] });

  return (
    <Animated.View style={[StyleSheet.absoluteFill, styles.root, { opacity }]} pointerEvents={leaving ? 'none' : 'auto'}>
      <Pressable style={styles.center} onPress={() => setLeaving(true)} accessibilityRole="button" accessibilityLabel="Skip">
        <Animated.View style={[styles.art, { transform: [{ scale: pop }] }]}>
          <View style={styles.halo} />
          <Animated.View style={[styles.shadow, { transform: [{ scaleX: shadowScale }] }]} />
          <Animated.View style={{ position: 'absolute', left: 30, top: 20, transform: [{ translateY: y }] }}>
            <Mascot size={200} />
          </Animated.View>
        </Animated.View>
        <Animated.View style={{ opacity: text, alignItems: 'center' }}>
          <T w="semibold" size={26}>
            AI-Assisted Home Care
          </T>
          <T size={16} c={color.muted} style={{ marginTop: 10 }}>
            Your care guide is getting ready…
          </T>
        </Animated.View>
        <View style={styles.dots}>
          {dots.map((d, i) => (
            <Animated.View key={i} style={[styles.dot, { transform: [{ translateY: d.interpolate({ inputRange: [0, 1], outputRange: [0, -6] }) }] }]} />
          ))}
        </View>
      </Pressable>
      <View style={[styles.foot, { bottom: Math.max(24, insets.bottom) }]} pointerEvents="none">
        <T size={13} w="medium" c={color.muted}>
          {NURSE.unit} · {NURSE.name}
        </T>
        <T size={11} c={color.muted} style={{ opacity: 0.6, marginTop: 8 }}>
          Tap to skip
        </T>
      </View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  root: { backgroundColor: color.bg, zIndex: 50 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  art: { width: 260, height: 260, marginBottom: 36 },
  halo: { position: 'absolute', width: 260, height: 260, borderRadius: 130, backgroundColor: '#e8f0e4' },
  shadow: { position: 'absolute', left: 85, top: 214, width: 90, height: 12, borderRadius: 6, backgroundColor: '#3F6B45', opacity: 0.14 },
  dots: { flexDirection: 'row', gap: 6, marginTop: 34 },
  dot: { width: 12, height: 12, borderRadius: 6, backgroundColor: color.primary },
  foot: { position: 'absolute', left: 0, right: 0, alignItems: 'center' },
});
