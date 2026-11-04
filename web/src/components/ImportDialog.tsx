import { useEffect, useRef, useState } from "react";
import { Badge, Button, Field, Icon } from "./Bits";

type Mode = "dump" | "catalog";

interface Props {
  mode: Mode | null;
  busy: boolean;
  error: string | null;
  onSubmit: (mode: Mode, text: string) => void;
  onClose: () => void;
  configLength: number;
}

export default function ImportDialog({ mode, busy, error, onSubmit, onClose, configLength }: Props) {
  const [text, setText] = useState("");
  const areaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (mode) {
      setText("");
      window.setTimeout(() => areaRef.current?.focus(), 40);
    }
  }, [mode]);

  if (!mode) return null;

  const submit = () => {
    if (text.trim().length > 0) onSubmit(mode, text);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-6 backdrop-blur-sm" onClick={onClose}>
      <div
        className="w-full max-w-2xl rounded-lg border border-line bg-panel shadow-2xl shadow-black/10"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between border-b border-line px-5 py-4">
          <div>
            <h2 className="flex items-center gap-2 text-[14px] font-semibold text-fg">
              <Icon name={mode === "dump" ? "hex" : "book"} className="h-4 w-4 text-acc" />
              {mode === "dump" ? "Import a config block" : "Import a catalog"}
            </h2>
            <p className="mt-1 text-[11.5px] leading-relaxed text-mut">
              {mode === "dump"
                ? `Paste ${configLength} bytes of hex. Spaces, commas, colons and 0x prefixes are fine. Nothing is uploaded.`
                : "Paste a catalog JSON document. It is validated (ids, bounds, overlaps, checksum) before it replaces the active catalog."}
            </p>
          </div>
          <button onClick={onClose} className="text-dim hover:text-fg" aria-label="Close">
            <Icon name="x" />
          </button>
        </div>

        <div className="px-5 py-4">
          <Field label={mode === "dump" ? "Hex bytes" : "Catalog JSON"}>
            <textarea
              ref={areaRef}
              value={text}
              onChange={(e) => setText(e.target.value)}
              spellCheck={false}
              placeholder={mode === "dump" ? "C8 0C 7B 92 20 56 86 7F …" : '{ "id": "my.p3", "configLength": 254, "groups": [ … ] }'}
              className="h-56 w-full resize-none rounded border border-line2 bg-ink px-3 py-2 font-mono text-[11.5px] leading-relaxed text-fg outline-none placeholder:text-[#7c8579] focus:border-[#c97c43]"
            />
          </Field>

          {mode === "dump" && (
            <p className="mt-2 flex items-start gap-2 text-[10.5px] leading-relaxed text-dim">
              <Badge tone="warn">local</Badge>
              Imported bytes are decoded with the active catalog. Values are not verified against a vehicle.
            </p>
          )}

          {error && (
            <p className="mt-3 rounded border border-[#ecc9c2] bg-[#fff3f1] px-3 py-2 font-mono text-[11px] leading-relaxed text-bad">
              {error}
            </p>
          )}
        </div>

        <div className="flex items-center justify-between border-t border-line px-5 py-3">
          <p className="font-mono text-[10px] text-[#737c71]">
            {text.trim() ? `${text.trim().length.toLocaleString()} characters` : "nothing pasted"}
          </p>
          <div className="flex gap-2">
            <Button variant="ghost" onClick={onClose}>
              Cancel
            </Button>
            <Button variant="primary" onClick={submit} loading={busy} disabled={!text.trim()}>
              {mode === "dump" ? "Decode block" : "Use catalog"}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
