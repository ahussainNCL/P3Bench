import type { ReactNode } from "react";
import type { State } from "../types";
import { Badge, Button, Icon, cx } from "./Bits";

interface Props {
  state: State;
  busy: string | null;
  onMode: (mode: "simulator" | "hardware") => void;
  onSelectAdapter: (library: string) => void;
  onRefreshAdapters: () => void;
  onConnect: () => void;
  onDisconnect: () => void;
  onReloadCatalog: () => void;
  onImportCatalog: () => void;
  onImportDump: () => void;
}

function Section({ title, right, children }: { title: string; right?: ReactNode; children: ReactNode }) {
  return (
    <section className="border-b border-line px-4 py-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-[10.5px] font-semibold uppercase tracking-[0.12em] text-dim">{title}</h2>
        {right}
      </div>
      {children}
    </section>
  );
}

export default function Sidebar({
  state,
  busy,
  onMode,
  onSelectAdapter,
  onRefreshAdapters,
  onConnect,
  onDisconnect,
  onReloadCatalog,
  onImportCatalog,
  onImportDump,
}: Props) {
  const connected = state.connection.state === "connected";
  const params = state.catalog.groups.reduce((sum, g) => sum + g.params.length, 0);

  return (
    <aside className="flex min-h-0 w-[304px] shrink-0 flex-col overflow-y-auto border-r border-line bg-panel">
      <Section
        title="Connection"
        right={
          <span className="flex items-center gap-1.5 text-[10.5px] text-dim">
            <span
              className={cx(
                "h-1.5 w-1.5 rounded-full",
                connected ? "bg-ok live-dot" : state.connection.state === "adapter-open" ? "bg-warn" : "bg-[#3a444f]"
              )}
            />
            {state.connection.state.replace("-", " ")}
          </span>
        }
      >
        <div className="mb-3 grid grid-cols-2 gap-1 rounded-md border border-line bg-ink p-1">
          {(["simulator", "hardware"] as const).map((mode) => (
            <button
              key={mode}
              onClick={() => onMode(mode)}
              disabled={busy !== null || connected}
              className={cx(
                "h-7 rounded text-[11.5px] font-medium capitalize transition-colors disabled:opacity-40",
                state.mode === mode ? "bg-panel2 text-fg shadow-sm ring-1 ring-line2" : "text-mut hover:text-fg"
              )}
            >
              {mode}
            </button>
          ))}
        </div>

        {state.mode === "hardware" ? (
          <div className="space-y-2">
            <div className="max-h-40 space-y-1 overflow-y-auto pr-0.5">
              {state.adapters.length === 0 && (
                <p className="rounded border border-dashed border-line2 px-2.5 py-2 text-[11.5px] leading-relaxed text-dim">
                  No 32-bit J2534 v04.04 driver found.
                </p>
              )}
              {state.adapters.map((adapter) => (
                <button
                  key={adapter.library + adapter.name}
                  onClick={() => onSelectAdapter(adapter.library)}
                  disabled={connected || busy !== null}
                  title={adapter.library}
                  className={cx(
                    "w-full rounded border px-2.5 py-2 text-left transition-colors disabled:cursor-not-allowed",
                    adapter.library === state.adapter?.library
                      ? "border-[#4a3524] bg-[#1d1712]"
                      : "border-line bg-panel2 hover:border-line2"
                  )}
                >
                  <span className="flex items-center gap-2 text-[12px] font-medium text-fg">
                    <Icon name="plug" className="h-3.5 w-3.5 text-dim" />
                    {adapter.name}
                  </span>
                  <span className="mt-0.5 block truncate pl-6 text-[10.5px] text-dim">{adapter.vendor}</span>
                </button>
              ))}
            </div>

            <div className="flex items-center gap-2">
              <Button size="sm" variant="ghost" onClick={onRefreshAdapters} disabled={connected || busy !== null}>
                Rescan
              </Button>
              <span className="flex-1" />
              {connected ? (
                <Button size="sm" variant="danger" onClick={onDisconnect} loading={busy === "disconnect"}>
                  Disconnect
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="primary"
                  onClick={onConnect}
                  loading={busy === "connect"}
                  disabled={!state.adapter || state.adapter.open}
                  icon={<Icon name="bolt" className="h-3.5 w-3.5" />}
                >
                  Connect
                </Button>
              )}
            </div>

            {state.connection.error && (
              <p className="rounded border border-[#4d2726] bg-[#1d1211] px-2.5 py-2 font-mono text-[10.5px] leading-relaxed text-bad">
                {state.connection.error}
              </p>
            )}
            {state.adapter && state.adapter.voltage !== null && (
              <p className="font-mono text-[11px] text-mut">
                supply <span className="text-fg">{state.adapter.voltage.toFixed(2)} V</span>
              </p>
            )}
          </div>
        ) : (
          <p className="rounded border border-dashed border-line2 px-2.5 py-2 text-[11.5px] leading-relaxed text-dim">
            Synthetic CEM runs in the bridge. No adapter, no vehicle. Switch to hardware for real
            diagnostic traffic.
          </p>
        )}
      </Section>

      <Section
        title="Vehicle"
        right={state.vehicle && <Badge tone={state.vehicle.source.startsWith("sim") ? "warn" : "ok"}>{state.vehicle.source.split(" ")[0]}</Badge>}
      >
        {state.vehicle ? (
          <div>
            <p className="font-mono text-[15px] tracking-[0.14em] text-fg">{state.vehicle.vin ?? "—"}</p>
            <p className="mt-1 text-[12px] text-mut">
              {[state.vehicle.model, state.vehicle.year, state.vehicle.market].filter(Boolean).join(" · ") || "Unknown vehicle"}
            </p>
            <div className="mt-2 flex flex-wrap gap-1">
              <Badge mono>{state.vehicle.platform ?? "P3"}</Badge>
              <Badge mono>{state.profile.requestId} → {state.profile.responseId}</Badge>
            </div>

            {state.vehicle.modules.length > 0 && (
              <details className="group mt-3">
                <summary className="flex cursor-pointer list-none items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.09em] text-dim hover:text-mut">
                  <Icon name="chevron" className="h-3 w-3 transition-transform group-open:rotate-90" />
                  {state.vehicle.modules.length} modules
                </summary>
                <ul className="mt-2 space-y-1">
                  {state.vehicle.modules.map((m) => (
                    <li key={m.id} className="flex items-baseline justify-between gap-2 rounded bg-panel2 px-2 py-1.5">
                      <span className="text-[11.5px] text-fg">
                        <span className="font-mono text-[10.5px] text-acc">{m.id}</span>{" "}
                        <span className="text-mut">{m.name}</span>
                      </span>
                      <span className="shrink-0 font-mono text-[10px] text-dim">{m.can}</span>
                    </li>
                  ))}
                </ul>
              </details>
            )}
          </div>
        ) : (
          <p className="text-[11.5px] text-dim">Nothing connected. Simulator mode shows a synthetic S60 II.</p>
        )}
      </Section>

      <Section title="Catalog" right={state.catalog.synthetic ? <Badge tone="acc">synthetic</Badge> : <Badge tone="ok">curated</Badge>}>
        <p className="text-[12px] font-medium text-fg">{state.catalog.label}</p>
        <p className="mt-0.5 truncate font-mono text-[10.5px] text-dim" title={state.catalogSource}>
          {state.catalogSource}
        </p>
        <dl className="mt-2.5 grid grid-cols-2 gap-x-3 gap-y-1.5 text-[11px]">
          <dt className="text-dim">Parameters</dt>
          <dd className="text-right font-mono text-mut">{params}</dd>
          <dt className="text-dim">Block</dt>
          <dd className="text-right font-mono text-mut">{state.catalog.configLength} B</dd>
          <dt className="text-dim">Checksum</dt>
          <dd className="text-right font-mono text-mut">
            {state.catalog.checksum?.enabled ? `sum @ ${state.catalog.checksum.byteIndex}` : "none"}
          </dd>
        </dl>
        <div className="mt-3 flex gap-2">
          <Button size="sm" variant="outline" onClick={onReloadCatalog} className="flex-1" disabled={busy !== null}>
            Reload file
          </Button>
          <Button size="sm" variant="outline" onClick={onImportCatalog} className="flex-1" disabled={busy !== null}>
            Paste JSON
          </Button>
        </div>
        <Button size="sm" variant="ghost" onClick={onImportDump} className="mt-2 w-full justify-start px-2" disabled={busy !== null} icon={<Icon name="hex" className="h-3.5 w-3.5" />}>
          Import a hex dump
        </Button>
      </Section>

      <div className="mt-auto px-4 py-4">
        <div className="rounded-md border border-line bg-panel2 p-3">
          <p className="flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.09em] text-warn">
            <Icon name="warn" className="h-3.5 w-3.5" /> Read-only by design
          </p>
          <p className="mt-1.5 text-[11px] leading-relaxed text-dim">
            The bridge sends identity reads only. Config writes are simulator-only until a profile is verified on a car.
          </p>
        </div>
        <p className="mt-3 text-[10px] leading-relaxed text-[#4d5865]">
          Not affiliated with Volvo, OrBit or any dealer tool. No vehicle data is transmitted anywhere.
        </p>
      </div>
    </aside>
  );
}

