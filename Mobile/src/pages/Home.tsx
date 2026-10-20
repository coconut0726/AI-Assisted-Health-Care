import { Link } from 'react-router-dom';
import { Body, Icon, Screen } from '../components/ui';
import { useDemo } from '../store';

export default function Home() {
  const { reset } = useDemo();
  return (
    <Screen>
      <Body>
        <div className="home-hero">
          <span className="glass-pill">Mobile demo</span>
          <h1>AI-Assisted Home Care</h1>
          <p>A nurse demonstrates a care procedure once. AI turns it into step-by-step guidance for home and reports back on how it is going.</p>
        </div>
        <p className="section-label">Open the app as</p>
        <Link to="/nurse" className="card role clickable">
          <span className="avatar">NL</span>
          <span className="patient-info">
            <strong>Nurse</strong>
            <span className="muted small">Review and correct instructions, read AI weekly reports, send feedback</span>
          </span>
          <Icon name="chevron" />
        </Link>
        <Link to="/patient" className="card role clickable">
          <span className="avatar">JC</span>
          <span className="patient-info">
            <strong>Caregiver / patient</strong>
            <span className="muted small">Read the care plan, see what changed, receive feedback</span>
          </span>
          <Icon name="chevron" />
        </Link>
        <div className="banner tone-neutral">
          <span>Tip: open the nurse and the patient app in two tabs of the same browser. Changes appear in the other tab right away.</span>
        </div>
        <button className="btn outline" onClick={reset}>
          Reset demo
        </button>
      </Body>
    </Screen>
  );
}
