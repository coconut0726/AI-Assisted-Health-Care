import { useState } from 'react';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { Body, Nav, Screen, Segmented } from '../../components/ui';
import { PATIENT, REQUIREMENT_TYPES } from '../../data/demo';
import { useDemo } from '../../store';
import type { Requirement } from '../../types';

const TYPE_HINT = {
  Temporal: 'Something must happen before or after something else.',
  Continuous: 'A condition that must hold for a whole stretch of the procedure.',
  State: 'How things must look at a given point.',
};

export default function EditRequirement() {
  const { reqId } = useParams();
  const { state, saveRequirement } = useDemo();
  const navigate = useNavigate();
  const original = state.instruction.requirements.find((r) => r.id === reqId);
  const [draft, setDraft] = useState<Requirement | undefined>(() => (original ? { ...original } : undefined));
  const back = `/nurse/${PATIENT.id}/instruction?view=checklist`;

  if (!original || !draft) return <Navigate to={back} replace />;

  const onSave = () => {
    saveRequirement(draft);
    navigate(back, { replace: true, state: { saved: 'Oversight Checklist' } });
  };

  return (
    <Screen>
      <Nav title="Edit requirement" sub={`${PATIENT.name} · Oversight Checklist`} back={back} />
      <Body>
        <div className="field">
          <span className="label">Type</span>
          <Segmented items={REQUIREMENT_TYPES.map((t) => ({ label: t, active: draft.type === t, onSelect: () => setDraft({ ...draft, type: t }) }))} />
          <span className="muted xsmall">{TYPE_HINT[draft.type]}</span>
        </div>
        <label className="field">
          <span className="label">Requirement</span>
          <textarea rows={3} value={draft.text} onChange={(e) => setDraft({ ...draft, text: e.target.value })} />
        </label>
        <label className="field">
          <span className="label">Applies to</span>
          <input value={draft.scope} onChange={(e) => setDraft({ ...draft, scope: e.target.value })} />
        </label>
      </Body>
      <footer className="action-bar">
        <button className="btn outline" onClick={() => navigate(back, { replace: true })}>
          Cancel
        </button>
        <button className="btn primary" onClick={onSave} disabled={!draft.text.trim()}>
          Save &amp; sync
        </button>
      </footer>
    </Screen>
  );
}
