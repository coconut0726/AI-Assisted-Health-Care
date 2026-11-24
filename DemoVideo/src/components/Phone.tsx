import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { useCurrentFrame } from 'remotion';
import '../../../Mobile/src/styles.css';
import { DemoProvider } from '../../../Mobile/src/store';
import type { DemoState } from '../../../Mobile/src/types';
import Report from '../../../Mobile/src/pages/Report';
import EditStep from '../../../Mobile/src/pages/nurse/EditStep';
import NurseFeedback from '../../../Mobile/src/pages/nurse/Feedback';
import NurseInstruction from '../../../Mobile/src/pages/nurse/Instruction';
import Patients from '../../../Mobile/src/pages/nurse/Patients';
import PatientFeedback from '../../../Mobile/src/pages/patient/Feedback';
import PatientInstruction from '../../../Mobile/src/pages/patient/Instruction';

let phoneIds = 0;

/**
 * A phone showing one real screen of the mobile app at a fixed state and route.
 * `highlight` is a CSS selector inside the screen that gets a pulsing sage ring (a "tap").
 */
export function PhoneScreen({
  state,
  route,
  routeState,
  highlight,
  scale = 1,
  label,
}: {
  state: DemoState;
  route: string;
  routeState?: unknown;
  highlight?: string;
  scale?: number;
  label?: string;
}) {
  const frame = useCurrentFrame();
  const id = `ph${(phoneIds = (phoneIds + 1) % 1000)}`;
  const pulse = 0.5 + 0.5 * Math.sin(frame / 5);
  const ring = highlight
    ? `#${id} ${highlight} { outline: 3px solid #5B8760; outline-offset: 3px; box-shadow: 0 0 0 ${6 + 6 * pulse}px rgba(91,135,96,${0.25 - 0.12 * pulse}) !important; }`
    : '';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 18 * scale }}>
      <div
        style={{
          width: 390 * scale,
          height: 844 * scale,
          borderRadius: 54 * scale,
          border: `${11 * scale}px solid #1F2A22`,
          boxShadow: '0 40px 80px rgba(31,42,34,0.25)',
          overflow: 'hidden',
          background: '#F5F8F2',
          boxSizing: 'content-box',
        }}
      >
        <style>{ring}</style>
        <div id={id} style={{ width: 390, height: 844, transform: `scale(${scale})`, transformOrigin: 'top left', position: 'relative' }}>
          <DemoProvider initial={state}>
            <MemoryRouter initialEntries={[{ pathname: route.split('?')[0], search: route.includes('?') ? '?' + route.split('?')[1] : '', state: routeState }]}>
              <Routes>
                <Route path="/nurse" element={<Patients />} />
                <Route path="/nurse/:patientId/instruction" element={<NurseInstruction />} />
                <Route path="/nurse/:patientId/steps/:stepId/edit" element={<EditStep />} />
                <Route path="/nurse/:patientId/feedback" element={<NurseFeedback />} />
                <Route path="/nurse/:patientId/report" element={<Report audience="nurse" />} />
                <Route path="/patient/instruction" element={<PatientInstruction />} />
                <Route path="/patient/feedback" element={<PatientFeedback />} />
                <Route path="/patient/report" element={<Report audience="patient" />} />
              </Routes>
            </MemoryRouter>
          </DemoProvider>
        </div>
      </div>
      {label && <Chip>{label}</Chip>}
    </div>
  );
}

export function Chip({ children, dark = false }: { children: React.ReactNode; dark?: boolean }) {
  return (
    <span
      style={{
        fontFamily: 'Outfit',
        fontSize: 22,
        fontWeight: 600,
        padding: '8px 20px',
        borderRadius: 999,
        background: dark ? '#3F6B45' : '#E2EEDD',
        color: dark ? '#FFFFFF' : '#3F6B45',
        whiteSpace: 'nowrap',
      }}
    >
      {children}
    </span>
  );
}
