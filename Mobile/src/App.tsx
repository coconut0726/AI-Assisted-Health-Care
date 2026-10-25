import { useCallback, useEffect, useState } from 'react';
import { HashRouter, Link, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import Splash, { splashSeen } from './components/Splash';
import Home from './pages/Home';
import Report from './pages/Report';
import EditRequirement from './pages/nurse/EditRequirement';
import EditStep from './pages/nurse/EditStep';
import NurseFeedback from './pages/nurse/Feedback';
import NurseInstruction from './pages/nurse/Instruction';
import Patients from './pages/nurse/Patients';
import PatientFeedback from './pages/patient/Feedback';
import PatientInstruction from './pages/patient/Instruction';
import { DemoProvider, useDemo } from './store';

function DemoPanel() {
  const { reset } = useDemo();
  const { pathname } = useLocation();
  const role = pathname.startsWith('/nurse') ? 'nurse' : pathname.startsWith('/patient') ? 'patient' : null;
  return (
    <aside className="demo-panel">
      <span className="glass-pill dark">Mobile demo</span>
      <h2>AI-Assisted Home Care</h2>
      <p>Switch role here. On a phone, open the site directly — this panel is hidden.</p>
      <div className="panel-links">
        <Link to="/nurse" className={role === 'nurse' ? 'panel-link active' : 'panel-link'}>
          Nurse app
        </Link>
        <Link to="/patient" className={role === 'patient' ? 'panel-link active' : 'panel-link'}>
          Caregiver / patient app
        </Link>
      </div>
      <p className="small">
        Show both side by side: open{' '}
        <a href={role === 'nurse' ? '#/patient' : '#/nurse'} target="_blank" rel="noreferrer">
          the {role === 'nurse' ? 'patient' : 'nurse'} app in a new tab
        </a>
        . Edits sync between tabs.
      </p>
      <button className="btn outline" onClick={reset}>
        Reset demo
      </button>
    </aside>
  );
}

export default function App() {
  const [showSplash, setShowSplash] = useState(() => !splashSeen());
  const hideSplash = useCallback(() => setShowSplash(false), []);
  useEffect(() => {
    const show = () => setShowSplash(true);
    window.addEventListener('ahc-show-splash', show);
    return () => window.removeEventListener('ahc-show-splash', show);
  }, []);

  return (
    <DemoProvider>
      <HashRouter>
        <div className="stage">
          <DemoPanel />
          <div className="phone">
            {showSplash && <Splash onDone={hideSplash} />}
            <Routes>
              <Route path="/" element={<Home />} />
              <Route path="/nurse" element={<Patients />} />
              <Route path="/nurse/:patientId" element={<Navigate to="instruction" replace />} />
              <Route path="/nurse/:patientId/instruction" element={<NurseInstruction />} />
              <Route path="/nurse/:patientId/steps/:stepId/edit" element={<EditStep />} />
              <Route path="/nurse/:patientId/requirements/:reqId/edit" element={<EditRequirement />} />
              <Route path="/nurse/:patientId/feedback" element={<NurseFeedback />} />
              <Route path="/nurse/:patientId/report" element={<Report audience="nurse" />} />
              <Route path="/patient" element={<Navigate to="/patient/instruction" replace />} />
              <Route path="/patient/instruction" element={<PatientInstruction />} />
              <Route path="/patient/feedback" element={<PatientFeedback />} />
              <Route path="/patient/report" element={<Report audience="patient" />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </div>
        </div>
      </HashRouter>
    </DemoProvider>
  );
}
