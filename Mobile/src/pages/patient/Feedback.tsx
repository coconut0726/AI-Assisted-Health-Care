import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { ReportCard } from '../../components/report';
import { Body, BottomTabs, Bubble, DayChip, Nav, Screen } from '../../components/ui';
import { NURSE, PATIENT } from '../../data/demo';
import { formatTime, patientMessages, useDemo } from '../../store';

export default function PatientFeedback() {
  const { state, markMessagesSeen } = useDemo();
  const navigate = useNavigate();
  const messages = patientMessages(state);
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    markMessagesSeen();
    endRef.current?.scrollIntoView({ block: 'end' });
  }, [messages.length, markMessagesSeen]);

  let lastDay = '';
  return (
    <Screen>
      <Nav title="Feedback" sub={`From ${NURSE.name} · ${NURSE.unit}`} large />
      <Body>
        {messages.map((m) => {
          const chip = m.day !== lastDay ? <DayChip>{m.day}</DayChip> : null;
          lastDay = m.day;
          return (
            <div key={m.id} className="chat-item">
              {chip}
              {m.kind === 'report' ? (
                <ReportCard report={state.report} audience="patient" sent patientName={PATIENT.firstName} onView={() => navigate('/patient/report')} />
              ) : (
                <Bubble text={m.text ?? ''} time={formatTime(m.at)} mine={false} />
              )}
            </div>
          );
        })}
        <div ref={endRef} />
      </Body>
      <div className="readonly-note">Only your nurse can send messages here.</div>
      <BottomTabs active="feedback" />
    </Screen>
  );
}
