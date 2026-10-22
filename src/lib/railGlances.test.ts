import { describe, expect, it } from 'vitest';
import { CLOUD_SECTION, GLANCED, azureGlance, followsGlance, machinesGlance } from './railGlances';

describe('every Cloud infrastructure card calculates a status', () => {
  it('glances every card the rail files under Cloud infrastructure, in its order, and no other', () => {
    // A card added under Cloud infrastructure without a status fails here before it ships.
    expect(GLANCED.map((g) => g.slug)).toEqual([...CLOUD_SECTION]);
    expect(CLOUD_SECTION).toHaveLength(11);
  });

  it("reads Azure's view of the container from its state, the certificate and the SQL store", () => {
    const running = { available: true, group_state: 'Running' };
    expect(azureGlance(running)).toEqual({ tone: 'fine', headline: 'Running' });
    expect(azureGlance({ ...running, group_state: 'Stopped' }).tone).toBe('red');
    expect(
      azureGlance({ ...running, hourly: { certificate: { days_left: 20 }, sql_storage: null } })
        .tone
    ).toBe('amber');
    expect(
      azureGlance({ ...running, hourly: { certificate: null, sql_storage: { share: 0.96 } } }).tone
    ).toBe('red');
    expect(azureGlance({ available: false })).toEqual({ tone: 'unread', headline: null });
  });

  it('reads the machines from the newest memory against the limit, and leaves an unread store out', () => {
    const report = (mb: number, sql: number[] = []) => ({
      container: {
        memory_limit_mb: 1_000,
        samples: [{ working_set_mb: 100 }, { working_set_mb: mb }],
      },
      relational: { available: sql.length > 0, rows: sql.map((m) => ({ memory_percent: m })) },
    });
    expect(machinesGlance(report(300))).toEqual({ tone: 'fine', headline: '30 %' });
    expect(machinesGlance(report(850)).tone).toBe('amber');
    expect(machinesGlance(report(300, [97])).tone).toBe('red');
    expect(
      machinesGlance({
        container: { memory_limit_mb: 1_000, samples: [] },
        relational: { available: false, rows: [] },
      })
    ).toEqual({ tone: 'unread', headline: null });
  });

  it("takes memory against traffic's tone from the server's verdict", () => {
    const report = (
      tone: 'good' | 'info' | 'warning' | 'alarm',
      points = [{ working_set_mb: 250 }]
    ) => ({
      memory_limit_mb: 1_000,
      points,
      verdict: { tone },
    });
    expect(followsGlance(report('good'))).toEqual({ tone: 'fine', headline: '25 %' });
    expect(followsGlance(report('info')).tone).toBe('fine');
    expect(followsGlance(report('alarm')).tone).toBe('red');
    expect(followsGlance(report('good', []))).toEqual({ tone: 'unread', headline: null });
  });
});
