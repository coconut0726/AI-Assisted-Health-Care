import type { ReactNode } from 'react';
import { AbsoluteFill, interpolate, Sequence, spring, useCurrentFrame, useVideoConfig } from 'remotion';
import { C, SceneShell, VrClip } from './components/Frame';
import { PhoneScreen } from './components/Phone';
import { VideoMascot } from './components/VideoMascot';
import { correcting, draft, reportSent, saved } from './states';

/** Splits a scene into consecutive beats whose lengths are shares of the scene. */
function Beats({ shares, children }: { shares: number[]; children: ReactNode[] }) {
  const { durationInFrames } = useVideoConfig();
  const total = shares.reduce((a, b) => a + b, 0);
  let from = 0;
  return (
    <>
      {children.map((child, i) => {
        const len = i === children.length - 1 ? durationInFrames - from : Math.round((shares[i] / total) * durationInFrames);
        const seq = (
          <Sequence key={i} from={from} durationInFrames={len} layout="none">
            <Fade>{child}</Fade>
          </Sequence>
        );
        from += len;
        return seq;
      })}
    </>
  );
}

function Fade({ children }: { children: ReactNode }) {
  const frame = useCurrentFrame();
  const opacity = interpolate(frame, [0, 8], [0, 1], { extrapolateRight: 'clamp' });
  return <AbsoluteFill style={{ opacity, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>{children}</AbsoluteFill>;
}

function Title({ size = 88, sub }: { size?: number; sub?: string }) {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = spring({ frame: frame - 12, fps, config: { damping: 18 } });
  return (
    <div style={{ textAlign: 'center', opacity: p, transform: `translateY(${(1 - p) * 24}px)` }}>
      <div style={{ fontSize: size, fontWeight: 700, letterSpacing: -1 }}>AI-Assisted Home Care</div>
      {sub && <div style={{ fontSize: 34, color: C.muted, marginTop: 14 }}>{sub}</div>}
    </div>
  );
}

function Halo({ children }: { children: ReactNode }) {
  const frame = useCurrentFrame();
  const s = interpolate(frame, [0, 20], [0.7, 1], { extrapolateRight: 'clamp' });
  return (
    <div style={{ position: 'relative', width: 400, height: 400, display: 'grid', placeItems: 'center' }}>
      <div style={{ position: 'absolute', inset: 0, borderRadius: 999, background: '#E8F0E4', transform: `scale(${s})` }} />
      <div style={{ position: 'relative', marginTop: -10 }}>{children}</div>
    </div>
  );
}

export function IntroScene() {
  return (
    <SceneShell>
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center', gap: 40, paddingBottom: 60 }}>
        <Halo>
          <VideoMascot size={280} />
        </Halo>
        <Title sub="An AI care guide for wound care after discharge" />
      </AbsoluteFill>
    </SceneShell>
  );
}

export function OutroScene() {
  return (
    <SceneShell>
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center', gap: 36, paddingBottom: 60 }}>
        <Halo>
          <VideoMascot size={240} />
        </Halo>
        <Title size={76} sub="The nurse's guidance, at home, every time." />
        <div style={{ fontSize: 22, color: C.muted, marginTop: 10 }}>Demo · James Carter is a fictional case</div>
      </AbsoluteFill>
    </SceneShell>
  );
}

function Row({ icon, title, text, delay }: { icon: string; title: string; text: string; delay: number }) {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const p = spring({ frame: frame - delay, fps, config: { damping: 18 } });
  return (
    <div style={{ display: 'flex', gap: 24, alignItems: 'center', opacity: p, transform: `translateX(${(1 - p) * 40}px)` }}>
      <span style={{ width: 72, height: 72, borderRadius: 36, background: C.primaryBg, display: 'grid', placeItems: 'center', fontSize: 34 }}>{icon}</span>
      <div>
        <div style={{ fontSize: 34, fontWeight: 600 }}>{title}</div>
        <div style={{ fontSize: 26, color: C.muted, marginTop: 4 }}>{text}</div>
      </div>
    </div>
  );
}

export function CaseScene() {
  const { durationInFrames } = useVideoConfig();
  const d = (share: number) => Math.round(share * durationInFrames);
  return (
    <SceneShell section="The case">
      <AbsoluteFill style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 110, paddingBottom: 80 }}>
        <div
          style={{
            width: 560, borderRadius: 40, background: C.hero, color: '#fff', padding: '44px 48px',
            boxShadow: '0 30px 60px rgba(61,97,66,0.25)', display: 'flex', flexDirection: 'column', gap: 18,
          }}
        >
          <span style={{ alignSelf: 'flex-start', fontSize: 22, fontWeight: 600, padding: '6px 18px', borderRadius: 999, background: 'rgba(255,255,255,0.24)' }}>
            Fictional case for this demo
          </span>
          <div style={{ display: 'flex', alignItems: 'center', gap: 22 }}>
            <span style={{ width: 96, height: 96, borderRadius: 48, background: 'rgba(255,255,255,0.9)', color: C.dark, display: 'grid', placeItems: 'center', fontSize: 38, fontWeight: 700 }}>JC</span>
            <div>
              <div style={{ fontSize: 52, fontWeight: 700 }}>James Carter</div>
              <div style={{ fontSize: 26, opacity: 0.9 }}>Burn Unit · Nurse Lee</div>
            </div>
          </div>
          <div style={{ height: 1, background: 'rgba(255,255,255,0.3)', margin: '6px 0' }} />
          <div style={{ fontSize: 28, lineHeight: 1.45 }}>Burn on the forearm · discharged Sep 28</div>
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 42 }}>
          <Row icon="🏥" title="Taught once at discharge" text="The nurse demonstrates the dressing change" delay={d(0.35)} />
          <Row icon="🩹" title="Dressing change, once a day" text="Clean, cream, pad, gauze, tape — at home" delay={d(0.5)} />
          <Row icon="🏠" title="On his own at home" text="No one there to catch mistakes" delay={d(0.75)} />
        </div>
      </AbsoluteFill>
    </SceneShell>
  );
}

