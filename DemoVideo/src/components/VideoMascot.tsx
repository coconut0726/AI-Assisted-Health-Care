import { interpolate, spring, useCurrentFrame, useVideoConfig } from 'remotion';

// The flat cat-ghost (same paths as Mobile/src/components/Mascot.tsx and the Figma component),
// animated from the frame number so it renders deterministically: pop-in, 1.6 s float,
// ear wiggles and blinks that repeat every 3.2 s like the Figma splash timeline.
export function VideoMascot({ size = 260, delay = 0 }: { size?: number; delay?: number }) {
  const frame = useCurrentFrame() - delay;
  const { fps } = useVideoConfig();
  const t = frame / fps;

  const pop = spring({ frame, fps, config: { damping: 11, stiffness: 120 } });
  const float = t > 0.6 ? -12 * (0.5 - 0.5 * Math.cos(((t - 0.6) / 1.6) * Math.PI)) : 0;
  const shadowScale = 1 - 0.22 * (-float / 12);
  const cycle = t % 3.2;
  const earL = cycle > 1.3 && cycle < 1.7 ? Math.sin(((cycle - 1.3) / 0.4) * Math.PI * 2) * -12 : 0;
  const earR = cycle > 2.4 && cycle < 2.8 ? Math.sin(((cycle - 2.4) / 0.4) * Math.PI * 2) * 12 : 0;
  const blink = cycle > 1.9 && cycle < 2.04 ? interpolate(cycle, [1.9, 1.97, 2.04], [1, 0.1, 1]) : 1;

  return (
    <svg width={size} height={(size * 220) / 200} viewBox="0 0 200 220" style={{ overflow: 'visible', opacity: Math.min(1, pop * 1.5) }}>
      <defs>
        <linearGradient id="vm-body" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#56795A" />
          <stop offset="0.45" stopColor="#9FB899" />
          <stop offset="1" stopColor="#C9DCC3" />
        </linearGradient>
      </defs>
      <g transform={`translate(100 110) scale(${0.55 + 0.45 * pop}) translate(-100 -110)`}>
        <ellipse cx="100" cy="210" rx={45 * shadowScale} ry="6" fill="#3F6B45" opacity={0.14 * (0.6 + 0.4 * shadowScale)} />
        <g transform={`translate(0 ${float})`}>
          <g transform={`rotate(${earL} 66 74)`}>
            <path d="M 44 74 L 52 26 Q 54 19 61 24 L 88 40 Z" fill="#56795A" />
            <path d="M 55 58 L 58 35 L 76 45 Z" fill="#9FB899" />
          </g>
          <g transform={`rotate(${earR} 134 74)`}>
            <path d="M 156 74 L 148 26 Q 146 19 139 24 L 112 40 Z" fill="#56795A" />
            <path d="M 145 58 L 142 35 L 124 45 Z" fill="#9FB899" />
          </g>
          <path
            d="M 30 100 C 30 52 62 30 100 30 C 138 30 170 52 170 100 L 172 168 Q 166 188 150 180 Q 136 172 125 184 Q 112 196 100 184 Q 88 172 75 184 Q 64 196 50 180 Q 34 188 28 168 Z"
            fill="url(#vm-body)"
          />
          <ellipse cx="100" cy="97" rx="53" ry="29" fill="#1F3826" />
          <ellipse cx="80" cy="80" rx="18" ry="5" fill="#FFFFFF" opacity="0.16" transform="rotate(-8 80 80)" />
          <g transform={`translate(0 95) scale(1 ${blink}) translate(0 -95)`}>
            <ellipse cx="78.5" cy="95" rx="6.5" ry="11" fill="#F6FBF2" />
            <ellipse cx="121.5" cy="95" rx="6.5" ry="11" fill="#F6FBF2" />
          </g>
          <path d="M 96 107 L 104 107 L 100 112 Z" fill="#F6FBF2" stroke="#F6FBF2" strokeWidth="1.5" strokeLinejoin="round" />
        </g>
      </g>
    </svg>
  );
}
