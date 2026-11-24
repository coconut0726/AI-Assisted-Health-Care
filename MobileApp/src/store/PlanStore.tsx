import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api } from '@/api/client';
import { PATIENT } from '@/data/demo';
import type { ActionProcedure, PlanVersion } from '@/model/types';

interface PlanState {
  current: PlanVersion | null;
  error: string | null;
  loading: boolean;
  mode: 'server' | 'on-device';
  reload: () => Promise<void>;
  /** Saves a new version. Throws with a message the screen can show. */
  save: (plan: ActionProcedure) => Promise<PlanVersion>;
  reset: () => Promise<void>;
}

const PlanContext = createContext<PlanState | null>(null);

export function PlanProvider({ children }: { children: ReactNode }) {
  const [current, setCurrent] = useState<PlanVersion | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setCurrent(await api.getPlan(PATIENT.id));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let alive = true;
    api.getPlan(PATIENT.id).then(
      (v) => {
        if (!alive) return;
        setCurrent(v);
        setLoading(false);
      },
      (e: unknown) => {
        if (!alive) return;
        setError(e instanceof Error ? e.message : String(e));
        setLoading(false);
      },
    );
    return () => {
      alive = false;
    };
  }, []);

  const save = useCallback(
    async (plan: ActionProcedure) => {
      if (!current) throw new Error('The plan has not loaded yet.');
      const next = await api.savePlan(PATIENT.id, current.version, plan);
      setCurrent(next);
      return next;
    },
    [current],
  );

  const reset = useCallback(async () => {
    if (api.reset) setCurrent(await api.reset(PATIENT.id));
  }, []);

  const value = useMemo(
    () => ({ current, error, loading, mode: api.mode, reload, save, reset }),
    [current, error, loading, reload, save, reset],
  );
  return <PlanContext.Provider value={value}>{children}</PlanContext.Provider>;
}

export function usePlan(): PlanState {
  const ctx = useContext(PlanContext);
  if (!ctx) throw new Error('usePlan must be used inside PlanProvider');
  return ctx;
}
