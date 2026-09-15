"use client";

import { useEffect, useState } from "react";
import ExpirationStrip from "./ExpirationStrip";
import { computeLegGreeks } from "../lib/api";
import type { Leg, StatsLeg } from "../lib/types";

interface LegConfigPanelProps {
  leg: Leg;
  symbol: string;
  snapshotDate: string;
  snapshotTime: string;
  availableExpirations: string[];
  strikes: number[];
  onUpdate: (patch: Partial<Leg>) => void;
  onChangeExpiration: (exp: string) => void;
  onRemove: () => void;
}

export default function LegConfigPanel({
  leg,
  symbol,
  snapshotDate,
  snapshotTime,
  availableExpirations,
  strikes,
  onUpdate,
  onChangeExpiration,
  onRemove,
}: LegConfigPanelProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [greeks, setGreeks] = useState<StatsLeg | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const right = leg.right;
    const strike = leg.strike;
    const expiration = leg.expiration;
    if (!expiration || !strike || strike <= 0) {
      setGreeks(null);
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    const timer = setTimeout(async () => {
      try {
        const g = await computeLegGreeks({
          symbol,
          snapshotDate,
          snapshotTime,
          leg: { right, strike, expiration, contracts: 1 },
        });
        if (!cancelled) setGreeks(g);
      } catch {
        if (!cancelled) setGreeks(null);
      } finally {
        if (!cancelled) setLoading(false);
      }
    }, 200);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [symbol, snapshotDate, snapshotTime, leg.right, leg.strike, leg.expiration]);

  return (
    <div className="leg-config-panel">
      <div
        className={`leg-config-header ${collapsed ? "collapsed" : ""}`}
        onClick={() => setCollapsed((c) => !c)}
      >
        <span className={`chevron ${collapsed ? "collapsed" : ""}`}>▾</span>
        <span className={`leg-badge ${leg.right}`}>
          {leg.side === "long" ? "Long" : "Short"} {leg.strike}
          {leg.right === "call" ? "C" : "P"} · {leg.contracts}
        </span>
        {collapsed && (
          <span className="leg-summary">
            <span className="leg-summary-item">Exp {leg.expiration}</span>
            {loading ? (
              <span className="leg-summary-item">…</span>
            ) : greeks ? (
              <>
                <span className="leg-summary-item">
                  Δ {greeks.delta.toFixed(3)}
                </span>
                <span className="leg-summary-item">
                  Γ {greeks.gamma.toFixed(4)}
                </span>
                <span className="leg-summary-item">
                  Θ {greeks.theta.toFixed(4)}
                </span>
                <span className="leg-summary-item">
                  IV {(greeks.impliedVol * 100).toFixed(1)}%
                </span>
                <span className="leg-summary-item">
                  {greeks.bid.toFixed(2)} / {greeks.ask.toFixed(2)}
                </span>
              </>
            ) : null}
          </span>
        )}
        <button
          className="secondary"
          onClick={(e) => {
            e.stopPropagation();
            onRemove();
          }}
        >
          Remove
        </button>
      </div>

      {!collapsed && (
        <>
          <ExpirationStrip
            availableExpirations={availableExpirations}
            snapshotDate={snapshotDate}
            selected={leg.expiration}
            onSelect={onChangeExpiration}
          />
          <div className="leg-config-controls">
            <div className="field">
              <label>Side</label>
              <select
                value={leg.side}
                onChange={(e) =>
                  onUpdate({ side: e.target.value as "long" | "short" })
                }
              >
                <option value="long">Long</option>
                <option value="short">Short</option>
              </select>
            </div>
            <div className="field">
              <label>Call / Put</label>
              <select
                value={leg.right}
                onChange={(e) =>
                  onUpdate({ right: e.target.value as "call" | "put" })
                }
              >
                <option value="call">Call</option>
                <option value="put">Put</option>
              </select>
            </div>
            <div className="field">
              <label>Strike</label>
              <select
                value={leg.strike}
                onChange={(e) => onUpdate({ strike: Number(e.target.value) })}
              >
                {strikes.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label>Qty</label>
              <input
                type="number"
                min={1}
                value={leg.contracts}
                onChange={(e) => onUpdate({ contracts: Number(e.target.value) })}
                style={{ width: 80 }}
              />
            </div>
          </div>
          <div className="leg-greeks">
            {!greeks ? (
              <span className="leg-greeks-empty">
                {loading ? "Loading greeks…" : "—"}
              </span>
            ) : loading ? (
              <span className="leg-greeks-empty">Recalculating…</span>
            ) : (
              <>
                <span className="leg-greek">
                  <i>Bid</i>
                  {greeks.bid.toFixed(2)}
                </span>
                <span className="leg-greek">
                  <i>Ask</i>
                  {greeks.ask.toFixed(2)}
                </span>
                <span className="leg-greek">
                  <i>Delta</i>
                  {greeks.delta.toFixed(3)}
                </span>
                <span className="leg-greek">
                  <i>Gamma</i>
                  {greeks.gamma.toFixed(4)}
                </span>
                <span className="leg-greek">
                  <i>Theta</i>
                  {greeks.theta.toFixed(4)}
                </span>
                <span className="leg-greek">
                  <i>Vega</i>
                  {greeks.vega.toFixed(2)}
                </span>
                <span className="leg-greek">
                  <i>IV</i>
                  {(greeks.impliedVol * 100).toFixed(1)}%
                </span>
              </>
            )}
          </div>
        </>
      )}
    </div>
  );
}
