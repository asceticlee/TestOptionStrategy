"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import DateStrip from "../components/DateStrip";
import LegConfigPanel from "../components/LegConfigPanel";
import StatsStrip from "../components/StatsStrip";
import StrikeRuler from "../components/StrikeRuler";
import SurfacePanel from "../components/SurfacePanel";
import {
  computeRealSurface,
  computeStats,
  computeSurface,
  fetchExpirations,
  fetchQuoteSummary,
  fetchStrikes,
  fetchUnderlyings,
} from "../lib/api";
import type {
  Leg,
  OptionLegRequest,
  StatsResponse,
  SurfaceResponse,
} from "../lib/types";

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

function cap(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

function strategyName(legs: Leg[]): string {
  if (legs.length === 0) return "Custom Strategy";
  if (legs.length === 1) {
    return `${cap(legs[0].side)} ${cap(legs[0].right)}`;
  }
  const calls = legs.filter((l) => l.right === "call").length;
  const puts = legs.filter((l) => l.right === "put").length;
  if (
    legs.length === 2 &&
    legs[0].right === legs[1].right &&
    legs[0].side === legs[1].side
  ) {
    return `${cap(legs[0].side)} ${cap(legs[0].right)} Spread`;
  }
  if (legs.length === 4 && calls === 2 && puts === 2) {
    return "Iron Condor";
  }
  return "Custom Strategy";
}

function toLegRequests(legs: Leg[]): OptionLegRequest[] {
  return legs.map((leg) => ({
    right: leg.right,
    strike: leg.strike,
    expiration: leg.expiration,
    contracts:
      leg.side === "short" ? -Math.abs(leg.contracts) : Math.abs(leg.contracts),
  }));
}

function nearestToSpot(list: number[], spot: number | null): number {
  if (list.length === 0) return 0;
  if (spot == null) return list[Math.floor(list.length / 2)];
  let best = list[0];
  let bestDist = Math.abs(best - spot);
  for (const s of list) {
    const d = Math.abs(s - spot);
    if (d < bestDist) {
      best = s;
      bestDist = d;
    }
  }
  return best;
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
  const [stats, setStats] = useState<StatsResponse | null>(null);
  const [snapshotSpot, setSnapshotSpot] = useState<number | null>(null);
  const [previousClose, setPreviousClose] = useState<number | null>(null);
  const [view, setView] = useState<"trade" | "theoretical" | "real">("trade");
  const [realResponse, setRealResponse] = useState<SurfaceResponse | null>(null);
  const [realLoading, setRealLoading] = useState<boolean>(false);

  const loadedExpirations = useRef<Set<string>>(new Set());
  const idCounter = useRef(0);

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
          return { ...leg, strike: nearestToSpot(list, snapshotSpot) };
        }
        return leg;
      }),
    );
  }, [strikesByExpiration, snapshotSpot]);

  useEffect(() => {
    if (!symbol || !date || !time) return;
    let cancelled = false;
    const timer = setTimeout(async () => {
      try {
        const qs = await fetchQuoteSummary(symbol, date, time);
        if (!cancelled) {
          setSnapshotSpot(qs.spot > 0 ? qs.spot : null);
          setPreviousClose(qs.previousClose > 0 ? qs.previousClose : null);
        }
      } catch {
        if (!cancelled) {
          setSnapshotSpot(null);
          setPreviousClose(null);
        }
      }
    }, 400);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [symbol, date, time]);

  useEffect(() => {
    const validLegs = legs.filter((l) => l.expiration && l.strike > 0);
    if (validLegs.length === 0) {
      setStats(null);
      return;
    }
    let cancelled = false;
    const timer = setTimeout(async () => {
      try {
        const result = await computeStats({
          symbol,
          snapshotDate: date,
          snapshotTime: time,
          spotShares: 0,
          timeStepMinutes,
          spotSamples,
          spotRangePercent,
          legs: toLegRequests(validLegs),
        });
        if (!cancelled) setStats(result);
      } catch {
        if (!cancelled) setStats(null);
      }
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [symbol, date, time, legs, timeStepMinutes, spotSamples, spotRangePercent]);

  const addLeg = useCallback(() => {
    const exp =
      (legs.length > 0 && legs[legs.length - 1].expiration) ||
      availableExpirations[0] ||
      "";
    loadStrikes(exp);
    const defaultStrike = nearestToSpot(strikesByExpiration[exp] ?? [], snapshotSpot);
    const nextId = idCounter.current++;
    setLegs((prev) => [
      ...prev,
      {
        id: nextId,
        side: "long",
        right: "call",
        expiration: exp,
        strike: defaultStrike,
        contracts: 1,
      } as Leg,
    ]);
  }, [legs, availableExpirations, loadStrikes, strikesByExpiration, snapshotSpot]);

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
        legs: toLegRequests(legs),
      });
      setResponse(result);
      setView("theoretical");
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

  const handleComputeReal = useCallback(async () => {
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
    setRealLoading(true);
    try {
      const result = await computeRealSurface({
        symbol,
        snapshotDate: date,
        snapshotTime: time,
        spotShares: 0,
        timeStepMinutes,
        spotSamples,
        spotRangePercent,
        legs: toLegRequests(legs),
      });
      setRealResponse(result);
      setView("real");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setRealLoading(false);
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

  const spotChange =
    snapshotSpot != null && previousClose != null
      ? snapshotSpot - previousClose
      : null;
  const spotChangePct =
    spotChange != null && previousClose
      ? (spotChange / previousClose) * 100
      : null;

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

  const realValueData = realResponse
    ? {
        x: realResponse.spotPrices,
        y: realResponse.timestamps,
        z: realResponse.valueSurface,
        spotLine: realResponse.spotLineValue,
      }
    : null;
  const realDeltaData = realResponse
    ? {
        x: realResponse.spotPrices,
        y: realResponse.timestamps,
        z: realResponse.deltaSurface,
        spotLine: realResponse.spotLineDelta,
      }
    : null;
  const realGammaData = realResponse
    ? {
        x: realResponse.spotPrices,
        y: realResponse.timestamps,
        z: realResponse.gammaSurface,
        spotLine: realResponse.spotLineGamma,
      }
    : null;

  return (
    <div className="container">
      <div className="tabs">
        <button
          className={`tab ${view === "trade" ? "active" : ""}`}
          onClick={() => setView("trade")}
        >
          Trade Setup
        </button>
        <button
          className={`tab ${view === "theoretical" ? "active" : ""}`}
          onClick={() => setView("theoretical")}
        >
          3D Theoretical
        </button>
        <button
          className={`tab ${view === "real" ? "active" : ""}`}
          onClick={() => setView("real")}
        >
          3D Real
        </button>
      </div>

      {error && <div className="error">{error}</div>}

      {view === "trade" && (
        <>
          <div className="hdr-row">
        <div className="hdr-title">
          <h1>{strategyName(legs)}</h1>
          <span
            className="hdr-info"
            title="Multi-leg option P&L surface with frozen Greeks"
          >
            ?
          </span>
        </div>
        <div className="hdr-actions">
          <button className="pill">Positions ({legs.length})</button>
          <button
            className="pill primary"
            onClick={handleCompute}
            disabled={loading}
          >
            {loading ? "Computing..." : "Plot surface"}
          </button>
        </div>
      </div>

      <div className="underlying-row">
        <span className="ticker-chip">
          <input
            value={symbol}
            onChange={(e) => setSymbol(e.target.value.toUpperCase())}
          />
        </span>
        <span className="spot-price">
          {snapshotSpot != null ? `$${snapshotSpot.toFixed(2)}` : "—"}
        </span>
        {spotChange != null && spotChangePct != null && (
          <span
            className={`spot-change ${spotChange >= 0 ? "pos" : "neg"}`}
          >
            {spotChangePct >= 0 ? "+" : ""}
            {spotChangePct.toFixed(2)}%{"  "}
            {spotChange >= 0 ? "+" : "-"}${Math.abs(spotChange).toFixed(2)}
          </span>
        )}
        <span className="badge">Snapshot</span>
      </div>

      <div className="trade-date-row">
        <div className="section-label">Trade Date</div>
        <div className="field">
          <label>Time (ET)</label>
          <input
            type="time"
            value={time}
            onChange={(e) => setTime(e.target.value)}
          />
        </div>
      </div>
      <DateStrip selected={date} onSelect={setDate} />

      <StrikeRuler
        legs={legs}
        spot={snapshotSpot}
        symbol={symbol}
        spotRangePercent={spotRangePercent}
        strikesByExpiration={strikesByExpiration}
        selectedIndex={null}
        onSelect={() => {}}
        onStrikeChange={(i, s) => updateLeg(i, { strike: s })}
        onSideChange={(i, s) => updateLeg(i, { side: s })}
        onRightChange={(i, r) => updateLeg(i, { right: r })}
        onRemove={removeLeg}
      />

      <button className="pill add-leg" onClick={addLeg}>
        Add Leg +
      </button>

      {legs.map((leg, index) => (
        <LegConfigPanel
          key={leg.id}
          leg={leg}
          symbol={symbol}
          snapshotDate={date}
          snapshotTime={time}
          availableExpirations={availableExpirations}
          strikes={strikesByExpiration[leg.expiration] ?? []}
          onUpdate={(patch) => updateLeg(index, patch)}
          onChangeExpiration={(exp) => changeLegExpiration(index, exp)}
          onRemove={() => removeLeg(index)}
        />
      ))}

      <StatsStrip stats={stats} />

      <div className="sub-toolbar">
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
        </>
      )}

      {view === "theoretical" && (
        <>
          <div className="surface-header">
            <button
              className="pill primary"
              onClick={handleCompute}
              disabled={loading}
            >
              {loading ? "Computing..." : "Re-plot"}
            </button>
          </div>

          {response ? (
            <>
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
              <div className="meta">
                Snapshot spot: {response.snapshotSpot.toFixed(2)} · Risk-free
                rate: {(response.riskFreeRate * 100).toFixed(2)}% · Legs:{" "}
                {response.legs
                  .map(
                    (l) =>
                      `${l.contracts > 0 ? "+" : ""}${l.contracts} ${l.right} ${l.strike} ${l.expiration} (IV ${(l.impliedVol * 100).toFixed(1)}%)`,
                  )
                  .join(", ")}
              </div>
            </>
          ) : (
            <div className="empty-surface">
              No theoretical surface plotted yet. Go to Trade Setup and click
              &quot;Plot surface&quot;.
            </div>
          )}
        </>
      )}

      {view === "real" && (
        <>
          <div className="surface-header">
            <button
              className="pill primary"
              onClick={handleComputeReal}
              disabled={realLoading}
            >
              {realLoading
                ? "Computing..."
                : realResponse
                  ? "Re-plot Real"
                  : "Plot Real Surface"}
            </button>
          </div>

          {realResponse ? (
            <div className="charts">
              {realValueData && (
                <SurfacePanel
                  data={realValueData}
                  title="3D Real Value Surface"
                  zTitle="Position Value"
                />
              )}
              {realDeltaData && (
                <SurfacePanel
                  data={realDeltaData}
                  title="3D Real Delta Surface"
                  zTitle="Delta"
                />
              )}
              {realGammaData && (
                <SurfacePanel
                  data={realGammaData}
                  title="3D Real Gamma Surface"
                  zTitle="Gamma"
                />
              )}
            </div>
          ) : (
            <div className="empty-surface">
              Click &quot;Plot Real Surface&quot; to build the 3D chart from the
              actual observed IV over time.
            </div>
          )}
        </>
      )}
    </div>
  );
}
