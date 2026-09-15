"use client";

import { useEffect, useMemo, useRef, type PointerEvent } from "react";

interface DateCell {
  value: string;
  weekday: string;
  day: number;
  isWeekend: boolean;
  monthLabel: string;
}

interface DateStripProps {
  selected: string;
  onSelect: (value: string) => void;
}

const CELL_WIDTH = 46;

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

function toValue(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function buildDates(): DateCell[] {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const start = new Date(today);
  start.setDate(start.getDate() - 60);
  const end = new Date(today);
  end.setDate(end.getDate() + 21);

  const cells: DateCell[] = [];
  const cursor = new Date(start);
  let previousMonth = -1;
  while (cursor <= end) {
    const dayOfWeek = cursor.getDay();
    const month = cursor.getMonth();
    const isMonthStart = month !== previousMonth;
    previousMonth = month;
    cells.push({
      value: toValue(cursor),
      weekday: cursor.toLocaleDateString("en-US", { weekday: "short" }),
      day: cursor.getDate(),
      isWeekend: dayOfWeek === 0 || dayOfWeek === 6,
      monthLabel: isMonthStart
        ? cursor.toLocaleDateString("en-US", { month: "short", year: "numeric" })
        : "",
    });
    cursor.setDate(cursor.getDate() + 1);
  }
  return cells;
}

export default function DateStrip({ selected, onSelect }: DateStripProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const selectedRef = useRef<HTMLButtonElement>(null);
  const draggedRef = useRef(false);
  const dragState = useRef<{ down: boolean; startX: number; scrollLeft: number }>({
    down: false,
    startX: 0,
    scrollLeft: 0,
  });

  const cells = useMemo(buildDates, []);

  const monthSpans = useMemo(() => {
    const spans: { label: string; count: number }[] = [];
    let current: { label: string; count: number } | null = null;
    for (const c of cells) {
      if (c.monthLabel !== "") {
        if (current) spans.push(current);
        current = { label: c.monthLabel, count: 1 };
      } else if (current) {
        current.count += 1;
      }
    }
    if (current) spans.push(current);
    return spans;
  }, [cells]);

  useEffect(() => {
    selectedRef.current?.scrollIntoView({ inline: "center", block: "nearest" });
  }, [selected]);

  const onWindowPointerMove = (e: globalThis.PointerEvent) => {
    if (!dragState.current.down) return;
    const el = scrollRef.current;
    if (!el) return;
    const dx = e.clientX - dragState.current.startX;
    if (Math.abs(dx) > 3) {
      draggedRef.current = true;
      el.scrollLeft = dragState.current.scrollLeft - dx;
    }
  };

  const onWindowPointerUp = () => {
    dragState.current.down = false;
    window.removeEventListener("pointermove", onWindowPointerMove);
    window.removeEventListener("pointerup", onWindowPointerUp);
    setTimeout(() => {
      draggedRef.current = false;
    }, 0);
  };

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    const el = scrollRef.current;
    if (!el) return;
    dragState.current = { down: true, startX: e.clientX, scrollLeft: el.scrollLeft };
    window.addEventListener("pointermove", onWindowPointerMove);
    window.addEventListener("pointerup", onWindowPointerUp);
  };

  const onCellClick = (value: string) => {
    if (draggedRef.current) return;
    onSelect(value);
  };

  return (
    <div className="date-strip-wrap">
      <div
        className="date-strip"
        ref={scrollRef}
        onPointerDown={onPointerDown}
      >
        <div className="date-strip-inner">
          <div className="date-month-row">
            {monthSpans.map((m) => (
              <div
                key={m.label}
                className="date-month-label"
                style={{ width: m.count * CELL_WIDTH }}
              >
                <span>{m.label}</span>
              </div>
            ))}
          </div>
          <div className="date-cell-row">
            {cells.map((c) => (
              <button
                key={c.value}
                ref={c.value === selected ? selectedRef : undefined}
                className={[
                  "date-cell",
                  c.value === selected ? "selected" : "",
                  c.isWeekend ? "weekend" : "",
                ]
                  .filter(Boolean)
                  .join(" ")}
                onClick={() => onCellClick(c.value)}
              >
                <span className="date-cell-weekday">{c.weekday}</span>
                <span className="date-cell-day">{c.day}</span>
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
