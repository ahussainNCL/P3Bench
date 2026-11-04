import type { Edit, LogEntry, State } from "./types";

export class ApiError extends Error {
  code: string;
  status: number;
  constructor(code: string, message: string, status = 0) {
    super(message);
    this.code = code;
    this.status = status;
  }
}

const base = (import.meta.env.VITE_BRIDGE as string | undefined) ?? "";

async function call<T>(path: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(base + path, {
      method: body === undefined ? "GET" : "POST",
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError("offline", "Cannot reach the local bridge on port 8787. Is P3OrbitBridge.exe running?", 0);
  }

  const text = await response.text();
  let payload: any = null;
  try {
    payload = text ? JSON.parse(text) : null;
  } catch {
    throw new ApiError("bad_response", "The bridge returned something that is not JSON.", response.status);
  }

  if (!response.ok || (payload && payload.ok === false)) {
    const error = payload?.error;
    throw new ApiError(error?.code ?? "http_" + response.status, error?.message ?? "Request failed.", response.status);
  }
  return payload as T;
}

const post = (path: string, body: unknown = {}) => call<State>(path, body);

export const api = {
  state: () => call<State>("/api/state"),
  log: (since: number) => call<{ seq: number; entries: LogEntry[] }>("/api/log?since=" + since),

  setMode: (mode: "simulator" | "hardware") => post("/api/mode", { mode }),
  refreshAdapters: () => post("/api/adapters/refresh", {}),
  selectAdapter: (library: string) => post("/api/adapter/select", { library }),
  connect: () => post("/api/connect", {}),
  disconnect: () => post("/api/disconnect", {}),

  read: () => post("/api/read", {}),
  importDump: (hex: string) => post("/api/dump/import", { hex }),

  plan: (edits: Edit[]) => post("/api/plan", { edits }),
  apply: (planId: string, confirm: string) => post("/api/apply", { planId, confirm }),
  undo: () => post("/api/undo", {}),
  discard: () => post("/api/discard", {}),

  reloadCatalog: () => post("/api/catalog/reload", {}),
  importCatalog: (text: string) => post("/api/catalog/import", { text }),
};

export function hexBytes(hex: string): number[] {
  const clean = hex.replace(/[^0-9a-fA-F]/g, "");
  const out: number[] = [];
  for (let i = 0; i + 1 < clean.length; i += 2) out.push(parseInt(clean.slice(i, i + 2), 16));
  return out;
}

export function toHex(bytes: number[], width = 2): string {
  return bytes.map((b) => b.toString(16).toUpperCase().padStart(width, "0")).join(" ");
}
