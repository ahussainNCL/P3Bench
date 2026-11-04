import type { State, Staged } from "../types";
import { Badge, Button, Icon, Kbd } from "./Bits";
import { cx } from "./Bits";

interface Props {
  state: State;
  staged: Staged | null;
  pending: number;
  stale: boolean;
  confirm: string;
  onConfirm: (value: string) => void;
  onApply: () => void;
  onDiscard: () => void;
  onPreview: () => void;
  onUndo: () => void;
  busy: string | null;
}

export default function PlanPanel({
  state,
  staged,
  pending,
  stale,
  confirm,
  onConfirm,
  onApply,
  onDiscard,
  onPreview,
  onUndo,
  busy,
}: Props) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {!staged ? (
        <div className="flex min-h-0 flex-1 flex-col items-center justify-center px-6 text-center">
          <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-full border border-line bg-panel2 text-dim">
            <Icon name="check" className="h-4 w-4" />
          </div>
          <p className="text-[13px] font-medium text-fg">Nothing staged</p>
          <p className="mt-1 max-w-[240px] text-[11.5px] leading-relaxed text-dim">
            {pending > 0
              ? `${pending} pending change${pending === 1 ? "" : "s"}. Preview to see the exact bytes and checksum before applying.`
              : stale
                ? "Parameters changed since the plan was built. Preview again to refresh it."
                : "Change parameters in the editor, then preview the write plan. Nothing is sent to a car."}
          </p>
          {pending > 0 && (
            <Button size="sm" variant="outline" className="mt-4" onClick={onPreview} loading={busy === "plan"}>
              Preview plan
            </Button>
          )}
          {state.capabilities.undo && (
            <Button
              size="sm"
              variant="ghost"
              className="mt-2"
              onClick={onUndo}
              loading={busy === "undo"}
              icon={<Icon name="undo" className="h-3.5 w-3.5" />}
            >
              Undo last simulated write
              {state.capabilities.undoDepth > 1 ? ` (${state.capabilities.undoDepth})` : ""}
            </Button>
          )}
        </div>
      ) : (
        <>
          <div className="border-b border-line px-4 py-3">
            <div className="flex items-center justify-between gap-2">
              <p className="text-[12.5px] font-medium text-fg">{staged.summary}</p>
              <Badge tone="acc">staged</Badge>
            </div>
            <p className="mt-0.5 font-mono text-[10px] text-dim">
              {staged.id} · {staged.changedBytes} byte{staged.changedBytes === 1 ? "" : "s"} differ
            </p>
            {stale && (
              <p className="mt-2 rounded border border-[#e4cfa1] bg-[#fff9ed] px-2 py-1.5 text-[10.5px] leading-relaxed text-warn">
                The editor changed after this plan was built. Preview again before applying.
              </p>
            )}
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto">
            {staged.changes.map((change) => (
              <div key={change.paramId} className="border-b border-line/60 px-4 py-2.5">
                <div className="flex items-baseline justify-between gap-2">
                  <p className="truncate text-[12px] text-fg">{change.label}</p>
                  <span className="shrink-0 font-mono text-[10px] text-dim">
                    0x{change.offset.toString(16).toUpperCase().padStart(3, "0")}
                  </span>
                </div>
                <div className="mt-1 flex items-center gap-1.5 font-mono text-[10.5px]">
                  <span className="rounded bg-[#f0f1ee] px-1.5 py-0.5 text-mut line-through decoration-[#ad7067]">
                    {change.oldHex}
                  </span>
                  <span className="text-dim">→</span>
                  <span className="rounded bg-[#fff1e5] px-1.5 py-0.5 text-acc">{change.newHex}</span>
                  {change.kind === "checksum" && <Badge tone="ok">recomputed</Badge>}
                </div>
                <p className="mt-1 truncate text-[10.5px] text-dim">
                  {change.oldDisplay} → <span className="text-mut">{change.newDisplay}</span>
                </p>
              </div>
            ))}
          </div>

          <div className="border-t border-line bg-panel px-4 py-3.5">
            {state.capabilities.apply ? (
              <>
                <label className="block text-[10.5px] font-semibold uppercase tracking-[0.09em] text-dim">
                  Type APPLY to write
                </label>
                <div className="mt-1.5 flex gap-2">
                  <input
                    value={confirm}
                    onChange={(e) => onConfirm(e.target.value)}
                    placeholder="APPLY"
                    className="h-9 min-w-0 flex-1 rounded border border-line2 bg-ink px-2.5 font-mono text-[12px] tracking-[0.2em] text-fg outline-none placeholder:text-[#7c8579] focus:border-[#c97c43]"
                    spellCheck={false}
                    autoComplete="off"
                  />
                  <Button
                    variant="primary"
                    onClick={onApply}
                    disabled={stale || confirm.trim() !== "APPLY" || busy !== null}
                    loading={busy === "apply"}
                  >
                    Apply
                  </Button>
                </div>
                <div className="mt-2 flex items-center justify-between">
                  <p className="text-[10px] leading-relaxed text-dim">{state.capabilities.applyReason}</p>
                  <button onClick={onDiscard} className="shrink-0 text-[10.5px] text-dim underline-offset-2 hover:text-mut hover:underline">
                    discard
                  </button>
                </div>
              </>
            ) : (
              <div className="rounded border border-[#ecc9c2] bg-[#fff3f1] px-2.5 py-2">
                <p className="flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.08em] text-bad">
                  <Icon name="warn" className="h-3.5 w-3.5" /> writes disabled
                </p>
                <p className="mt-1 text-[11px] leading-relaxed text-mut">{state.capabilities.applyReason}</p>
                <button onClick={onDiscard} className="mt-2 text-[10.5px] text-dim hover:text-mut">
                  Discard plan
                </button>
              </div>
            )}
            <p className="mt-2 text-[10px] text-[#7c8579]">
              Export the read block: <Kbd>Ctrl</Kbd> <Kbd>S</Kbd>
            </p>
          </div>
        </>
      )}
    </div>
  );
}
