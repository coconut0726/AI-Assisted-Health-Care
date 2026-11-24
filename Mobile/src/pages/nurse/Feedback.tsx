import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { ReportCard } from '../../components/report';
import { Body, Bubble, DayChip, Icon, Screen } from '../../components/ui';
import { PATIENT } from '../../data/demo';
import { formatTime, useDemo } from '../../store';
import PatientHeader from './PatientHeader';

export default function NurseFeedback() {
  const { state, sendReport, sendMessage } = useDemo();
  const navigate = useNavigate();
  const [draft, setDraft] = useState('');
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: 'end' });
  }, [state.messages.length, state.reportSent]);

  const send = () => {
    const text = draft.trim();
    if (!text) return;
    sendMessage(text);
    setDraft('');
  };
  const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      send();
    }
  };

  let lastDay = '';
  return (
    <Screen>
      <PatientHeader tab="feedback" />
      <Body>
        {state.messages.map((m) => {
          const chip = m.day !== lastDay ? <DayChip>{m.day}</DayChip> : null;
          lastDay = m.day;
          return (
            <div key={m.id} className="chat-item">
              {chip}
              {m.kind === 'report' ? (
                <ReportCard
                  report={state.report}
                  audience="nurse"
                  sent={state.reportSent}
                  patientName={PATIENT.firstName}
                  onView={() => navigate(`/nurse/${PATIENT.id}/report`)}
                  onSend={sendReport}
                />
              ) : (
                <Bubble text={m.text ?? ''} time={formatTime(m.at)} mine />
              )}
            </div>
          );
        })}
        <div ref={endRef} />
      </Body>
      <footer className="composer">
        <textarea
          rows={1}
          value={draft}
          placeholder={`Write feedback to ${PATIENT.firstName}…`}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={onKey}
          aria-label="Message"
        />
        <button className="send" onClick={send} disabled={!draft.trim()} aria-label="Send">
          <Icon name="send" size={18} />
        </button>
      </footer>
    </Screen>
  );
}
