import { Link } from 'react-router-dom';
import { GUIDANCE_LABEL, TACTIC_LABEL } from '../data/demo';
import type { Requirement, RequirementType, Step } from '../types';
import { Pill, type Tone } from './ui';

export function StepCard({
  step,
  index,
  open,
  onToggle,
  editTo,
  tag,
}: {
  step: Step;
  index: number;
  open: boolean;
  onToggle: () => void;
  editTo?: string;
  tag?: { label: string; tone: Tone };
}) {
  return (
    <article className={tag ? 'card step highlight' : 'card step'}>
      <div className="step-head">
        <button className="step-toggle" onClick={onToggle} aria-expanded={open}>
          <span className="num">{index + 1}</span>
          <span className="step-title">{step.title}</span>
        </button>
        {tag && <Pill tone={tag.tone}>{tag.label}</Pill>}
        {editTo && (
          <Link to={editTo} className="link">
            Edit
          </Link>
        )}
      </div>
      <p className="muted small">Done when: {step.expectedState}</p>
      {open ? (
        <div className="actions">
          {step.actions.map((a) => (
            <div key={a.id} className="action">
              <p>{a.voice}</p>
              <div className="tags">
                <Pill tone="primary">{TACTIC_LABEL[a.tactic]}</Pill>
                <Pill>
                  {GUIDANCE_LABEL[a.guidanceType]}
                  {a.guidanceParam ? `: ${a.guidanceParam}` : ''}
                </Pill>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <button className="more" onClick={onToggle}>
          {step.actions.length} {step.actions.length === 1 ? 'action' : 'actions'}
        </button>
      )}
    </article>
  );
}

export const REQ_TONE: Record<RequirementType, Tone> = { Temporal: 'lav', Continuous: 'amber', State: 'primary' };

export function RequirementCard({ req, editTo, tag }: { req: Requirement; editTo?: string; tag?: { label: string; tone: Tone } }) {
  return (
    <article className={tag ? 'card req highlight' : 'card req'}>
      <div className="req-head">
        <Pill tone={REQ_TONE[req.type]}>{req.type}</Pill>
        <span className="muted xsmall grow">{req.scope}</span>
        {tag && <Pill tone={tag.tone}>{tag.label}</Pill>}
        {editTo && (
          <Link to={editTo} className="link">
            Edit
          </Link>
        )}
      </div>
      <p>{req.text}</p>
    </article>
  );
}

export function RedFlags({ text }: { text: string }) {
  return (
    <article className="card red-flags">
      <strong>Call the nurse if</strong>
      <p>{text}</p>
    </article>
  );
}
