declare module "plotly.js-dist-min" {
  const Plotly: {
    newPlot: (div: HTMLElement, data: unknown[], layout: unknown) => Promise<unknown>;
    addTraces: (div: HTMLElement, traces: unknown[]) => Promise<unknown>;
    relayout: (div: HTMLElement, update: unknown) => Promise<unknown>;
    purge: (div: HTMLElement) => void;
    [key: string]: unknown;
  };
  export default Plotly;
}
