import { useState } from 'react';
import { Platform, Pressable, StyleSheet, TextInput, View, type TextInputProps } from 'react-native';
import { T } from './components';
import { color, font } from './theme';

/** Rounded white input from N4 / N10, with an optional "Remove" on the right. */
export function Input({ onRemove, style, ...rest }: TextInputProps & { onRemove?: () => void }) {
  const [focused, setFocused] = useState(false);
  return (
    <View style={[styles.box, focused && styles.focused]}>
      <TextInput
        // Native multiline inputs grow with the text; on web a textarea starts two rows tall, so the
        // browser preview uses single-line inputs instead.
        multiline={Platform.OS !== 'web'}
        placeholderTextColor={color.placeholder}
        {...rest}
        onFocus={(e) => {
          setFocused(true);
          rest.onFocus?.(e);
        }}
        onBlur={(e) => {
          setFocused(false);
          rest.onBlur?.(e);
        }}
        style={[styles.input, style]}
      />
      {onRemove && (
        <Pressable onPress={onRemove} hitSlop={8} accessibilityRole="button" accessibilityLabel="Remove">
          <T w="semibold" size={13} c="#8aa58d">
            Remove
          </T>
        </Pressable>
      )}
    </View>
  );
}

export function Switch({ value, onChange, label }: { value: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityLabel={label}
      accessibilityState={{ checked: value }}
      onPress={() => onChange(!value)}
      hitSlop={8}
      style={[styles.track, { backgroundColor: value ? color.primary : '#dfe5dc' }]}
    >
      <View style={[styles.knob, { left: value ? 19 : 3 }]} />
    </Pressable>
  );
}

export function Note({ children, tone = 'review' }: { children: string; tone?: 'review' | 'info' }) {
  const review = tone === 'review';
  return (
    <View style={[styles.note, { backgroundColor: review ? color.amberBg : color.seg }]}>
      <T size={review ? 12 : 13} c={review ? color.amber : color.muted}>
        {children}
      </T>
    </View>
  );
}

const styles = StyleSheet.create({
  box: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    backgroundColor: color.surface,
    borderWidth: 1,
    borderColor: color.border,
    borderRadius: 16,
    paddingHorizontal: 12,
    paddingVertical: 4,
    minHeight: 42,
  },
  focused: { borderColor: color.primary, borderWidth: 1.5 },
  input: { flex: 1, fontFamily: font.regular, fontSize: 15, color: color.text, paddingVertical: 8 },
  track: { width: 40, height: 24, borderRadius: 12 },
  knob: { position: 'absolute', top: 3, width: 18, height: 18, borderRadius: 9, backgroundColor: '#fff' },
  note: { borderRadius: 12, paddingHorizontal: 10, paddingVertical: 6 },
});
