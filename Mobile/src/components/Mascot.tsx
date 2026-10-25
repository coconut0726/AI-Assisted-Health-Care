// Flat 2D cat-ghost, the AI care guide. Same shapes as the Figma component "Mascot / Cat Ghost"
// (200 × 220 design box). Class names hook into the animations in styles.css.
export default function Mascot({ size = 200, animated = true }: { size?: number; animated?: boolean }) {
  return (
    <svg
      className={animated ? 'mascot animated' : 'mascot'}
      width={size}
      height={(size * 220) / 200}
      viewBox="0 0 200 220"
      role="img"
      aria-label="Care guide mascot"
    >
      <defs>
        <linearGradient id="mascot-body" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#56795A" />
          <stop offset="0.45" stopColor="#9FB899" />
          <stop offset="1" stopColor="#C9DCC3" />
        </linearGradient>
      </defs>
      <ellipse className="m-shadow" cx="100" cy="210" rx="45" ry="6" fill="#3F6B45" opacity="0.14" />
      <g className="m-ghost">
        <g className="m-ear m-ear-l">
          <path d="M 44 74 L 52 26 Q 54 19 61 24 L 88 40 Z" fill="#56795A" />
          <path d="M 55 58 L 58 35 L 76 45 Z" fill="#9FB899" />
        </g>
        <g className="m-ear m-ear-r">
          <path d="M 156 74 L 148 26 Q 146 19 139 24 L 112 40 Z" fill="#56795A" />
          <path d="M 145 58 L 142 35 L 124 45 Z" fill="#9FB899" />
        </g>
        <path
          d="M 30 100 C 30 52 62 30 100 30 C 138 30 170 52 170 100 L 172 168 Q 166 188 150 180 Q 136 172 125 184 Q 112 196 100 184 Q 88 172 75 184 Q 64 196 50 180 Q 34 188 28 168 Z"
          fill="url(#mascot-body)"
        />
        <ellipse cx="100" cy="97" rx="53" ry="29" fill="#1F3826" />
        <ellipse cx="80" cy="80" rx="18" ry="5" fill="#FFFFFF" opacity="0.16" transform="rotate(-8 80 80)" />
        <g className="m-eyes">
          <ellipse cx="78.5" cy="95" rx="6.5" ry="11" fill="#F6FBF2" />
          <ellipse cx="121.5" cy="95" rx="6.5" ry="11" fill="#F6FBF2" />
        </g>
        <path d="M 96 107 L 104 107 L 100 112 Z" fill="#F6FBF2" stroke="#F6FBF2" strokeWidth="1.5" strokeLinejoin="round" />
      </g>
    </svg>
  );
}
