import type { CatalogGroup, CatalogParam, Dump, State } from "../types";
import { Badge, Button, Icon, Switch, cx } from "./Bits";

type Value = boolean | number | string;

interface Props {
  state: State;
  draft: Record<string, Value>;
  onDraft: (id: string, value: Value) => void;
  filter: string;
  onFilter: (value: string) => void;
  pendingOnly: boolean;
  onPendingOnly: (value: boolean) => void;
  onPreview: () => void;
  onRead: () => void;
  onImportDump: () => void;
  busy: string | null;
}

function typeLabel(p: CatalogParam) {
  switch (p.type) {
    case "bool":
      return `bit ${p.bit}`;
    case "enum":
      return `enum · ${p.length} B`;
    case "ascii":
      return `ascii · ${p.length} B`;
    default:
      return `${p.type} · ${p.length} B`;
  }
}

function Control({
  param,
  value,
  onChange,
}: {
  param: CatalogParam;
  value: Value;
  onChange: (next: Value) => void;
}) {
  const inputClass =
    "h-8 rounded border border-line2 bg-panel2 px-2 font-mono text-[12px] text-fg outline-none transition-colors focus:border-[#6b4a2d] focus:ring-2 focus:ring-acc/20";

  if (param.type === "bool") {
    return (
      <div className="flex items-center justify-end gap-3">
        <span className={cx("font-mono text-[11px]", value ? "text-acc" : "text-dim")}>{value ? "ON" : "OFF"}</span>
        <Switch checked={Boolean(value)} onChange={onChange} label={param.label} />
      </div>
    );
  }

  if (param.type === "enum") {
    if (param.values.length <= 4) {
      return (
        <div className="flex flex-wrap justify-end gap-1">
          {param.values.map((option) => (
            <button
              key={option.value}
              onClick={() => onChange(option.value)}
              className={cx(
                "h-7 rounded border px-2.5 text-[11.5px] transition-colors",
                value === option.value
                  ? "border-[#6b4a2d] bg-[#26190f] text-acc"
                  : "border-line bg-panel2 text-mut hover:border-line2 hover:text-fg"
              )}
            >
              {option.label}
            </button>
          ))}
        </div>
      );
    }
    return (
      <select
        value={String(value)}
        onChange={(e) => onChange(Number(e.target.value))}
        className={cx(inputClass, "w-full")}
      >
        {param.values.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    );
  }

  if (param.type === "ascii") {
    return (
      <input
        value={String(value)}
        maxLength={param.length}
        onChange={(e) => onChange(e.target.value)}
        className={cx(inputClass, "w-full tracking-[0.08em]")}
        spellCheck={false}
      />
    );
  }

  return (
    <div className="flex items-center justify-end gap-2">
      <input
        type="number"
        value={Number(value)}
        min={param.min}
        max={param.max > 0 ? param.max : undefined}
        step={1}
        onChange={(e) => {
          const next = e.target.value === "" ? param.min : Number(e.target.value);
          onChange(next);
        }}
        className={cx(inputClass, "w-24 text-right")}
      />
      {param.unit && <span className="w-14 text-[11px] text-dim">{param.unit}</span>}
    </div>
  );
}

function Row({
  param,
  draft,
  onDraft,
}: {
  param: CatalogParam;
  draft: Record<string, Value>;
  onDraft: (id: string, value: Value) => void;
}) {
  const current = param.value;
  const value = draft[param.id] ?? current;
  const dirty = draft[param.id] !== undefined && draft[param.id] !== current;

  return (
    <div
      className={cx(
        "grid grid-cols-1 items-center gap-3 border-b border-line/60 px-6 py-3.5 transition-colors md:grid-cols-[1fr_272px] md:gap-5",
        dirty ? "bg-[#17120d]" : "hover:bg-panel/50"
      )}
    >
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-[13px] font-medium text-fg">{param.label}</span>
          {dirty && <Badge tone="acc">pending</Badge>}
          {param.notes && (
            <span title={param.notes} className="text-dim">
              <Icon name="warn" className="h-3.5 w-3.5" />
            </span>
          )}
        </div>
        {param.description && <p className="mt-0.5 truncate text-[11.5px] text-dim">{param.description}</p>}
        <div className="mt-1 flex items-center gap-3 font-mono text-[10px] text-[#55616e]">
          <span>0x{param.offset.toString(16).toUpperCase().padStart(3, "0")}</span>
          <span>{typeLabel(param)}</span>
          <span className="truncate text-[#414b56]">{param.id}</span>
        </div>
      </div>

      <div className="flex items-center justify-end gap-3">
        {dirty && current !== undefined && (
          <span className="shrink-0 text-right font-mono text-[10px] leading-tight text-dim">
            <span className="block text-[#4c5763]">was</span>
            {param.display}
          </span>
        )}
        <div className="min-w-0 flex-1">
          <Control param={param} value={value ?? ""} onChange={(next) => onDraft(param.id, next)} />
        </div>
      </div>
    </div>
  );
}

function GroupSection({
  group,
  draft,
  onDraft,
  filter,
  pendingOnly,
}: {
  group: CatalogGroup;
  draft: Record<string, Value>;
  onDraft: (id: string, value: Value) => void;
  filter: string;
  pendingOnly: boolean;
}) {
  const needle = filter.trim().toLowerCase();
  const params = group.params.filter((p) => {
    const matches =
      !needle ||
      p.label.toLowerCase().includes(needle) ||
      p.id.toLowerCase().includes(needle) ||
      (p.description ?? "").toLowerCase().includes(needle);
    const pending = draft[p.id] !== undefined && draft[p.id] !== p.value;
    return matches && (!pendingOnly || pending);
  });

  if (params.length === 0) return null;

  return (
    <section>
      <header className="sticky top-0 z-10 flex items-baseline gap-3 border-y border-line bg-ink/95 px-6 py-2.5 backdrop-blur">
        <h3 className="text-[11.5px] font-semibold uppercase tracking-[0.1em] text-fg">{group.label}</h3>
        <span className="font-mono text-[10.5px] text-dim">{params.length}</span>
        {group.description && <p className="ml-auto truncate text-[11px] text-dim">{group.description}</p>}
      </header>
      {params.map((param) => (
        <Row key={param.id} param={param} draft={draft} onDraft={onDraft} />
      ))}
    </section>
  );
}

function EmptyState({
  dump,
  state,
  onRead,
  onImportDump,
  busy,
}: {
  dump: Dump | null;
  state: State;
  onRead: () => void;
  onImportDump: () => void;
  busy: string | null;
}) {
  return (
    <div className="grid-lines flex min-h-0 flex-1 items-center justify-center p-8">
      <div className="w-full max-w-md rounded-lg border border-line bg-panel/90 p-7 shadow-2xl shadow-black/40">
        <div className="mb-4 inline-flex h-11 w-11 items-center justify-center rounded-md border border-[#4a3524] bg-[#1d1712] text-acc">
          <Icon name="hex" className="h-5 w-5" />
        </div>
        <h2 className="text-[17px] font-semibold text-fg">No config block loaded</h2>
        <p className="mt-1.5 text-[12.5px] leading-relaxed text-mut">
          {state.mode === "simulator"
            ? "Read the synthetic CEM to populate the editor, then stage changes and preview the exact bytes before applying. Writes only touch the in-memory simulator."
            : "The active profile has no verified read path. Connect for identity probes, or import a dump you captured elsewhere."}
        </p>

        <div className="mt-5 flex flex-col gap-2">
          <Button
            variant="primary"
            onClick={onRead}
            loading={busy === "read"}
            disabled={!state.capabilities.read || busy !== null}
            icon={<Icon name="download" className="h-4 w-4" />}
          >
            Read config
            <span className="ml-1 font-mono text-[10.5px] opacity-60">{state.catalog.configLength} B</span>
          </Button>
          <Button variant="outline" onClick={onImportDump} disabled={busy !== null}>
            Import hex dump
          </Button>
        </div>

        {!state.capabilities.read && state.capabilities.reason && (
          <p className="mt-4 rounded border border-[#4d4020] bg-[#1f1a0f] px-3 py-2 text-[11px] leading-relaxed text-warn">
            {state.capabilities.reason}
          </p>
        )}

        {dump === null && (
          <p className="mt-4 font-mono text-[10.5px] leading-relaxed text-[#4d5865]">
            catalog {state.catalog.id} · {state.catalog.configLength} bytes · checksum{" "}
            {state.catalog.checksum?.enabled ? `sum @ ${state.catalog.checksum.byteIndex}` : "off"}
          </p>
        )}
      </div>
    </div>
  );
}

export default function ConfigPanel({
  state,
  draft,
  onDraft,
  filter,
  onFilter,
  pendingOnly,
  onPendingOnly,
  onPreview,
  onRead,
  onImportDump,
  busy,
}: Props) {
  const params = state.catalog.groups.flatMap((g) => g.params);
  const pending = params.filter((p) => draft[p.id] !== undefined && draft[p.id] !== p.value).length;
  const dump = state.dump;
  const checksum = dump?.checksum;
  const checksumOk = checksum?.enabled ? checksum.ok : null;

  return (
    <div className="flex min-h-0 min-w-0 flex-1 flex-col bg-ink">
      <div className="flex shrink-0 flex-col gap-2.5 border-b border-line bg-panel/60 px-4 py-3 sm:px-6 xl:flex-row xl:items-center xl:gap-3">
        <div className="mr-2 shrink-0">
          <h2 className="text-[13px] font-semibold text-fg">Configuration</h2>
          <p className="text-[10.5px] text-dim">
            {dump ? `${dump.length} bytes · ${dump.source}` : "nothing loaded"}
            {checksumOk === true && <span className="ml-2 text-ok">checksum ok</span>}
            {checksumOk === false && <span className="ml-2 text-bad">checksum mismatch</span>}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2 xl:ml-auto">
          <div className="relative">
            <Icon name="list" className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-dim" />
            <input
              value={filter}
              onChange={(e) => onFilter(e.target.value)}
              placeholder="Filter parameters"
              className="h-8 w-40 rounded border border-line bg-ink pl-8 pr-2.5 text-[12px] text-fg outline-none placeholder:text-[#4d5865] focus:border-line2 sm:w-52"
            />
          </div>

          <button
            onClick={() => onPendingOnly(!pendingOnly)}
            disabled={pending === 0}
            className={cx(
              "h-8 rounded border px-2.5 text-[11.5px] transition-colors disabled:opacity-40",
              pendingOnly ? "border-[#6b4a2d] bg-[#26190f] text-acc" : "border-line bg-panel2 text-mut hover:text-fg"
            )}
          >
            Pending <span className="font-mono">{pending}</span>
          </button>

          <div className="ml-auto flex items-center gap-2">
            <Button size="sm" variant="outline" onClick={onPreview} disabled={pending === 0 || busy !== null} loading={busy === "plan"}>
              Preview plan
            </Button>
            <Button
              size="sm"
              variant="primary"
              onClick={onRead}
              loading={busy === "read"}
              disabled={!state.capabilities.read || busy !== null}
              icon={<Icon name="download" className="h-3.5 w-3.5" />}
            >
              {dump ? "Re-read" : "Read config"}
            </Button>
          </div>
        </div>
      </div>

      {!dump ? (
        <EmptyState dump={dump} state={state} onRead={onRead} onImportDump={onImportDump} busy={busy} />
      ) : (
        <div className="min-h-0 flex-1 overflow-y-auto pb-10">
          {state.catalog.groups.map((group) => (
            <GroupSection
              key={group.id}
              group={group}
              draft={draft}
              onDraft={onDraft}
              filter={filter}
              pendingOnly={pendingOnly}
            />
          ))}
          {pendingOnly && pending === 0 && (
            <p className="px-6 py-10 text-center text-[12.5px] text-dim">No pending changes. Turn the filter off to browse.</p>
          )}
        </div>
      )}
    </div>
  );
}
