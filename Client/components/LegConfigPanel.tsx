"use client";

import { memo, useState } from "react";
import ExpirationStrip from "./ExpirationStrip";
import type { Leg, StatsLeg } from "../lib/types";

interface LegConfigPanelProps {
  leg: Leg;
  greeks: StatsLeg | null;
  loading: boolean;
  snapshotDate: string;
  availableExpirations: string[];
  strikes: number[];
  onUpdate: (id: number, patch: Partial<Leg>) => void;
  onChangeExpiration: (id: number, exp: string) => void;
  onRemove: (id: number) => void;
}

function LegConfigPanel({
  leg,
  greeks,
  loading,
  snapshotDate,
  availableExpirations,
  strikes,
  onUpdate,
  onChangeExpiration,
  onRemove,
}: LegConfigPanelProps) {
  const [collapsed, setCollapsed] = useState(false);

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
            onRemove(leg.id);
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
            onSelect={(exp) => onChangeExpiration(leg.id, exp)}
          />
          <div className="leg-config-controls">
            <div className="field">
              <label>Side</label>
              <select
                value={leg.side}
                onChange={(e) =>
                  onUpdate(leg.id, { side: e.target.value as "long" | "short" })
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
                  onUpdate(leg.id, { right: e.target.value as "call" | "put" })
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
                onChange={(e) =>
                  onUpdate(leg.id, { strike: Number(e.target.value) })
                }
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
                onChange={(e) =>
                  onUpdate(leg.id, { contracts: Number(e.target.value) })
                }
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

export default memo(LegConfigPanel);
