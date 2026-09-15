"use client";

import { useEffect, useRef, useState } from "react";
import type { Leg } from "../lib/types";

interface StrikeRulerProps {
  legs: Leg[];
  spot: number | null;
  symbol: string;
  spotRangePercent: number;
  strikesByExpiration: Record<string, number[]>;
  selectedIndex: number | null;
  onSelect: (index: number) => void;
  onStrikeChange: (index: number, strike: number) => void;
  onSideChange: (index: number, side: "long" | "short") => void;
  onRightChange: (index: number, right: "call" | "put") => void;
  onRemove: (index: number) => void;
}

const LANE_HEIGHT = 28;
const PILL_WIDTH = 60;

export default function StrikeRuler({
  legs,
  spot,
  symbol,
  spotRangePercent,
  strikesByExpiration,
  selectedIndex,
  onSelect,
  onStrikeChange,
  onSideChange,
  onRightChange,
  onRemove,
}: StrikeRulerProps) {
  const trackRef = useRef<HTMLDivElement>(null);
  const [trackWidth, setTrackWidth] = useState(0);
  const legsRef = useRef(legs);
  legsRef.current = legs;
  const dragRef = useRef<{
    index: number;
    startX: number;
    moved: boolean;
  } | null>(null);
  const lastClickRef = useRef<{ index: number; time: number } | null>(null);

  useEffect(() => {
    const el = trackRef.current;
    if (!el) return;
    const update = () => setTrackWidth(el.offsetWidth);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const allStrikes = legs.map((l) => l.strike);
  const center =
    spot != null
      ? spot
      : allStrikes.length > 0
        ? (Math.min(...allStrikes) + Math.max(...allStrikes)) / 2
        : 0;
  let halfWidth = center * (spotRangePercent / 100);
  if (!(halfWidth > 0)) {
    halfWidth = center * 0.05 || 1;
  }
  const min = center - halfWidth;
  const max = center + halfWidth;

  const fraction = (s: number) => (s - min) / (max - min);
  const clampFraction = (f: number) => Math.min(Math.max(f, 0), 1);

  const range = max - min;
  let tickStep = 1;
  if (range > 100) tickStep = 25;
  else if (range > 50) tickStep = 10;
  else if (range > 20) tickStep = 5;

  const ticks: number[] = [];
  for (
    let t = Math.ceil(min / tickStep) * tickStep;
    t <= max;
    t += tickStep
  ) {
    ticks.push(t);
  }

  const gapFrac = trackWidth > 0 ? PILL_WIDTH / trackWidth : 0.05;
  const halfGap = gapFrac / 2;

  const lanesByIndex = new Map<number, number>();
  for (const side of ["long", "short"] as const) {
    const sorted = legs
      .map((l, i) => ({ i, strike: l.strike }))
      .filter((x) => legs[x.i].side === side)
      .sort((a, b) => a.strike - b.strike);
    const laneRightEdge: number[] = [];
    for (const { i, strike } of sorted) {
      const f = fraction(strike);
      let lane = 0;
      while (lane < laneRightEdge.length && laneRightEdge[lane] > f - halfGap) {
        lane++;
      }
      laneRightEdge[lane] = f + halfGap;
      lanesByIndex.set(i, lane);
    }
  }

  const strikesForLeg = (index: number): number[] => {
    const exp = legsRef.current[index]?.expiration;
    return (exp && strikesByExpiration[exp]) || [];
  };

  const snapToNearest = (raw: number, available: number[]): number => {
    if (available.length === 0) return Math.round(raw * 2) / 2;
    let best = available[0];
    let bestDist = Math.abs(raw - best);
    for (const s of available) {
      const d = Math.abs(raw - s);
      if (d < bestDist) {
        best = s;
        bestDist = d;
      }
    }
    return best;
  };

  const onWindowPointerMove = (e: globalThis.PointerEvent) => {
    if (!dragRef.current) return;
    const el = trackRef.current;
    if (!el) return;
    const index = dragRef.current.index;
    const rect = el.getBoundingClientRect();
    const frac = Math.min(Math.max((e.clientX - rect.left) / rect.width, 0), 1);
    const rawStrike = min + frac * (max - min);
    const snapped = snapToNearest(rawStrike, strikesForLeg(index));
    if (Math.abs(e.clientX - dragRef.current.startX) > 3) {
      dragRef.current.moved = true;
      if (snapped !== legsRef.current[index].strike) {
        onStrikeChange(index, snapped);
      }
    }
    const midY = rect.top + rect.height / 2;
    const currentSide = legsRef.current[index].side;
    if (e.clientY < midY - 12 && currentSide !== "long") {
      onSideChange(index, "long");
    } else if (e.clientY > midY + 12 && currentSide !== "short") {
      onSideChange(index, "short");
    }
  };

  const onWindowPointerUp = () => {
    const d = dragRef.current;
    dragRef.current = null;
    window.removeEventListener("pointermove", onWindowPointerMove);
    window.removeEventListener("pointerup", onWindowPointerUp);
    if (!d) return;
    if (d.moved) {
      lastClickRef.current = null;
      return;
    }
    const now = Date.now();
    if (
      lastClickRef.current &&
      lastClickRef.current.index === d.index &&
      now - lastClickRef.current.time < 350
    ) {
      const currentRight = legsRef.current[d.index].right;
      onRightChange(d.index, currentRight === "call" ? "put" : "call");
      lastClickRef.current = null;
    } else {
      lastClickRef.current = { index: d.index, time: now };
    }
  };

  const onPillPointerDown = (
    e: React.PointerEvent<HTMLButtonElement>,
    index: number,
  ) => {
    e.preventDefault();
    onSelect(index);
    dragRef.current = { index, startX: e.clientX, moved: false };
    window.addEventListener("pointermove", onWindowPointerMove);
    window.addEventListener("pointerup", onWindowPointerUp);
  };

  return (
    <div className="ruler">
      <div className="ruler-track" ref={trackRef}>
        <div className="ruler-axis-line" />
        {legs.map((leg, index) => {
          const lane = lanesByIndex.get(index) ?? 0;
          const offsetPx = lane * LANE_HEIGHT;
          const verticalStyle =
            leg.side === "long"
              ? { bottom: `calc(50% + ${8 + offsetPx}px)` }
              : { top: `calc(50% + ${8 + offsetPx}px)` };
          return (
            <button
              key={index}
              className={[
                "leg-chip",
                leg.right,
                leg.side === "long" ? "above" : "below",
                selectedIndex === index ? "selected" : "",
              ]
                .filter(Boolean)
                .join(" ")}
              style={{
                left: `${clampFraction(fraction(leg.strike)) * 100}%`,
                ...verticalStyle,
              }}
              onPointerDown={(e) => onPillPointerDown(e, index)}
              title={`${leg.side} ${leg.right} ${leg.strike} exp ${leg.expiration} — drag sideways for strike, up/down for side, double-click to swap call/put, × to remove`}
            >
              <span>
                {leg.contracts > 1 ? `${leg.contracts}× ` : ""}
                {leg.strike}
                {leg.right === "call" ? "C" : "P"}
              </span>
              <span
                className="leg-chip-x"
                onPointerDown={(e) => e.stopPropagation()}
                onClick={() => onRemove(index)}
                title="Remove leg"
              >
                ×
              </span>
            </button>
          );
        })}
        {spot != null && (
          <div className="spot-marker" style={{ left: "50%" }}>
            <div className="spot-marker-line" />
            <div className="spot-marker-label">
              {symbol} {spot.toFixed(2)}
            </div>
          </div>
        )}
        {ticks.map((t) => (
          <div
            key={t}
            className="ruler-tick-label"
            style={{ left: `${fraction(t) * 100}%` }}
          >
            {t}
          </div>
        ))}
        {legs.length === 0 && spot == null && (
          <div className="ruler-empty">Add a leg to see it on the strike ruler</div>
        )}
      </div>
    </div>
  );
}
