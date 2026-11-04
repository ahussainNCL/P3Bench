import { useMemo, useState } from "react";
import type { CatalogParam, Dump, State, Staged } from "../types";
import { Badge, Button, Icon, cx } from "./Bits";
import { hexBytes } from "../api";

interface Props {
  state: State;
  dump: Dump | null;
  staged: Staged | null;
  onExport: () => void;
}

function coverage(state: State) {
  const map = new Map<number, CatalogParam>();
  for (const group of state.catalog.groups) {
    for (const param of group.params) {
      for (let i = 0; i < param.length; i++) map.set(param.offset + i, param);
    }
  }
  return map;
}

export default function HexPanel({ state, dump, staged, onExport }: Props) {
  const [hover, setHover] = useState<number | null>(null);
  const [copied, setCopied] = useState(false);

  const covered = useMemo(() => coverage(state), [state]);
  const oldBytes = useMemo(() => (dump ? hexBytes(dump.hex) : []), [dump]);
  const newBytes = useMemo(() => (staged ? hexBytes(staged.blockHex) : oldBytes), [staged, oldBytes]);

  const changed = useMemo(() => {
    const set = new Set<number>();
    for (const change of staged?.changes ?? []) {
      const before = change.oldHex.split(/\s+/).filter(Boolean);
      const after = change.newHex.split(/\s+/).filter(Boolean);
      for (let i = 0; i < Math.max(before.length, after.length); i++) {
        if (before[i] !== after[i]) set.add(change.offset + i);
      }
    }
    return set;
  }, [staged]);

  if (!dump) {
    return (
      <div className="flex min-h-0 flex-1 items-center justify-center px-6 text-center">
        <p className="max-w-[220px] text-[11.5px] leading-relaxed text-dim">
          Read or import a block to see the raw bytes, catalog coverage and staged differences.
        </p>
      </div>
    );
  }

  const checksumByte = state.catalog.checksum?.enabled ? state.catalog.checksum.byteIndex : -1;
  const rows = Math.ceil(oldBytes.length / 16);

  async function copy() {
    try {
      await navigator.clipboard.writeText(dump!.hex);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1400);
    } catch {
      /* clipboard blocked; the export button still works */
    }
  }

  const hoverParam = hover !== null ? covered.get(hover) : undefined;

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="border-b border-line px-4 py-3">
        <div className="flex items-center justify-between">
          <p className="text-[12.5px] font-medium text-fg">Raw block</p>
          <div className="flex gap-1">
            <Button size="sm" variant="ghost" onClick={copy} icon={<Icon name="hex" className="h-3.5 w-3.5" />}>
              {copied ? "Copied" : "Copy"}
            </Button>
            <Button size="sm" variant="ghost" onClick={onExport} icon={<Icon name="download" className="h-3.5 w-3.5" />}>
              JSON
            </Button>
          </div>
        </div>
        <p className="mt-0.5 truncate font-mono text-[10px] text-dim" title={dump.provenance}>
          {dump.source} · {dump.length} B
        </p>
      </div>

      <div className="flex flex-wrap gap-x-3 gap-y-1 border-b border-line px-4 py-2 text-[10px] text-dim">
        <span className="flex items-center gap-1">
          <span className="h-2.5 w-2.5 rounded-sm bg-acc" /> staged
        </span>
        <span className="flex items-center gap-1">
          <span className="h-2.5 w-2.5 rounded-sm border border-line2 bg-panel2" /> catalog
        </span>
        {checksumByte >= 0 && (
          <span className="flex items-center gap-1">
            <span className="h-2.5 w-2.5 rounded-sm ring-1 ring-ok" /> checksum
          </span>
        )}
      </div>

      <div className="min-h-0 flex-1 overflow-auto px-3 py-3 font-mono text-[10.5px] leading-[18px]">
        <div className="inline-block">
          <div className="mb-1 flex text-[9px] text-[#747d72]">
            <span className="w-8" />
            {Array.from({ length: 16 }, (_, i) => (
              <span key={i} className="w-[17px] text-center">
                {i.toString(16).toUpperCase()}
              </span>
            ))}
          </div>
          {Array.from({ length: rows }, (_, row) => (
            <div key={row} className="flex items-center">
              <span className="w-8 pr-1.5 text-right text-[9.5px] text-[#737c71]">
                {`${(row * 16).toString(16).toUpperCase().padStart(3, "0")}`}
              </span>
              {Array.from({ length: 16 }, (_, col) => {
                const index = row * 16 + col;
                if (index >= oldBytes.length) return <span key={col} className="w-[17px]" />;
                const isChanged = changed.has(index);
                const isChecksum = index === checksumByte;
                const isParam = covered.has(index);
                return (
                  <span
                    key={col}
                    onMouseEnter={() => setHover(index)}
                    onMouseLeave={() => setHover((h) => (h === index ? null : h))}
                    className={cx(
                      "w-[17px] cursor-default text-center",
                      isChanged
                        ? "rounded-sm bg-acc font-medium text-[#fff0e2]"
                        : isChecksum
                          ? "rounded-sm text-ok ring-1 ring-ok/50"
                          : isParam
                            ? "text-[#404b3e]"
                            : "text-[#727b70]",
                      hover === index && !isChanged && "rounded-sm bg-panel2 text-fg"
                    )}
                  >
                    {newBytes[index]?.toString(16).toUpperCase().padStart(2, "0") ?? "··"}
                  </span>
                );
              })}
            </div>
          ))}
        </div>
      </div>

      <div className="border-t border-line px-4 py-2.5">
        {hover !== null ? (
          <div className="flex items-baseline justify-between gap-2">
            <div className="min-w-0">
              <p className="truncate text-[11.5px] text-fg">{hoverParam?.label ?? "Unmapped byte"}</p>
              <p className="truncate font-mono text-[10px] text-dim">
                0x{hover.toString(16).toUpperCase().padStart(3, "0")} · {hoverParam?.id ?? "—"}
              </p>
            </div>
            <p className="shrink-0 font-mono text-[10.5px] text-mut">
              {oldBytes[hover]?.toString(16).toUpperCase().padStart(2, "0")}
              {changed.has(hover) && (
                <>
                  {" → "}
                  <span className="text-acc">{newBytes[hover]?.toString(16).toUpperCase().padStart(2, "0")}</span>
                </>
              )}
            </p>
          </div>
        ) : (
          <div className="flex items-center justify-between">
            <p className="text-[10.5px] text-dim">Hover a byte to inspect it.</p>
            {state.catalog.checksum?.enabled && dump.checksum?.enabled && (
              <Badge tone={dump.checksum.ok ? "ok" : "bad"} mono>
                sum {dump.checksum.actual?.toString(16).toUpperCase().padStart(2, "0")}
              </Badge>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

