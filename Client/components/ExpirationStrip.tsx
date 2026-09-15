"use client";

import { useEffect, useMemo, useRef } from "react";

interface ExpirationStripProps {
  availableExpirations: string[];
  snapshotDate: string;
  selected: string;
  onSelect: (exp: string) => void;
}

function monthLabel(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return d.toLocaleDateString("en-US", { month: "short", year: "numeric" });
}

export default function ExpirationStrip({
  availableExpirations,
  snapshotDate,
  selected,
  onSelect,
}: ExpirationStripProps) {
  const chipsRef = useRef<Map<number, HTMLButtonElement>>(new Map());

  const months = useMemo(() => {
    const seen = new Set<string>();
    const out: { label: string; firstIndex: number }[] = [];
    availableExpirations.forEach((exp, i) => {
      const label = monthLabel(exp);
      if (!seen.has(label)) {
        seen.add(label);
        out.push({ label, firstIndex: i });
      }
    });
    return out;
  }, [availableExpirations]);

  const selectedIndex = availableExpirations.indexOf(selected);

  const daysToExpiry =
    selected && snapshotDate
      ? Math.round((Date.parse(selected) - Date.parse(snapshotDate)) / 86400000)
      : null;

  useEffect(() => {
    if (selectedIndex >= 0) {
      chipsRef.current
        .get(selectedIndex)
        ?.scrollIntoView({ inline: "center", block: "nearest" });
    }
  }, [selected, selectedIndex]);

  const scrollToMonth = (firstIndex: number) => {
    chipsRef.current
      .get(firstIndex)
      ?.scrollIntoView({ inline: "start", block: "nearest" });
  };

  return (
    <div className="exp-strip">
      <div className="exp-strip-header">
        <span className="exp-strip-label">
          EXPIRATION: {daysToExpiry != null ? `${daysToExpiry}d` : "—"}
        </span>
      </div>
      <div className="exp-month-tabs">
        {months.map((m) => (
          <button
            key={m.label}
            className={`exp-month-tab ${
              selected && monthLabel(selected) === m.label ? "active" : ""
            }`}
            onClick={() => scrollToMonth(m.firstIndex)}
          >
            {m.label}
          </button>
        ))}
      </div>
      <div className="exp-chips">
        {availableExpirations.map((exp, i) => (
          <button
            key={exp}
            ref={(el) => {
              if (el) chipsRef.current.set(i, el);
            }}
            className={`exp-chip ${exp === selected ? "selected" : ""}`}
            onClick={() => onSelect(exp)}
            title={exp}
          >
            {exp.slice(8, 10)}
          </button>
        ))}
      </div>
    </div>
  );
}
