import { Outfit_400Regular, Outfit_500Medium, Outfit_600SemiBold, Outfit_700Bold, useFonts } from '@expo-google-fonts/outfit';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useCallback, useEffect, useState } from 'react';
import { View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { PlanProvider } from '@/store/PlanStore';
import Splash, { splashSeen } from '@/ui/Splash';
import { color } from '@/ui/theme';

SplashScreen.preventAutoHideAsync().catch(() => undefined);

export default function RootLayout() {
  const [fontsLoaded, fontError] = useFonts({ Outfit_400Regular, Outfit_500Medium, Outfit_600SemiBold, Outfit_700Bold });
  const [showSplash, setShowSplash] = useState<boolean | null>(null);

  useEffect(() => {
    splashSeen().then((seen) => setShowSplash(!seen));
  }, []);

  const ready = (fontsLoaded || !!fontError) && showSplash !== null;
  useEffect(() => {
    if (ready) SplashScreen.hideAsync().catch(() => undefined);
  }, [ready]);

  const hideSplash = useCallback(() => setShowSplash(false), []);
  if (!ready) return null;

  return (
    <SafeAreaProvider>
      <PlanProvider>
        <View style={{ flex: 1, backgroundColor: color.bg }}>
          <StatusBar style="dark" />
          <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: color.bg } }} />
          {showSplash && <Splash onDone={hideSplash} />}
        </View>
      </PlanProvider>
    </SafeAreaProvider>
  );
}
