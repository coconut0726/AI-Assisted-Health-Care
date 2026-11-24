import { useNavigate } from 'react-router-dom';
import { FindingCard, Stats } from '../components/report';
import { Body, BottomTabs, Nav, Screen } from '../components/ui';
import { NURSE, PATIENT } from '../data/demo';
import { useDemo } from '../store';

export default function Report({ audience }: { audience: 'nurse' | 'patient' }) {
  const { state, sendReport } = useDemo();
  const navigate = useNavigate();
  const r = state.report;
  const nurse = audience === 'nurse';
  const back = nurse ? `/nurse/${PATIENT.id}/feedback` : '/patient/feedback';

  return (
    <Screen>
      <Nav title="Weekly Report" sub={`${r.title} · ${r.range} · ${nurse ? 'AI-generated' : `from ${NURSE.name}`}`} back={back} />
      <Body>
        <Stats counts={r.counts} />
        <p className="section-label">{nurse ? 'Needs attention' : 'What to fix'}</p>
        {r.findings.map((f) => (
          <FindingCard key={f.id} finding={f} audience={audience} />
        ))}
      </Body>
      {nurse ? (
        <footer className="action-bar">
          {state.reportSent ? (
            <button className="btn outline" disabled>
              ✓ Sent to {PATIENT.firstName}
            </button>
          ) : (
            <button
              className="btn primary"
              onClick={() => {
                sendReport();
                navigate(back, { replace: true });
              }}
            >
              Send to patient
            </button>
          )}
        </footer>
      ) : (
        <BottomTabs active="feedback" />
      )}
    </Screen>
  );
}
