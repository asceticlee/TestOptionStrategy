export interface OptionLegRequest {
  right: "call" | "put";
  strike: number;
  expiration: string;
  contracts: number;
}

export interface SurfaceRequest {
  symbol: string;
  snapshotDate: string;
  snapshotTime: string;
  spotShares: number;
  timeStepMinutes: number;
  spotSamples: number;
  spotRangePercent: number;
  legs: OptionLegRequest[];
}

export interface OptionLegResult {
  right: string;
  strike: number;
  expiration: string;
  contracts: number;
  impliedVol: number;
  entryPrice: number;
}

export interface SpotLinePoint {
  x: number;
  y: string;
  z: number;
}

export interface SurfaceResponse {
  spotPrices: number[];
  timestamps: string[];
  valueSurface: number[][];
  deltaSurface: number[][];
  gammaSurface: number[][];
  spotLineValue: SpotLinePoint[];
  spotLineDelta: SpotLinePoint[];
  spotLineGamma: SpotLinePoint[];
  snapshotSpot: number;
  riskFreeRate: number;
  legs: OptionLegResult[];
}
