import { useEffect, useRef } from "react";
import type { LogEntry } from "../types";
import { Button, Icon, cx } from "./Bits";

interface Props {
  open: boolean;
  entries: LogEntry[];
  onClose: () => void;
  onClear: () => void;
}

export default function LogDrawer({ open, entries, onClose, onClear }: Props) {
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (open) endRef.current?.scrollIntoView({ block: "end" });
  }, [open, entries.length]);

  if (!open) return null;

  return (
    <div className="flex h-[236px] shrink-0 flex-col border-t border-line bg-[#fafbf9]">
      <div className="flex items-center justify-between border-b border-line px-4 py-1.5">
        <div className="flex items-center gap-2">
          <Icon name="terminal" className="h-3.5 w-3.5 text-dim" />
          <span className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-dim">Bridge log</span>
          <span className="font-mono text-[10px] text-[#737c71]">{entries.length} entries</span>
        </div>
        <div className="flex items-center gap-1">
          <Button size="sm" variant="ghost" onClick={onClear}>
            Clear
          </Button>
          <Button size="sm" variant="ghost" onClick={onClose} icon={<Icon name="x" className="h-3.5 w-3.5" />}>
            Close
          </Button>
        </div>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-2 font-mono text-[11px] leading-[1.75]">
        {entries.length === 0 && <p className="text-dim">No bridge output yet.</p>}
        {entries.map((entry) => (
          <div key={entry.seq} className="flex gap-3">
            <span className="shrink-0 text-[#7c8579]">
              {new Date(entry.at).toLocaleTimeString([], { hour12: false })}
            </span>
            <span
              className={cx(
                "w-10 shrink-0 uppercase",
                entry.level === "error" ? "text-bad" : entry.level === "warn" ? "text-warn" : "text-[#6c756a]"
              )}
            >
              {entry.level}
            </span>
            <span className={cx("min-w-0 break-words", entry.level === "error" ? "text-[#af3e30]" : "text-[#535d50]")}>
              {entry.text}
            </span>
          </div>
        ))}
        <div ref={endRef} />
      </div>
    </div>
  );
}
