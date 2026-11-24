import { useState } from 'react';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { Body, Icon, Nav, Screen } from '../../components/ui';
import { GUIDANCE_LABEL, PATIENT, TACTIC_LABEL } from '../../data/demo';
import { useDemo } from '../../store';
import type { Action, GuidanceType, Step, Tactic } from '../../types';

export default function EditStep() {
  const { stepId } = useParams();
  const { state, saveStep } = useDemo();
  const navigate = useNavigate();
  const index = state.instruction.steps.findIndex((s) => s.id === stepId);
  const original = state.instruction.steps[index];
  const [draft, setDraft] = useState<Step | undefined>(() => (original ? structuredClone(original) : undefined));
  const back = `/nurse/${PATIENT.id}/instruction`;

  if (!original || !draft) return <Navigate to={back} replace />;

  const setAction = (id: string, patch: Partial<Action>) =>
    setDraft({ ...draft, actions: draft.actions.map((a) => (a.id === id ? { ...a, ...patch } : a)) });
  const addAction = () =>
    setDraft({
      ...draft,
      actions: [...draft.actions, { id: 'a' + Date.now(), voice: '', tactic: 'Command', guidanceType: 'ContextualWidget', guidanceParam: '' }],
    });
  const removeAction = (id: string) => setDraft({ ...draft, actions: draft.actions.filter((a) => a.id !== id) });

  const onSave = () => {
    const cleaned = { ...draft, actions: draft.actions.filter((a) => a.voice.trim() !== '') };
    saveStep(cleaned);
    navigate(back, { replace: true, state: { saved: `Step ${index + 1}` } });
  };

  return (
    <Screen>
      <Nav title={`Edit Step ${index + 1}`} sub={`${PATIENT.name} · ${state.instruction.procedureTitle}`} back={back} />
      <Body>
        <div className="source">
          <span className="label">What you said in the recording</span>
          <p>“…{draft.sourceSpan}”</p>
        </div>
        <label className="field">
          <span className="label">Step title</span>
          <input value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} />
        </label>
        <label className="field">
          <span className="label">Done when (expected state)</span>
          <textarea rows={2} value={draft.expectedState} onChange={(e) => setDraft({ ...draft, expectedState: e.target.value })} />
        </label>
        {draft.actions.map((a, i) => (
          <div key={a.id} className="field">
            <div className="field-head">
              <span className="label">Action {i + 1}</span>
              <button className="icon-btn small" onClick={() => removeAction(a.id)} aria-label={`Remove action ${i + 1}`}>
                <Icon name="trash" size={16} />
              </button>
            </div>
            <textarea rows={2} value={a.voice} onChange={(e) => setAction(a.id, { voice: e.target.value })} />
            <div className="selects">
              <select className="pill-select tone-primary" value={a.tactic} onChange={(e) => setAction(a.id, { tactic: e.target.value as Tactic })} aria-label="Tactic">
                {Object.entries(TACTIC_LABEL).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </select>
              <select
                className="pill-select tone-neutral"
                value={a.guidanceType}
                onChange={(e) => setAction(a.id, { guidanceType: e.target.value as GuidanceType })}
                aria-label="Guidance type"
              >
                {Object.entries(GUIDANCE_LABEL).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </select>
              <input
                className="pill-input"
                value={a.guidanceParam}
                placeholder="parameter"
                onChange={(e) => setAction(a.id, { guidanceParam: e.target.value })}
                aria-label="Guidance parameter"
              />
            </div>
          </div>
        ))}
        <button className="link-btn" onClick={addAction}>
          <Icon name="plus" size={16} /> Add action
        </button>
      </Body>
      <footer className="action-bar">
        <button className="btn outline" onClick={() => navigate(back, { replace: true })}>
          Cancel
        </button>
        <button className="btn primary" onClick={onSave}>
          Save &amp; sync
        </button>
      </footer>
    </Screen>
  );
}
