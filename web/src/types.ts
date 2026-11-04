export type ParamType = "bool" | "enum" | "u8" | "u16le" | "u16be" | "ascii";

export interface CatalogParam {
  id: string;
  label: string;
  description?: string | null;
  type: ParamType;
  offset: number;
  bit: number;
  length: number;
  min: number;
  max: number;
  scale: number;
  unit?: string | null;
  notes?: string | null;
  values: { value: number; label: string }[];
  value?: boolean | number | string;
  display?: string;
}

export interface CatalogGroup {
  id: string;
  label: string;
  description?: string | null;
  params: CatalogParam[];
}

export interface Catalog {
  id: string;
  label: string;
  description?: string | null;
  platform: string;
  synthetic: boolean;
  configLength: number;
  checksum?: {
    enabled: boolean;
    byteIndex: number;
    from: number;
    to: number;
  } | null;
  groups: CatalogGroup[];
}

export interface Dump {
  id: string;
  source: string;
  provenance: string;
  capturedAt: string;
  vin?: string | null;
  length: number;
  hex: string;
  checksum: {
    enabled: boolean;
    byteIndex?: number;
    from?: number;
    to?: number;
    expected?: number;
    actual?: number;
    ok?: boolean;
  };
}

export interface Change {
  paramId: string;
  label: string;
  offset: number;
  oldHex: string;
  newHex: string;
  oldDisplay: string;
  newDisplay: string;
  kind: "param" | "checksum";
}

export interface Staged {
  id: string;
  summary: string;
  changedBytes: number;
  blockHex: string;
  changes: Change[];
}

export interface AdapterInfo {
  name: string;
  vendor: string;
  library: string;
  selected: boolean;
}

export interface ModuleInfo {
  id: string;
  name: string;
  can: string;
  hardware: string;
  software: string;
  source: string;
}

export interface Vehicle {
  vin: string | null;
  model: string | null;
  year: string | null;
  market: string | null;
  platform: string | null;
  source: string;
  modules: ModuleInfo[];
}

export interface Profile {
  id: string;
  label: string;
  description?: string | null;
  platform: string;
  synthetic: boolean;
  validated: boolean;
  configLength: number;
  requestId: string;
  responseId: string;
  baud: number;
  read: { method: string; did?: string | null; length: number; note?: string | null } | null;
  notes: string[] | null;
  active: boolean;
}

export interface State {
  ok: boolean;
  bridge: { version: string; port: number; pid: number; startedAt: string; uptimeSec: number };
  mode: "simulator" | "hardware";
  adapters: AdapterInfo[];
  adapter: {
    name: string | null;
    vendor: string | null;
    library: string | null;
    open: boolean;
    voltage: number | null;
  } | null;
  profile: Profile;
  profiles: Profile[];
  vehicle: Vehicle | null;
  connection: { state: "disconnected" | "adapter-open" | "connected"; error?: string | null };
  catalog: Catalog;
  dump: Dump | null;
  staged: Staged | null;
  warnings: string[];
  capabilities: {
    read: boolean;
    apply: boolean;
    applyReason: string;
    undo: boolean;
    undoDepth: number;
    reason?: string | null;
  };
  catalogSource: string;
}

export interface LogEntry {
  seq: number;
  at: string;
  level: "info" | "warn" | "error";
  text: string;
}

export interface Edit {
  id: string;
  value: boolean | number | string;
}
