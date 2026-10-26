import type { ReactNode } from 'react';
import { AbsoluteFill, interpolate, OffthreadVideo, staticFile, useCurrentFrame, useVideoConfig } from 'remotion';
import { FPS, VOICE_LEAD_IN } from '../timing';

export const C = {
  bg: '#F5F8F2',
  primary: '#5B8760',
  dark: '#3F6B45',
  primaryBg: '#E2EEDD',
  text: '#1F2A22',
  muted: '#7B877D',
  hero: 'linear-gradient(180deg, #8FB58F 0%, #4F7D57 100%)',
};

/** Scene wrapper: app background, fade in/out, a section label top-left. */
export function SceneShell({ children, section, step }: { children: ReactNode; section?: string; step?: string }) {
  const frame = useCurrentFrame();
  const { durationInFrames } = useVideoConfig();
  const opacity = interpolate(frame, [0, 10, durationInFrames - 10, durationInFrames], [0, 1, 1, 0], {
    extrapolateLeft: 'clamp',
    extrapolateRight: 'clamp',
  });
  return (
    <AbsoluteFill style={{ background: C.bg, fontFamily: 'Outfit', color: C.text, opacity }}>
      {section && (
        <div style={{ position: 'absolute', top: 56, left: 80, display: 'flex', alignItems: 'center', gap: 16 }}>
          {step && (
            <span
              style={{
                width: 44, height: 44, borderRadius: 22, background: C.primary, color: '#fff',
                display: 'grid', placeItems: 'center', fontSize: 22, fontWeight: 700,
              }}
            >
              {step}
            </span>
          )}
          <span style={{ fontSize: 30, fontWeight: 600 }}>{section}</span>
        </div>
      )}
      {children}
    </AbsoluteFill>
  );
}

/**
 * Subtitles: the scene's narration split into sentences, each shown for a share of the voice
 * time proportional to its length.
 */
export function Captions({ text, voiceSeconds }: { text: string; voiceSeconds: number }) {
  const frame = useCurrentFrame();
  const t = frame / FPS - VOICE_LEAD_IN;
  const parts = text.match(/[^.!?—:]+[.!?—:]*\s*/g)?.map((s) => s.trim()).filter(Boolean) ?? [text];
  const total = parts.reduce((n, p) => n + p.length, 0);
  let acc = 0;
  let current = '';
  for (const p of parts) {
    const start = (acc / total) * voiceSeconds;
    acc += p.length;
    if (t >= start - 0.05) current = p;
  }
  if (t < -0.2 || t > voiceSeconds + 0.6) current = '';
  return (
    <div style={{ position: 'absolute', left: 0, right: 0, bottom: 46, display: 'flex', justifyContent: 'center' }}>
      {current && (
        <div
          style={{
            maxWidth: 1400, padding: '14px 30px', borderRadius: 18, background: 'rgba(31,42,34,0.82)', color: '#fff',
            fontSize: 34, lineHeight: 1.3, fontWeight: 500, textAlign: 'center',
          }}
        >
          {current}
        </div>
      )}
    </div>
  );
}

/** A VR clip in a rounded "headset view" frame with a caption chip. */
export function VrClip({ src, width = 1180, label }: { src: string; width?: number; label?: string }) {
  const frame = useCurrentFrame();
  const scale = interpolate(frame, [0, 12], [0.97, 1], { extrapolateRight: 'clamp' });
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 18, transform: `scale(${scale})` }}>
      <div style={{ width, aspectRatio: '16 / 9', borderRadius: 36, overflow: 'hidden', boxShadow: '0 40px 80px rgba(31,42,34,0.25)', background: '#000' }}>
        <OffthreadVideo src={staticFile(src)} muted style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
      </div>
      {label && (
        <span style={{ fontSize: 24, fontWeight: 600, padding: '8px 22px', borderRadius: 999, background: C.primaryBg, color: C.dark }}>{label}</span>
      )}
    </div>
  );
}
