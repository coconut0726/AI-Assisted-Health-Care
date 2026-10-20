import { useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { RedFlags, RequirementCard, StepCard } from '../../components/instruction';
import { Banner, Body, Screen, Segmented } from '../../components/ui';
import { PATIENT } from '../../data/demo';
import { useDemo } from '../../store';
import PatientHeader from './PatientHeader';

export default function NurseInstruction() {
  const { state } = useDemo();
  const [params] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const view = params.get('view') === 'checklist' ? 'checklist' : 'steps';
  const saved = (location.state as { saved?: string } | null)?.saved;
  const edited = new Set(state.changes.map((c) => c.id));
  const [open, setOpen] = useState<Set<string>>(() => new Set(['s2']));
  const base = `/nurse/${PATIENT.id}`;

  const toggle = (id: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const setView = (v: 'steps' | 'checklist') => {
    // Changing view drops the "saved" banner, which lives in navigation state.
    navigate({ search: v === 'checklist' ? '?view=checklist' : '' }, { replace: true, state: null });
  };

  return (
    <Screen>
      <PatientHeader tab="instruction" />
      <Body>
        <Segmented
          items={[
            { label: 'Care Steps', active: view === 'steps', onSelect: () => setView('steps') },
            { label: 'Oversight Checklist', active: view === 'checklist', onSelect: () => setView('checklist') },
          ]}
        />
        {saved && (
          <Banner tone="green">
            ✓ Saved and synced to {PATIENT.firstName}’s app. They will see “{saved} updated”.
          </Banner>
        )}
        {view === 'steps' ? (
          state.instruction.steps.map((s, i) => (
            <StepCard
              key={s.id}
              step={s}
              index={i}
              open={open.has(s.id)}
              onToggle={() => toggle(s.id)}
              editTo={`${base}/steps/${s.id}/edit`}
              tag={edited.has(s.id) ? { label: 'Edited', tone: 'lav' } : undefined}
            />
          ))
        ) : (
          <>
            <p className="muted small">What the AI checks in each home session</p>
            {state.instruction.requirements.map((r) => (
              <RequirementCard
                key={r.id}
                req={r}
                editTo={`${base}/requirements/${r.id}/edit`}
                tag={edited.has(r.id) ? { label: 'Edited', tone: 'lav' } : undefined}
              />
            ))}
            <RedFlags text={state.instruction.redFlags} />
          </>
        )}
      </Body>
    </Screen>
  );
}
