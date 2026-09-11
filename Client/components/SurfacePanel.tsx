"use client";

import { useEffect, useRef } from "react";
import type { SpotLinePoint } from "../lib/types";

interface SurfacePanelProps {
  data: {
    x: number[];
    y: string[];
    z: number[][];
    spotLine: SpotLinePoint[];
  };
  title: string;
  zTitle: string;
}

interface PlotlyLike {
  newPlot: (div: HTMLElement, data: unknown[], layout: unknown) => Promise<PlotlyDiv>;
  addTraces: (div: HTMLElement, traces: unknown[]) => Promise<unknown>;
  relayout: (div: HTMLElement, update: unknown) => Promise<unknown>;
  purge: (div: HTMLElement) => void;
}

interface PlotlyDiv extends HTMLElement {
  on: (event: string, callback: (data: unknown) => void) => void;
  removeAllListeners: () => void;
}

interface RegistryEntry {
  id: string;
  plotly: PlotlyLike;
}

const divRegistry: RegistryEntry[] = [];
const lastCamera: Record<string, unknown> = {};

async function getPlotly(): Promise<PlotlyLike> {
  const mod = await import("plotly.js-dist-min");
  return (mod.default ?? mod) as PlotlyLike;
}

export default function SurfacePanel({ data, title, zTitle }: SurfacePanelProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const divId = useRef<string>(`surface-${Math.random().toString(36).slice(2, 9)}`);

  useEffect(() => {
    const id = divId.current;
    const el = containerRef.current;
    if (!el) return;

    let cancelled = false;
    let plotly: PlotlyLike | null = null;

    (async () => {
      const plt = await getPlotly();
      if (cancelled || !el) return;
      plotly = plt;

      divRegistry.push({ id, plotly: plt });

      const surface = {
        x: data.x,
        y: data.y,
        z: data.z,
        type: "surface",
        contours: {
          x: { show: true, color: "black" },
          y: { show: true, color: "black" },
        },
        colorscale: buildColorscale(data.z),
      };

      const line = {
        x: data.spotLine.map((p) => p.x),
        y: data.spotLine.map((p) => p.y),
        z: data.spotLine.map((p) => p.z),
        type: "scatter3d",
        mode: "lines",
        line: { width: 5, color: "orange" },
      };

      const layout = {
        title,
        autosize: true,
        width: 1000,
        height: 760,
        scene: {
          camera: { eye: { x: 0.5, y: -1, z: 1.5 } },
          xaxis: { title: "Spot Price", tickfont: { size: 10, color: "black" } },
          yaxis: {
            tickformat: "%Y%m%d %H:%M",
            title: "",
            tickfont: { size: 10, color: "black" },
          },
          zaxis: { title: zTitle, tickfont: { size: 10, color: "black" } },
        },
      };

      const plotlyDiv = await plt.newPlot(el, [surface], layout);
      if (data.spotLine.length > 0) {
        await plt.addTraces(el, [line]);
      }

      lastCamera[id] = null;
      plotlyDiv.on("plotly_relayout", (eventData: unknown) => {
        syncCamera(plt, id, eventData);
      });
    })();

    return () => {
      cancelled = true;
      const idx = divRegistry.findIndex((d) => d.id === id);
      if (idx >= 0) divRegistry.splice(idx, 1);
      delete lastCamera[id];
      if (plotly && el) {
        plotly.purge(el);
      }
    };
  }, [data, title, zTitle]);

  return <div ref={containerRef} />;
}

function syncCamera(plotly: PlotlyLike, sourceId: string, eventData: unknown) {
  const e = eventData as { "scene.camera"?: unknown };
  const newCamera = e?.["scene.camera"];
  if (!newCamera) return;
  if (JSON.stringify(newCamera) === JSON.stringify(lastCamera[sourceId])) return;
  lastCamera[sourceId] = newCamera;
  divRegistry.forEach((d) => {
    if (d.id !== sourceId) {
      const target = document.getElementById(d.id);
      if (target) {
        void plotly.relayout(target, { "scene.camera": newCamera });
      }
    }
  });
}

function buildColorscale(z: number[][]): Array<[number, string]> {
  let minZ = Infinity;
  let maxZ = -Infinity;
  for (const row of z) {
    for (const v of row) {
      if (Number.isNaN(v)) continue;
      minZ = Math.min(minZ, v);
      maxZ = Math.max(maxZ, v);
    }
  }
  if (maxZ - minZ < 1e-12) {
    return [
      [0, "rgb(0,0,255)"],
      [0.5, "grey"],
      [1, "rgb(56,127,35)"],
    ];
  }
  const normalizedZero = Math.max(0 - Math.min(minZ, 0), 0) / (maxZ - minZ);
  return [
    [0, "rgb(0,0,255)"],
    [Math.max(normalizedZero * 0.995, 0), "white"],
    [normalizedZero, "grey"],
    [Math.min(normalizedZero + normalizedZero * 0.005, 1), "white"],
    [1, "rgb(56,127,35)"],
  ];
}
