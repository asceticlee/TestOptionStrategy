"use client";

import type { Leg, StatsLeg } from "../lib/types";

interface PositionGreeksProps {
  legs: Leg[];
  greeksById: Record<number, StatsLeg | null>;
}

interface Combined {
  delta: number;
  gamma: number;
  theta: number;
  vega: number;
  bid: number;
  ask: number;
}

function combine(
  legs: Leg[],
  greeksById: Record<number, StatsLeg | null>,
): Combined | null {
  let delta = 0;
  let gamma = 0;
  let theta = 0;
  let vega = 0;
  let bid = 0;
  let ask = 0;
  let any = false;
  for (const leg of legs) {
    const g = greeksById[leg.id];
    if (!g) continue;
    any = true;
    const qty = leg.contracts;
    const signed = leg.side === "short" ? -qty : qty;
    delta += signed * g.delta;
    gamma += signed * g.gamma;
    theta += signed * g.theta;
    vega += signed * g.vega;
    if (leg.side === "long") {
      bid += qty * g.bid;
      ask += qty * g.ask;
    } else {
      bid -= qty * g.ask;
      ask -= qty * g.bid;
    }
  }
  if (!any) return null;
  return { delta, gamma, theta, vega, bid, ask };
}

export default function PositionGreeks({ legs, greeksById }: PositionGreeksProps) {
  const combined = combine(legs, greeksById);
  return (
    <div className="position-greeks">
      <span className="position-greeks-label">Position</span>
      {combined ? (
        <>
          <span className="leg-greek">
            <i>Delta</i>
            {combined.delta.toFixed(3)}
          </span>
          <span className="leg-greek">
            <i>Gamma</i>
            {combined.gamma.toFixed(4)}
          </span>
          <span className="leg-greek">
            <i>Theta</i>
            {combined.theta.toFixed(4)}
          </span>
          <span className="leg-greek">
            <i>Vega</i>
            {combined.vega.toFixed(2)}
          </span>
          <span className="leg-greek">
            <i>Bid</i>
            {combined.bid.toFixed(2)}
          </span>
          <span className="leg-greek">
            <i>Ask</i>
            {combined.ask.toFixed(2)}
          </span>
        </>
      ) : (
        <span className="leg-greeks-empty">—</span>
      )}
    </div>
  );
}
