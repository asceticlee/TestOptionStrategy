"use client";

import type { StatsResponse } from "../lib/types";

interface StatsStripProps {
  stats: StatsResponse | null;
}

function fmtMoney(v: number): string {
  const sign = v < 0 ? "-" : "";
  return `${sign}$${Math.abs(v).toFixed(0)}`;
}

export default function StatsStrip({ stats }: StatsStripProps) {
  if (!stats) {
    return (
      <div className="stats-strip">
        <div className="stat-cell">
          <div className="stat-label">NET DEBIT / CREDIT</div>
          <div className="stat-value">—</div>
        </div>
        <div className="stat-cell">
          <div className="stat-label">MAX LOSS</div>
          <div className="stat-value">—</div>
        </div>
        <div className="stat-cell">
          <div className="stat-label">MAX PROFIT</div>
          <div className="stat-value">—</div>
        </div>
        <div className="stat-cell">
          <div className="stat-label">CHANCE OF PROFIT</div>
          <div className="stat-value">—</div>
        </div>
        <div className="stat-cell">
          <div className="stat-label">BREAKEVENS</div>
          <div className="stat-value">—</div>
        </div>
      </div>
    );
  }

  const isCredit = stats.netDebit < 0;
  const netLabel = isCredit ? "NET CREDIT" : "NET DEBIT";
  const netValue = Math.abs(stats.netDebit);

  const breakevenText =
    stats.breakevens.length === 0
      ? "—"
      : stats.breakevens.length === 1
        ? `Above $${stats.breakevens[0].toFixed(2)}`
        : stats.breakevens.length === 2
          ? `Outside of $${stats.breakevens[0].toFixed(2)} - $${stats.breakevens[1].toFixed(2)}`
          : stats.breakevens.map((b) => `$${b.toFixed(2)}`).join(", ");

  return (
    <div className="stats-strip">
      <div className="stat-cell">
        <div className="stat-label">{netLabel}</div>
        <div className={`stat-value ${isCredit ? "pos" : "neg"}`}>
          {fmtMoney(netValue)}
        </div>
      </div>
      <div className="stat-cell">
        <div className="stat-label">MAX LOSS</div>
        <div className="stat-value neg">{fmtMoney(stats.maxLoss)}</div>
      </div>
      <div className="stat-cell">
        <div className="stat-label">MAX PROFIT</div>
        <div className="stat-value pos">{fmtMoney(stats.maxProfit)}</div>
      </div>
      <div className="stat-cell">
        <div className="stat-label">CHANCE OF PROFIT</div>
        <div className="stat-value">—</div>
      </div>
      <div className="stat-cell">
        <div className="stat-label">BREAKEVENS</div>
        <div className="stat-value small">{breakevenText}</div>
      </div>
    </div>
  );
}
