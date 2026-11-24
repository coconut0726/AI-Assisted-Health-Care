import type { Finding, Verdict, WeeklyReport } from '../types';
import { Pill, type Tone } from './ui';

const VERDICT_TONE: Record<Verdict, Tone> = { Satisfied: 'green', Violated: 'red', Unknown: 'amber' };

export function Stats({ counts }: { counts: Record<Verdict, number> }) {
  return (
    <div className="stats">
      {(['Satisfied', 'Violated', 'Unknown'] as Verdict[]).map((v) => (
        <div key={v} className={`stat tone-${VERDICT_TONE[v]}`}>
          <strong>{counts[v]}</strong>
          <span>{v}</span>
        </div>
      ))}
    </div>
  );
}

export function ReportCard({
  report,
  audience,
  sent,
  onView,
  onSend,
  patientName,
}: {
  report: WeeklyReport;
  audience: 'nurse' | 'patient';
  sent: boolean;
  onView: () => void;
  onSend?: () => void;
  patientName: string;
}) {
  const toFix = report.counts.Violated;
  return (
    <article className="report-card">
      <div className="report-head">
        <span className="glass-pill">{audience === 'nurse' ? 'AI weekly report' : 'Weekly report'}</span>
        <span className="report-status">
          {audience === 'patient' ? 'Shared by Nurse Lee' : sent ? `✓ Sent to ${patientName}` : 'Only you can see this'}
        </span>
      </div>
      <h3>
        {report.title} · {report.range}
      </h3>
      <p className="report-sub">
        {audience === 'nurse'
          ? `${report.sessionsRecorded} daily dressing changes recorded · ${report.findings.length + report.counts.Satisfied} requirements checked`
          : `${report.sessionsRecorded} dressing changes recorded. ${toFix} things to fix.`}
      </p>
      <Stats counts={report.counts} />
      <div className="report-actions">
        <button className="btn glass" onClick={onView}>
          View report
        </button>
        {audience === 'nurse' && !sent && onSend && (
          <button className="btn white" onClick={onSend}>
            Send to patient
          </button>
        )}
      </div>
    </article>
  );
}

export function FindingCard({ finding, audience }: { finding: Finding; audience: 'nurse' | 'patient' }) {
  return (
    <article className="card finding">
      <div className="req-head">
        <Pill tone={VERDICT_TONE[finding.verdict]}>{finding.verdict}</Pill>
        <span className="muted xsmall">{finding.frequency}</span>
      </div>
      <h4>{finding.requirement}</h4>
      {finding.evidence.length > 0 && (
        <div className="evidence">
          {finding.evidence.map((e) => (
            <div key={e} className="frame" role="img" aria-label={`Frame capture ${e}`}>
              <span>{e}</span>
            </div>
          ))}
        </div>
      )}
      <p>{audience === 'nurse' ? finding.nurseNote : finding.patientNote}</p>
    </article>
  );
}
