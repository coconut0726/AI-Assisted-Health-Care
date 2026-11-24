import { Nav, Tabs } from '../../components/ui';
import { PATIENT } from '../../data/demo';
import { useDemo } from '../../store';

export default function PatientHeader({ tab }: { tab: 'instruction' | 'feedback' }) {
  const { state } = useDemo();
  const base = `/nurse/${PATIENT.id}`;
  return (
    <>
      <Nav title={PATIENT.name} sub={`${state.instruction.procedureTitle} · ${state.instruction.frequency}`} back="/nurse" />
      <Tabs
        items={[
          { label: 'Instruction', to: `${base}/instruction`, active: tab === 'instruction' },
          { label: 'Home Care Feedback', to: `${base}/feedback`, active: tab === 'feedback' },
        ]}
      />
    </>
  );
}
