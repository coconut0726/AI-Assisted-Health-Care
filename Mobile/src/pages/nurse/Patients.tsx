import { Link } from 'react-router-dom';
import { Body, Icon, Nav, Pill, Screen } from '../../components/ui';
import { NURSE, OTHER_PATIENTS, PATIENT } from '../../data/demo';
import { useDemo } from '../../store';

export default function Patients() {
  const { state } = useDemo();
  return (
    <Screen>
      <Nav title="My Patients" sub={`${NURSE.unit} · ${NURSE.name}`} large back="/" />
      <Body>
        <Link to={`/nurse/${PATIENT.id}/instruction`} className="card patient clickable">
          <span className="avatar">{PATIENT.initials}</span>
          <span className="patient-info">
            <strong>{PATIENT.name}</strong>
            <span>{state.instruction.procedureTitle}</span>
            <span className="muted small">{PATIENT.discharged}</span>
            {!state.reportSent && (
              <span>
                <Pill tone="primary">New weekly report</Pill>
              </span>
            )}
          </span>
          <Icon name="chevron" />
        </Link>
        {OTHER_PATIENTS.map((p) => (
          <div key={p.name} className="card patient disabled" title="Not part of this demo">
            <span className="avatar">{p.initials}</span>
            <span className="patient-info">
              <strong>{p.name}</strong>
              <span>{p.procedure}</span>
              <span className="muted small">{p.meta}</span>
              {p.flag && (
                <span>
                  <Pill tone="amber">{p.flag}</Pill>
                </span>
              )}
            </span>
            <Icon name="chevron" />
          </div>
        ))}
        <div className="banner tone-neutral">
          <span>New instructions are recorded with the headset. They appear here after the AI decomposes them into steps.</span>
        </div>
      </Body>
    </Screen>
  );
}