export function RecordScene() {
  return (
    <SceneShell section="Nurse records the instruction" step="1">
      <Beats shares={[3, 8, 4]}>
        <VrClip src="vr/nurse-start.mp4" label="Nurse · VR headset — start recording" />
        <VrClip src="vr/nurse-recording.mp4" label="Recording: what the nurse sees and says, with a live transcript" />
        <VrClip src="vr/nurse-saved.mp4" label="Saved — the AI decomposes it into steps" />
      </Beats>
    </SceneShell>
  );
}

function Side({ children, title, text }: { children: ReactNode; title: string; text: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 90 }}>
      {children}
      <div style={{ width: 560 }}>
        <div style={{ fontSize: 44, fontWeight: 700, lineHeight: 1.2 }}>{title}</div>
        <div style={{ fontSize: 28, color: C.muted, marginTop: 16, lineHeight: 1.45 }}>{text}</div>
      </div>
    </div>
  );
}

const PHONE = 1;

export function EditScene() {
  return (
    <SceneShell section="Nurse reviews and corrects it on the phone" step="2">
      <Beats shares={[2.5, 4, 2.2, 3.3, 2.5, 3.5]}>
        <Side title="My patients" text="James's new instruction is ready to review.">
          <PhoneScreen state={draft} route="/nurse" highlight=".card.patient.clickable" scale={PHONE} />
        </Side>
        <Side title="The AI's draft" text="Care steps with what “done” looks like, plus an oversight checklist. Step 2 lost its condition: “Wet the bandage with water.”">
          <PhoneScreen state={draft} route="/nurse/james/instruction" highlight="article.step:nth-of-type(2)" scale={PHONE} />
        </Side>
        <Side title="Fix it in place" text="The nurse's own words from the recording are right there to check against.">
          <PhoneScreen state={draft} route="/nurse/james/steps/s2/edit" highlight=".source" scale={PHONE} />
        </Side>
        <Side title="“If the old bandage is stuck…”" text="The condition is restored and tagged as a conditional instruction.">
          <PhoneScreen state={correcting} route="/nurse/james/steps/s2/edit" highlight=".body > div.field:nth-of-type(2) textarea" scale={PHONE} />
        </Side>
        <Side title="Save & sync" text="The corrected instruction goes to James's app right away.">
          <PhoneScreen state={saved} route="/nurse/james/instruction" routeState={{ saved: 'Step 2' }} highlight=".banner" scale={PHONE} />
        </Side>
        <Side title="James sees what changed" text="His care plan flags the update, so he never follows outdated steps.">
          <PhoneScreen state={{ ...saved, patientSeenVersion: 1 }} route="/patient/instruction" highlight=".banner" scale={PHONE} />
        </Side>
      </Beats>
    </SceneShell>
  );
}

export function GuideScene() {
  return (
    <SceneShell section="James is guided at home in VR" step="3">
      <Beats shares={[3.5, 4, 4, 4, 3.5]}>
        <VrClip src="vr/patient-start.mp4" label="Supplies in view, updated step flagged" />
        <VrClip src="vr/patient-step2.mp4" label="Step by step, with the care guide speaking each instruction" />
        <VrClip src="vr/patient-step3.mp4" label="Clean the burn — pat, don't rub" />
        <VrClip src="vr/patient-step45.mp4" label="Cream, pad, gauze — moving on when ready" />
        <VrClip src="vr/patient-done.mp4" label="All 5 steps done — session saved for the nurse" />
      </Beats>
    </SceneShell>
  );
}

export function ReportScene() {
  return (
    <SceneShell section="Weekly report back to the nurse" step="4">
      <Beats shares={[3.5, 5, 4, 4.5]}>
        <Side title="A weekly AI report" text="Every session is checked against the oversight checklist — only the nurse sees it first.">
          <PhoneScreen state={saved} route="/nurse/james/feedback" highlight=".report-card" scale={PHONE} />
        </Side>
        <Side title="What needs attention" text="Rubbing while drying in 4 of 7 sessions; one item the camera couldn't see clearly.">
          <PhoneScreen state={saved} route="/nurse/james/report" highlight="article.finding:nth-of-type(1)" scale={PHONE} />
        </Side>
        <Side title="Forward it, add a note" text="The nurse shares the report and writes to James in their own words.">
          <PhoneScreen state={reportSent} route="/nurse/james/feedback" highlight=".body > .chat-item:nth-last-child(2) .bubble" scale={PHONE} />
        </Side>
        <Side title="James gets the feedback" text="Clear things to fix before small mistakes become a setback.">
          <PhoneScreen state={reportSent} route="/patient/feedback" highlight=".report-card" scale={PHONE} />
        </Side>
      </Beats>
    </SceneShell>
  );
}
