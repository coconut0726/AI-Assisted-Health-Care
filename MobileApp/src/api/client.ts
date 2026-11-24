import AsyncStorage from '@react-native-async-storage/async-storage';
import { demoVersion } from '@/data/demo';
import { changedStepIds } from '@/model/plan';
import type { ActionProcedure, PlanVersion } from '@/model/types';

// Talks to the care-plan server on the Windows PC (planned, next to Tools/verification_bridge.py).
// Set EXPO_PUBLIC_SERVER_URL, e.g. http://192.168.1.20:8770, to use it. Without it the app keeps the
// plan on the phone so the screens can be tried in Expo Go before the server exists.
//
// Proposed endpoints (see docs/SCHEMA_mobile.md):
//   GET /api/patients/:id/plan                      -> PlanVersion (latest)
//   PUT /api/patients/:id/plan  { base_version, plan } -> PlanVersion (new version, or 409 if base_version is old)

export const SERVER_URL = (process.env.EXPO_PUBLIC_SERVER_URL ?? '').replace(/\/$/, '');

export class ConflictError extends Error {}

export interface PlanApi {
  readonly mode: 'server' | 'on-device';
  getPlan(patientId: string): Promise<PlanVersion>;
  savePlan(patientId: string, baseVersion: number, plan: ActionProcedure): Promise<PlanVersion>;
  reset?(patientId: string): Promise<PlanVersion>;
}

class ServerApi implements PlanApi {
  readonly mode = 'server' as const;
  constructor(private readonly base: string) {}

  private async request<T>(path: string, init?: RequestInit): Promise<T> {
    const res = await fetch(this.base + path, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } });
    if (res.status === 409) throw new ConflictError('The plan was changed somewhere else. Reload and try again.');
    if (!res.ok) throw new Error(`Server error ${res.status}: ${await res.text()}`);
    return (await res.json()) as T;
  }

  getPlan(patientId: string) {
    return this.request<PlanVersion>(`/api/patients/${encodeURIComponent(patientId)}/plan`);
  }

  savePlan(patientId: string, baseVersion: number, plan: ActionProcedure) {
    return this.request<PlanVersion>(`/api/patients/${encodeURIComponent(patientId)}/plan`, {
      method: 'PUT',
      body: JSON.stringify({ base_version: baseVersion, plan }),
    });
  }
}

class OnDeviceApi implements PlanApi {
  readonly mode = 'on-device' as const;
  private key = (patientId: string) => `ahc.plan.${patientId}`;

  async getPlan(patientId: string): Promise<PlanVersion> {
    try {
      const raw = await AsyncStorage.getItem(this.key(patientId));
      if (raw) return JSON.parse(raw) as PlanVersion;
    } catch {
      // storage unavailable: fall back to the demo plan
    }
    return this.reset(patientId);
  }

  async savePlan(patientId: string, baseVersion: number, plan: ActionProcedure): Promise<PlanVersion> {
    const current = await this.getPlan(patientId);
    if (current.version !== baseVersion) throw new ConflictError('The plan was changed somewhere else. Reload and try again.');
    const next: PlanVersion = {
      patient_id: patientId,
      version: current.version + 1,
      updated_at: new Date().toISOString(),
      updated_by: 'nurse',
      plan,
      changed_step_ids: changedStepIds(current.plan, plan),
    };
    await AsyncStorage.setItem(this.key(patientId), JSON.stringify(next)).catch(() => undefined);
    return next;
  }

  async reset(patientId: string): Promise<PlanVersion> {
    const v = demoVersion();
    await AsyncStorage.setItem(this.key(patientId), JSON.stringify(v)).catch(() => undefined);
    return v;
  }
}

export const api: PlanApi = SERVER_URL ? new ServerApi(SERVER_URL) : new OnDeviceApi();
