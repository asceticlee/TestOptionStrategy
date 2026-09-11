import type { SurfaceRequest, SurfaceResponse } from "./types";

// Same-origin: Next.js rewrites /api/* to the C# server (see next.config.mjs).
// This keeps the browser on :3000 so only that port needs forwarding.
const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? "";

export async function fetchUnderlyings(): Promise<string[]> {
  const res = await fetch(`${API_BASE}/api/market/underlyings`);
  if (!res.ok) throw new Error("Failed to fetch underlyings");
  return res.json();
}

export async function fetchExpirations(symbol: string): Promise<string[]> {
  const res = await fetch(
    `${API_BASE}/api/market/expirations?symbol=${encodeURIComponent(symbol)}`,
  );
  if (!res.ok) throw new Error("Failed to fetch expirations");
  return res.json();
}

export async function fetchStrikes(
  symbol: string,
  expiration: string,
): Promise<number[]> {
  const res = await fetch(
    `${API_BASE}/api/market/strikes?symbol=${encodeURIComponent(symbol)}&expiration=${encodeURIComponent(expiration)}`,
  );
  if (!res.ok) throw new Error("Failed to fetch strikes");
  return res.json();
}

export async function fetchSpot(
  symbol: string,
  date: string,
  time: string,
): Promise<number> {
  const res = await fetch(
    `${API_BASE}/api/market/spot?symbol=${encodeURIComponent(symbol)}&date=${encodeURIComponent(date)}&time=${encodeURIComponent(time)}`,
  );
  if (!res.ok) throw new Error("Failed to fetch spot");
  return res.json();
}

export async function computeSurface(
  request: SurfaceRequest,
): Promise<SurfaceResponse> {
  const res = await fetch(`${API_BASE}/api/surface`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed with status ${res.status}`);
  }
  return res.json();
}
