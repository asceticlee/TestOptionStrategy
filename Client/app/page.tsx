"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import SurfacePanel from "../components/SurfacePanel";
import {
  computeSurface,
  fetchExpirations,
  fetchSpot,
  fetchStrikes,
  fetchUnderlyings,
} from "../lib/api";
import type {
  OptionLegRequest,
  SurfaceResponse,
} from "../lib/types";

interface Leg {
  side: "long" | "short";
  right: "call" | "put";
  expiration: string;
  strike: number;
  contracts: number;
}

function toDateInput(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

function lastCompletedTradingDay(): string {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  while (d.getDay() === 0 || d.getDay() === 6) {
    d.setDate(d.getDate() - 1);
  }
  return toDateInput(d);
}

export default function Page() {
  const [symbol, setSymbol] = useState<string>("SPY");
  const [date, setDate] = useState<string>(lastCompletedTradingDay());
  const [time, setTime] = useState<string>("10:00");
  const [expirations, setExpirations] = useState<string[]>([]);
  const [strikesByExpiration, setStrikesByExpiration] = useState<
    Record<string, number[]>
  >({});
  const [legs, setLegs] = useState<Leg[]>([]);
  const [timeStepMinutes, setTimeStepMinutes] = useState<number>(30);
  const [spotRangePercent, setSpotRangePercent] = useState<number>(5);
  const [spotSamples, setSpotSamples] = useState<number>(81);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string>("");
  const [response, setResponse] = useState<SurfaceResponse | null>(null);
  const [snapshotSpot, setSnapshotSpot] = useState<number | null>(null);

  const loadedExpirations = useRef<Set<string>>(new Set());

  const availableExpirations = expirations.filter((exp) => exp >= date);

  const loadStrikes = useCallback(
    (exp: string) => {
      if (!symbol || !exp || loadedExpirations.current.has(exp)) return;
      loadedExpirations.current.add(exp);
      fetchStrikes(symbol, exp)
        .then((s) => setStrikesByExpiration((prev) => ({ ...prev, [exp]: s })))
        .catch((e) => setError((e as Error).message));
    },
    [symbol],
  );

  useEffect(() => {
    (async () => {
      try {
        const symbols = await fetchUnderlyings();
        if (symbols.length > 0) setSymbol(symbols[0]);
      } catch (e) {
        setError((e as Error).message);
      }
    })();
  }, []);

  useEffect(() => {
    if (!symbol) return;
    setStrikesByExpiration({});
    loadedExpirations.current.clear();
    (async () => {
      try {
        const exps = await fetchExpirations(symbol);
        setExpirations(exps);
      } catch (e) {
        setError((e as Error).message);
      }
    })();
  }, [symbol]);

  useEffect(() => {
    if (availableExpirations.length === 0) return;
    const invalidLegs = legs.filter(
      (leg) => !availableExpirations.includes(leg.expiration),
    );
    if (invalidLegs.length === 0) return;
    const newExp = availableExpirations[0];
    loadStrikes(newExp);
    setLegs((prev) =>
      prev.map((leg) =>
        availableExpirations.includes(leg.expiration)
          ? leg
          : { ...leg, expiration: newExp },
      ),
    );
  }, [availableExpirations, legs, loadStrikes]);

  useEffect(() => {
    setLegs((prev) =>
      prev.map((leg) => {
        const list = strikesByExpiration[leg.expiration];
        if (list && list.length > 0 && !list.includes(leg.strike)) {
          return { ...leg, strike: list[0] };
        }
        return leg;
      }),
    );
  }, [strikesByExpiration]);

  useEffect(() => {
    if (!symbol || !date || !time) return;
    let cancelled = false;
    const timer = setTimeout(async () => {
      try {
        const s = await fetchSpot(symbol, date, time);
        if (!cancelled) setSnapshotSpot(s > 0 ? s : null);
      } catch {
        if (!cancelled) setSnapshotSpot(null);
      }
    }, 400);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [symbol, date, time]);

  const addLeg = useCallback(() => {
    const exp = availableExpirations[0] ?? "";
    loadStrikes(exp);
    setLegs((prev) => [
      ...prev,
      { side: "long", right: "call", expiration: exp, strike: 0, contracts: 1 },
    ]);
  }, [availableExpirations, loadStrikes]);

  const updateLeg = useCallback((index: number, patch: Partial<Leg>) => {
    setLegs((prev) =>
      prev.map((leg, i) => (i === index ? { ...leg, ...patch } : leg)),
    );
  }, []);

  const changeLegExpiration = useCallback(
    (index: number, exp: string) => {
      loadStrikes(exp);
      updateLeg(index, { expiration: exp });
    },
    [loadStrikes, updateLeg],
  );

  const removeLeg = useCallback((index: number) => {
    setLegs((prev) => prev.filter((_, i) => i !== index));
  }, []);

  const handleCompute = useCallback(async () => {
    setError("");
    if (legs.length === 0) {
      setError("Add at least one option leg.");
      return;
    }
    for (const leg of legs) {
      if (!leg.expiration) {
        setError("Each option leg needs an expiration date.");
        return;
      }
      if (!leg.strike || leg.strike <= 0) {
        setError("Each option leg needs a strike.");
        return;
      }
    }
    const legRequests: OptionLegRequest[] = legs.map((leg) => ({
      right: leg.right,
      strike: leg.strike,
      expiration: leg.expiration,
      contracts:
        leg.side === "short" ? -Math.abs(leg.contracts) : Math.abs(leg.contracts),
    }));
    setLoading(true);
    try {
      const result = await computeSurface({
        symbol,
        snapshotDate: date,
        snapshotTime: time,
        spotShares: 0,
        timeStepMinutes,
        spotSamples,
        spotRangePercent,
        legs: legRequests,
      });
      setResponse(result);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [
    symbol,
    date,
    time,
    legs,
    timeStepMinutes,
    spotSamples,
    spotRangePercent,
  ]);

  const valueData = response
    ? {
        x: response.spotPrices,
        y: response.timestamps,
        z: response.valueSurface,
        spotLine: response.spotLineValue,
      }
    : null;
  const deltaData = response
    ? {
        x: response.spotPrices,
        y: response.timestamps,
        z: response.deltaSurface,
        spotLine: response.spotLineDelta,
      }
    : null;
  const gammaData = response
    ? {
        x: response.spotPrices,
        y: response.timestamps,
        z: response.gammaSurface,
        spotLine: response.spotLineGamma,
      }
    : null;

  return (
    <div className="container">
      <h1>Option P&amp;L Surface (Greeks frozen)</h1>

      <div className="controls">
        <div className="controls-row">
          <div className="field">
            <label>Symbol</label>
            <input
              value={symbol}
              onChange={(e) => setSymbol(e.target.value.toUpperCase())}
            />
          </div>
          <div className="field">
            <label>Date</label>
            <input
              type="date"
              value={date}
              onChange={(e) => setDate(e.target.value)}
            />
          </div>
          <div className="field">
            <label>Time (ET)</label>
            <input
              type="time"
              value={time}
              onChange={(e) => setTime(e.target.value)}
            />
          </div>
          <div className="field">
            <label>Spot @ snapshot</label>
            <div className="spot-display">
              {snapshotSpot != null ? snapshotSpot.toFixed(2) : "—"}
            </div>
          </div>
          <div className="field">
            <label>Time step (min)</label>
            <select
              value={timeStepMinutes}
              onChange={(e) => setTimeStepMinutes(Number(e.target.value))}
            >
              {[15, 30, 60].map((v) => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label>Spot range (%)</label>
            <input
              type="number"
              min={1}
              max={30}
              value={spotRangePercent}
              onChange={(e) => setSpotRangePercent(Number(e.target.value))}
            />
          </div>
          <div className="field">
            <label>Spot samples</label>
            <input
              type="number"
              min={10}
              max={201}
              value={spotSamples}
              onChange={(e) => setSpotSamples(Number(e.target.value))}
            />
          </div>
        </div>

        <div className="legs">
          <div style={{ marginBottom: 8, fontWeight: 600 }}>Option legs</div>
          {legs.map((leg, index) => (
            <div className="leg-row" key={index}>
              <select
                value={leg.side}
                onChange={(e) =>
                  updateLeg(index, { side: e.target.value as "long" | "short" })
                }
              >
                <option value="long">Long</option>
                <option value="short">Short</option>
              </select>
              <select
                value={leg.right}
                onChange={(e) =>
                  updateLeg(index, { right: e.target.value as "call" | "put" })
                }
              >
                <option value="call">Call</option>
                <option value="put">Put</option>
              </select>
              <select
                value={leg.expiration}
                onChange={(e) => changeLegExpiration(index, e.target.value)}
              >
                {availableExpirations.map((exp) => (
                  <option key={exp} value={exp}>
                    {exp}
                  </option>
                ))}
              </select>
              <select
                value={leg.strike}
                onChange={(e) => updateLeg(index, { strike: Number(e.target.value) })}
              >
                {(strikesByExpiration[leg.expiration] ?? []).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
              <input
                type="number"
                min={1}
                value={leg.contracts}
                onChange={(e) => updateLeg(index, { contracts: Number(e.target.value) })}
                style={{ width: 90 }}
              />
              <button
                className="secondary"
                onClick={() => removeLeg(index)}
              >
                Remove
              </button>
            </div>
          ))}
          <button className="secondary" onClick={addLeg}>
            + Add leg
          </button>
        </div>

        <div className="controls-row" style={{ marginTop: 12 }}>
          <button onClick={handleCompute} disabled={loading}>
            {loading ? "Computing..." : "Plot surface"}
          </button>
        </div>
      </div>

      {error && <div className="error">{error}</div>}

      {response && (
        <div className="meta">
          Snapshot spot: {response.snapshotSpot.toFixed(2)} · Risk-free rate:{" "}
          {(response.riskFreeRate * 100).toFixed(2)}% · Legs:{" "}
          {response.legs
            .map(
              (l) =>
                `${l.contracts > 0 ? "+" : ""}${l.contracts} ${l.right} ${l.strike} ${l.expiration} (IV ${(l.impliedVol * 100).toFixed(1)}%)`,
            )
            .join(", ")}
        </div>
      )}

      <div className="charts">
        {valueData && (
          <SurfacePanel
            data={valueData}
            title="3D Investment Value Surface"
            zTitle="Position Value"
          />
        )}
        {deltaData && (
          <SurfacePanel
            data={deltaData}
            title="3D Investment Delta Surface"
            zTitle="Delta"
          />
        )}
        {gammaData && (
          <SurfacePanel
            data={gammaData}
            title="3D Investment Gamma Surface"
            zTitle="Gamma"
          />
        )}
      </div>
    </div>
  );
}
