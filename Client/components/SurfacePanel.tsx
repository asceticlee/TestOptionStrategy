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

      const yLabels = data.y;
      const yIndex = new Map<string, number>();
      yLabels.forEach((label, i) => yIndex.set(label, i));

      const surface = {
        x: data.x,
        y: yLabels.map((_, i) => i),
        z: data.z,
        customdata: data.z.map((row, i) => row.map(() => yLabels[i])),
        type: "surface",
        hovertemplate:
          "Spot: %{x:.2f}<br>Time: %{customdata}<br>Value: %{z:.2f}<extra></extra>",
        contours: {
          x: { show: true, color: "black" },
          y: { show: true, color: "black" },
        },
        colorscale: buildColorscale(data.z),
      };

      const line = {
        x: data.spotLine.map((p) => p.x),
        y: data.spotLine.map((p) => yIndex.get(p.y) ?? 0),
        z: data.spotLine.map((p) => p.z),
        customdata: data.spotLine.map((p) => p.y),
        type: "scatter3d",
        mode: "lines",
        hovertemplate:
          "Spot: %{x:.2f}<br>Time: %{customdata}<br>Value: %{z:.2f}<extra></extra>",
        line: { width: 5, color: "orange" },
      };

      const tickStep = Math.max(1, Math.ceil(yLabels.length / 50));
      const tickvals: number[] = [];
      const ticktext: string[] = [];
      for (let i = 0; i < yLabels.length; i += tickStep) {
        tickvals.push(i);
        ticktext.push(yLabels[i].slice(5));
      }
      const lastIndex = yLabels.length - 1;
      if (
        lastIndex >= 0 &&
        tickvals.length > 0 &&
        tickvals[tickvals.length - 1] !== lastIndex
      ) {
        tickvals.push(lastIndex);
        ticktext.push(yLabels[lastIndex].slice(5));
      }

      const layout = {
        title: { text: title, font: { color: "#e8eaf2" } },
        autosize: true,
        width: 1000,
        height: 760,
        paper_bgcolor: "#04041f",
        font: { color: "#cfd4e6" },
        scene: {
          camera: { eye: { x: 0.5, y: -1, z: 1.5 } },
          xaxis: {
            title: "Spot Price",
            titlefont: { color: "#9aa1b5" },
            tickfont: { size: 10, color: "#9aa1b5" },
            gridcolor: "#26264a",
          },
          yaxis: {
            title: "",
            tickvals,
            ticktext,
            tickfont: { size: 8, color: "#9aa1b5" },
            gridcolor: "#26264a",
          },
          zaxis: {
            title: zTitle,
            titlefont: { color: "#9aa1b5" },
            tickfont: { size: 10, color: "#9aa1b5" },
            gridcolor: "#26264a",
          },
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

  return <div ref={containerRef} className="chart" />;
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
