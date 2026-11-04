import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ApiError, api } from "./api";
import type { Edit, LogEntry, State } from "./types";
import { Badge, Button, Icon, cx } from "./components/Bits";
import Sidebar from "./components/Sidebar";
import ConfigPanel from "./components/ConfigPanel";
import PlanPanel from "./components/PlanPanel";
import HexPanel from "./components/HexPanel";
import LogDrawer from "./components/LogDrawer";
import ImportDialog from "./components/ImportDialog";

type Value = boolean | number | string;
type Toast = { id: number; tone: "ok" | "bad"; text: string };

export default function App() {
  const [state, setState] = useState<State | null>(null);
  const [offline, setOffline] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [toast, setToast] = useState<Toast | null>(null);

  const [draft, setDraft] = useState<Record<string, Value>>({});
  const [stale, setStale] = useState(false);
  const [confirm, setConfirm] = useState("");
  const [filter, setFilter] = useState("");
  const [pendingOnly, setPendingOnly] = useState(false);
  const [rightTab, setRightTab] = useState<"plan" | "hex">(() =>
    typeof window !== "undefined" && window.location.hash === "#bytes" ? "hex" : "plan"
  );

  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [logOpen, setLogOpen] = useState(false);
  const [setupOpen, setSetupOpen] = useState(false);
  const [panelOpen, setPanelOpen] = useState(false);
  const [dialog, setDialog] = useState<"dump" | "catalog" | null>(null);
  const [dialogError, setDialogError] = useState<string | null>(null);

  const logSeq = useRef(0);
  const toastTimer = useRef<number | null>(null);

  const notify = useCallback((tone: "ok" | "bad", text: string) => {
    if (toastTimer.current) window.clearTimeout(toastTimer.current);
    setToast({ id: Date.now(), tone, text });
    toastTimer.current = window.setTimeout(() => setToast(null), tone === "bad" ? 7000 : 3500);
  }, []);

  const load = useCallback(async () => {
    try {
      const next = await api.state();
      setState(next);
      setOffline(null);
    } catch (error) {
      setOffline(error instanceof ApiError ? error.message : String(error));
    }
  }, []);

  useEffect(() => {
    void load();
    const timer = window.setInterval(load, 15000);
    return () => window.clearInterval(timer);
  }, [load]);

  useEffect(() => {
    const timer = window.setInterval(async () => {
      try {
        const result = await api.log(logSeq.current);
        if (result.entries.length > 0) {
          logSeq.current = result.seq;
          setLogs((previous) => [...previous, ...result.entries].slice(-400));
        }
      } catch {
        /* the offline banner already reports a dead bridge */
      }
    }, 1500);
    return () => window.clearInterval(timer);
  }, []);

  const act = useCallback(
    async (name: string, work: () => Promise<State>, resetDraft = false) => {
      setBusy(name);
      try {
        const next = await work();
        setState(next);
        setOffline(null);
        if (resetDraft) {
          setDraft({});
          setConfirm("");
          setStale(false);
        }
        return next;
      } catch (error) {
        notify("bad", error instanceof Error ? error.message : String(error));
        return null;
      } finally {
        setBusy(null);
      }
    },
    [notify]
  );

  const params = useMemo(() => state?.catalog.groups.flatMap((group) => group.params) ?? [], [state]);
  const pendingEdits = useMemo<Edit[]>(() => {
    const edits: Edit[] = [];
    for (const param of params) {
      const value = draft[param.id];
      if (value !== undefined && value !== param.value) edits.push({ id: param.id, value });
    }
    return edits;
  }, [params, draft]);

  function setValue(id: string, value: Value) {
    setDraft((previous) => ({ ...previous, [id]: value }));
    setConfirm("");
    if (state?.staged) setStale(true);
  }

  async function preview() {
    if (pendingEdits.length === 0) {
      notify("bad", "No pending changes to preview.");
      return;
    }
    const next = await act("plan", () => api.plan(pendingEdits));
    if (next) {
      setStale(false);
      setRightTab("plan");
      setPanelOpen(true);
      notify("ok", "Plan staged. Nothing has been written.");
    }
  }

  async function apply() {
    if (!state?.staged) return;
    const before = state.catalog.groups
      .flatMap((group) => group.params)
      .filter((param) => draft[param.id] !== undefined && draft[param.id] !== param.value)
      .map((param) => `${param.label} → ${String(draft[param.id])}`);
    const next = await act("apply", () => api.apply(state.staged!.id, confirm), true);
    if (next) notify("ok", `Applied ${before.length} change(s) to the simulator.`);
  }

  async function importText(mode: "dump" | "catalog", text: string) {
    setDialogError(null);
    try {
      const next = mode === "dump" ? await api.importDump(text) : await api.importCatalog(text);
      setState(next);
      setDraft({});
      setConfirm("");
      setStale(false);
      setDialog(null);
      setRightTab(mode === "dump" ? "hex" : "plan");
      notify("ok", mode === "dump" ? "Block decoded with the active catalog." : "Catalog loaded.");
    } catch (error) {
      setDialogError(error instanceof Error ? error.message : String(error));
    }
  }

  const exportJson = useCallback(() => {
    if (!state) return;
    const payload = {
      exportedAt: new Date().toISOString(),
      app: "P3Bench web",
      bridge: state.bridge.version,
      mode: state.mode,
      profile: state.profile,
      vehicle: state.vehicle,
      catalog: {
        id: state.catalog.id,
        label: state.catalog.label,
        synthetic: state.catalog.synthetic,
        configLength: state.catalog.configLength,
        checksum: state.catalog.checksum,
      },
      dump: state.dump,
      staged: state.staged,
      pending: draft,
      warnings: state.warnings,
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `p3-block-${state.dump?.id ?? "empty"}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }, [state, draft]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        exportJson();
      }
      if (event.key === "Escape") {
        setDialog(null);
        setLogOpen(false);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [exportJson]);

  if (offline && !state) {
    return (
      <div className="grid-lines flex h-full items-center justify-center p-8">
        <div className="w-full max-w-lg rounded-lg border border-line bg-panel p-8">
          <Badge tone="bad">bridge offline</Badge>
          <h1 className="mt-4 text-[20px] font-semibold text-fg">The local bridge is not answering</h1>
          <p className="mt-2 text-[12.5px] leading-relaxed text-mut">{offline}</p>
          <pre className="mt-4 overflow-x-auto rounded border border-line bg-ink px-4 py-3 font-mono text-[11.5px] leading-relaxed text-[#9fb0c1]">
{`cd bridge
build.cmd
P3OrbitBridge.exe --open`}
          </pre>
          <p className="mt-3 text-[11px] leading-relaxed text-dim">
            The UI talks to <span className="font-mono">127.0.0.1:8787</span> only. No cloud service, no account, no telemetry.
          </p>
          <Button variant="primary" className="mt-5" onClick={() => void load()}>
            Retry
          </Button>
        </div>
      </div>
    );
  }

  if (!state) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="flex items-center gap-3 font-mono text-[12px] text-dim">
          <span className="h-2 w-2 animate-ping rounded-full bg-acc" />
          connecting to bridge…
        </div>
      </div>
    );
  }

  const connected = state.connection.state === "connected";
  const lastLog = logs[logs.length - 1];

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <header className="flex h-14 shrink-0 items-center gap-3 border-b border-line bg-panel px-4">
        <div className="flex items-center gap-2.5">
          <span className="flex h-8 w-8 items-center justify-center rounded-md border border-[#4a3524] bg-gradient-to-br from-[#2a1c12] to-[#17120d] text-acc">
            <Icon name="hex" className="h-4 w-4" />
          </span>
          <div className="leading-none">
            <h1 className="text-[13.5px] font-bold tracking-[0.12em] text-fg">P3BENCH</h1>
            <p className="mt-1 text-[9.5px] uppercase tracking-[0.16em] text-dim">Config studio · web</p>
          </div>
        </div>

        <div className="mx-2 h-6 w-px bg-line" />

        <Button
          size="sm"
          variant="ghost"
          className="lg:hidden"
          onClick={() => setSetupOpen(true)}
          icon={<Icon name="chip" className="h-3.5 w-3.5" />}
        >
          Setup
        </Button>

        <Badge tone={state.mode === "simulator" ? "warn" : connected ? "ok" : "neutral"}>
          {state.mode === "simulator" ? "simulator" : connected ? "vehicle connected" : "hardware"}
        </Badge>
        <span className="hidden font-mono text-[10.5px] text-dim lg:inline">{state.profile.id}</span>

        <div className="ml-auto flex items-center gap-2">
          <span className="hidden items-center gap-1.5 rounded border border-line bg-ink px-2 py-1 font-mono text-[10px] text-dim xl:flex">
            <span className={cx("h-1.5 w-1.5 rounded-full", offline ? "bg-bad" : "bg-ok")} />
            127.0.0.1:{state.bridge.port}
          </span>
          <Button
            size="sm"
            variant="outline"
            className="lg:hidden"
            onClick={() => setPanelOpen(true)}
            icon={<Icon name="list" className="h-3.5 w-3.5" />}
          >
            {rightTab === "hex" ? "Bytes" : "Plan"}
            {state.staged && rightTab === "plan" && (
              <span className="rounded bg-[#2a1c12] px-1 font-mono text-[10px] text-acc">{state.staged.changes.length}</span>
            )}
          </Button>
          <Button size="sm" variant="ghost" onClick={() => setLogOpen((open) => !open)} icon={<Icon name="terminal" className="h-3.5 w-3.5" />}>
            Log
          </Button>
          <Button size="sm" variant="outline" onClick={exportJson} disabled={!state.dump} icon={<Icon name="download" className="h-3.5 w-3.5" />}>
            Export
          </Button>
        </div>
      </header>

      {state.warnings.length > 0 && (
        <div className="flex h-8 shrink-0 items-center gap-2 border-b border-[#3d3319] bg-[#191509] px-4 text-[10.5px] text-warn">
          <Icon name="warn" className="h-3.5 w-3.5 shrink-0" />
          <span className="truncate">{state.warnings[0]}</span>
          {state.warnings.length > 1 && (
            <span className="shrink-0 text-[#8a7534]" title={state.warnings.slice(1).join("\n")}>
              +{state.warnings.length - 1} more
            </span>
          )}
        </div>
      )}

      <div className="relative flex min-h-0 flex-1">
        {setupOpen && (
          <div className="fixed inset-0 z-30 bg-black/60 lg:hidden" onClick={() => setSetupOpen(false)} />
        )}

        <div
          className={cx(
            "z-40 shrink-0 lg:static lg:block",
            setupOpen ? "fixed inset-y-0 left-0 block shadow-2xl shadow-black/60" : "hidden lg:block"
          )}
        >
          <Sidebar
            state={state}
            busy={busy}
            onMode={(mode) => void act("mode", () => api.setMode(mode), true)}
            onSelectAdapter={(library) => void act("adapter", () => api.selectAdapter(library))}
            onRefreshAdapters={() => void act("adapters", () => api.refreshAdapters())}
            onConnect={() => void act("connect", () => api.connect())}
            onDisconnect={() => void act("disconnect", () => api.disconnect(), true)}
            onReloadCatalog={() => void act("catalog", () => api.reloadCatalog(), true)}
            onImportCatalog={() => {
              setDialogError(null);
              setDialog("catalog");
            }}
            onImportDump={() => {
              setDialogError(null);
              setDialog("dump");
            }}
          />
        </div>

        <main className="flex min-w-0 flex-1 flex-col">
          <ConfigPanel
            state={state}
            draft={draft}
            onDraft={setValue}
            filter={filter}
            onFilter={setFilter}
            pendingOnly={pendingOnly}
            onPendingOnly={setPendingOnly}
            onPreview={() => void preview()}
            onRead={() => void act("read", () => api.read(), true)}
            onImportDump={() => {
              setDialogError(null);
              setDialog("dump");
            }}
            busy={busy}
          />
        </main>

        {panelOpen && (
          <div className="fixed inset-0 z-30 bg-black/60 lg:hidden" onClick={() => setPanelOpen(false)} />
        )}
        <aside
          className={cx(
            "z-40 w-[372px] max-w-[92vw] shrink-0 flex-col border-l border-line bg-panel lg:static lg:flex",
            panelOpen ? "fixed inset-y-0 right-0 flex shadow-2xl shadow-black/60" : "hidden lg:flex"
          )}
        >
          <div className="flex h-[46px] shrink-0 items-center gap-1 border-b border-line px-3">
            {(["plan", "hex"] as const).map((tab) => (
              <button
                key={tab}
                onClick={() => setRightTab(tab)}
                className={cx(
                  "flex h-7 items-center gap-1.5 rounded px-2.5 text-[11.5px] font-medium transition-colors",
                  rightTab === tab ? "bg-panel2 text-fg ring-1 ring-line2" : "text-mut hover:text-fg"
                )}
              >
                <Icon name={tab === "plan" ? "check" : "hex"} className="h-3.5 w-3.5" />
                {tab === "plan" ? "Write plan" : "Bytes"}
                {tab === "plan" && state.staged && (
                  <span className="rounded bg-[#2a1c12] px-1 font-mono text-[10px] text-acc">{state.staged.changes.length}</span>
                )}
              </button>
            ))}
            <span className="ml-auto font-mono text-[10px] text-[#4d5865]">{state.catalog.configLength} B</span>
            <button
              onClick={() => setPanelOpen(false)}
              className="ml-1 rounded p-1 text-dim hover:text-fg lg:hidden"
              aria-label="Close panel"
            >
              <Icon name="x" className="h-3.5 w-3.5" />
            </button>
          </div>

          {rightTab === "plan" ? (
            <PlanPanel
              state={state}
              staged={state.staged}
              pending={pendingEdits.length}
              stale={stale}
              confirm={confirm}
              onConfirm={setConfirm}
              onApply={() => void apply()}
              onDiscard={() => void act("discard", () => api.discard(), true)}
              onPreview={() => void preview()}
              onUndo={() => void act("undo", () => api.undo(), true)}
              busy={busy}
            />
          ) : (
            <HexPanel state={state} dump={state.dump} staged={state.staged} onExport={exportJson} />
          )}
        </aside>
      </div>

      <LogDrawer
        open={logOpen}
        entries={logs}
        onClose={() => setLogOpen(false)}
        onClear={() => {
          setLogs([]);
          logSeq.current = 0;
        }}
      />

      <footer className="flex h-8 shrink-0 items-center gap-3 border-t border-line bg-panel px-4 font-mono text-[10px] text-[#546170]">
        <button onClick={() => setLogOpen((open) => !open)} className="flex items-center gap-1.5 hover:text-mut">
          <Icon name="terminal" className="h-3 w-3" />
          {lastLog ? (
            <span className="max-w-[520px] truncate">
              <span className={lastLog.level === "error" ? "text-bad" : lastLog.level === "warn" ? "text-warn" : "text-[#7a8797]"}>
                {lastLog.text}
              </span>
            </span>
          ) : (
            <span>bridge ready</span>
          )}
        </button>
        <span className="ml-auto flex items-center gap-3">
          <span>{params.length} params</span>
          <span className="text-[#37424f]">|</span>
          <span>bridge {state.bridge.version}</span>
          <span className="text-[#37424f]">|</span>
          <span>local only</span>
        </span>
      </footer>

      <ImportDialog
        mode={dialog}
        busy={busy === "import"}
        error={dialogError}
        configLength={state.catalog.configLength}
        onSubmit={(mode, text) => {
          setBusy("import");
          void importText(mode, text).finally(() => setBusy(null));
        }}
        onClose={() => setDialog(null)}
      />

      {toast && (
        <div
          key={toast.id}
          className={cx(
            "fixed bottom-12 left-1/2 z-50 flex max-w-xl -translate-x-1/2 items-start gap-2 rounded-md border px-3.5 py-2.5 text-[12px] shadow-xl shadow-black/50",
            toast.tone === "ok"
              ? "border-[#1f4a3a] bg-[#102019] text-ok"
              : "border-[#4d2726] bg-[#241413] text-[#ffb3b3]"
          )}
        >
          <Icon name={toast.tone === "ok" ? "check" : "warn"} className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span className="leading-relaxed">{toast.text}</span>
        </div>
      )}
    </div>
  );
}
