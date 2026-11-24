import { useState } from 'react';
import { RedFlags, RequirementCard, StepCard } from '../../components/instruction';
import { Banner, Body, BottomTabs, Nav, Screen, Segmented } from '../../components/ui';
import { NURSE } from '../../data/demo';
import { formatTime, patientMessages, useDemo } from '../../store';

export default function PatientInstruction() {
  const { state, markInstructionSeen } = useDemo();
  const [view, setView] = useState<'steps' | 'checklist'>('steps');
  const unseen = state.changes.filter((c) => c.version > state.patientSeenVersion);
  const changed = new Set(unseen.map((c) => c.id));
  const [open, setOpen] = useState<Set<string>>(() => new Set(unseen.length ? unseen.map((c) => c.id) : ['s1']));
  const unread = patientMessages(state).length > state.patientSeenMessages;

  const toggle = (id: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const changedNames = state.instruction.steps
    .map((s, i) => (changed.has(s.id) ? `Step ${i + 1}` : null))
    .filter(Boolean)
    .concat(unseen.some((c) => c.kind === 'requirement') ? ['Oversight Checklist'] : []);
  const lastChange = unseen[unseen.length - 1];
  const tag = { label: 'Updated', tone: 'lav' as const };

  return (
    <Screen>
      <Nav title="My Care Plan" sub={`${state.instruction.procedureTitle} · from ${NURSE.name}`} large />
      <Body>
        <Segmented
          items={[
            { label: 'Care Steps', active: view === 'steps', onSelect: () => setView('steps') },
            { label: 'Oversight Checklist', active: view === 'checklist', onSelect: () => setView('checklist') },
          ]}
        />
        {lastChange && (
          <Banner
            tone="lav"
            action={
              <button className="banner-btn" onClick={markInstructionSeen}>
                Got it
              </button>
            }
          >
            {NURSE.name} updated your instructions today at {formatTime(lastChange.at)} — {[...new Set(changedNames)].join(', ')} changed.
          </Banner>
        )}
        {view === 'steps' ? (
          state.instruction.steps.map((s, i) => (
            <StepCard key={s.id} step={s} index={i} open={open.has(s.id)} onToggle={() => toggle(s.id)} tag={changed.has(s.id) ? tag : undefined} />
          ))
        ) : (
          <>
            <p className="muted small">What to keep right every time</p>
            {state.instruction.requirements.map((r) => (
              <RequirementCard key={r.id} req={r} tag={changed.has(r.id) ? tag : undefined} />
            ))}
            <RedFlags text={state.instruction.redFlags} />
          </>
        )}
      </Body>
      <BottomTabs active="instruction" unread={unread} />
    </Screen>
  );
}
