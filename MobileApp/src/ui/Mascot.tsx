import Svg, { Defs, Ellipse, G, LinearGradient, Path, Stop } from 'react-native-svg';

// Flat cat-ghost, the AI care guide. Same shapes as the Figma component "Mascot / Cat Ghost" (200 × 220 box).
// The floor shadow is drawn separately by the splash so the ghost can float above it.
export default function Mascot({ size = 200 }: { size?: number }) {
  return (
    <Svg width={size} height={(size * 200) / 200} viewBox="0 0 200 200" accessibilityLabel="Care guide mascot">
      <Defs>
        <LinearGradient id="mascot-body" x1="0" y1="0" x2="0" y2="1">
          <Stop offset="0" stopColor="#56795A" />
          <Stop offset="0.45" stopColor="#9FB899" />
          <Stop offset="1" stopColor="#C9DCC3" />
        </LinearGradient>
      </Defs>
      <G>
        <Path d="M 44 74 L 52 26 Q 54 19 61 24 L 88 40 Z" fill="#56795A" />
        <Path d="M 55 58 L 58 35 L 76 45 Z" fill="#9FB899" />
        <Path d="M 156 74 L 148 26 Q 146 19 139 24 L 112 40 Z" fill="#56795A" />
        <Path d="M 145 58 L 142 35 L 124 45 Z" fill="#9FB899" />
        <Path
          d="M 30 100 C 30 52 62 30 100 30 C 138 30 170 52 170 100 L 172 168 Q 166 188 150 180 Q 136 172 125 184 Q 112 196 100 184 Q 88 172 75 184 Q 64 196 50 180 Q 34 188 28 168 Z"
          fill="url(#mascot-body)"
        />
        <Ellipse cx="100" cy="97" rx="53" ry="29" fill="#1F3826" />
        <Ellipse cx="80" cy="80" rx="18" ry="5" fill="#FFFFFF" opacity={0.16} transform="rotate(-8 80 80)" />
        <Ellipse cx="78.5" cy="95" rx="6.5" ry="11" fill="#F6FBF2" />
        <Ellipse cx="121.5" cy="95" rx="6.5" ry="11" fill="#F6FBF2" />
        <Path d="M 96 107 L 104 107 L 100 112 Z" fill="#F6FBF2" stroke="#F6FBF2" strokeWidth={1.5} strokeLinejoin="round" />
      </G>
    </Svg>
  );
}
