import type { ButtonHTMLAttributes, ReactNode } from "react";

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "primary" | "ghost" | "outline" | "danger";
  size?: "sm" | "md";
  loading?: boolean;
  icon?: ReactNode;
};

export function Button({
  variant = "outline",
  size = "md",
  loading = false,
  icon,
  className,
  children,
  disabled,
  ...rest
}: ButtonProps) {
  const base =
    "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acc";
  const sizes = size === "sm" ? "h-7 px-2.5 text-[12px]" : "h-9 px-3.5 text-[13px]";
  const variants = {
    primary: "bg-acc text-[#20140a] hover:bg-acc2 font-semibold",
    outline: "border border-line2 bg-panel2 text-fg hover:border-[#3d4a58] hover:bg-[#1b222a]",
    ghost: "text-mut hover:text-fg hover:bg-panel2",
    danger: "border border-[#58302f] bg-[#241716] text-bad hover:bg-[#2d1b1a]",
  }[variant];

  return (
    <button className={cx(base, sizes, variants, className)} disabled={disabled || loading} {...rest}>
      {loading ? <Spinner /> : icon}
      {children}
    </button>
  );
}

export function Spinner({ className }: { className?: string }) {
  return (
    <svg className={cx("h-3.5 w-3.5 animate-spin", className)} viewBox="0 0 24 24" fill="none" aria-hidden>
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.25" strokeWidth="3" />
      <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}

export function Badge({
  tone = "neutral",
  mono = false,
  children,
  title,
}: {
  tone?: "neutral" | "ok" | "warn" | "bad" | "acc";
  mono?: boolean;
  children: ReactNode;
  title?: string;
}) {
  const tones = {
    neutral: "border-line2 bg-panel2 text-mut",
    ok: "border-[#1f4a3a] bg-[#12231d] text-ok",
    warn: "border-[#4d4020] bg-[#241f10] text-warn",
    bad: "border-[#4d2726] bg-[#241413] text-bad",
    acc: "border-[#54331d] bg-[#26190f] text-acc",
  }[tone];
  return (
    <span
      title={title}
      className={cx(
        "inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[10.5px] font-medium uppercase tracking-[0.08em]",
        tones,
        mono && "font-mono normal-case tracking-normal"
      )}
    >
      {children}
    </span>
  );
}

export function Switch({
  checked,
  onChange,
  disabled,
  label,
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
  label: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cx(
        "relative h-6 w-11 shrink-0 rounded-full border transition-colors disabled:cursor-not-allowed disabled:opacity-40",
        checked ? "border-[#7a4a22] bg-acc" : "border-line2 bg-[#1d242c]"
      )}
    >
      <span
        className={cx(
          "absolute top-0.5 rounded-full bg-[#f4f7fa] shadow transition-all",
          checked ? "left-[22px]" : "left-0.5"
        )}
        style={{ height: 18, width: 18 }}
      />
    </button>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-[10.5px] font-semibold uppercase tracking-[0.09em] text-dim">
        {label}
      </span>
      {children}
    </label>
  );
}

export function Kbd({ children }: { children: ReactNode }) {
  return (
    <kbd className="rounded border border-line2 bg-panel2 px-1 py-0.5 font-mono text-[10px] text-mut">
      {children}
    </kbd>
  );
}

export function Icon({ name, className }: { name: "car" | "chip" | "plug" | "bolt" | "warn" | "check" | "x" | "download" | "book" | "chevron" | "terminal" | "hex" | "list"; className?: string }) {
  const paths: Record<string, ReactNode> = {
    car: (
      <>
        <path d="M5 17h14M6 17l1-5h10l1 5M4 17v-4l1.6-4.2A2 2 0 0 1 7.5 7.5h9a2 2 0 0 1 1.9 1.3L20 13v4" />
        <circle cx="8" cy="17.5" r="1.2" />
        <circle cx="16" cy="17.5" r="1.2" />
      </>
    ),
    chip: (
      <>
        <rect x="7" y="7" width="10" height="10" rx="1.5" />
        <path d="M10 3v4M14 3v4M10 17v4M14 17v4M3 10h4M3 14h4M17 10h4M17 14h4" />
      </>
    ),
    plug: (
      <>
        <path d="M9 3v5M15 3v5" />
        <path d="M6 8h12v3a6 6 0 0 1-6 6 6 6 0 0 1-6-6V8Z" />
        <path d="M12 17v4" />
      </>
    ),
    bolt: <path d="M13 3 5 14h5l-1 7 8-11h-5l1-7Z" />,
    warn: (
      <>
        <path d="M12 4 2.5 20h19L12 4Z" />
        <path d="M12 10v4M12 17h.01" />
      </>
    ),
    check: <path d="m4 12 5 5L20 6" />,
    x: <path d="M6 6l12 12M18 6 6 18" />,
    download: <path d="M12 4v11m0 0 4-4m-4 4-4-4M4 19h16" />,
    book: (
      <>
        <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5v-15Z" />
        <path d="M4 20.5A2.5 2.5 0 0 1 6.5 18H20v3H6.5A2.5 2.5 0 0 1 4 20.5Z" />
      </>
    ),
    chevron: <path d="m9 6 6 6-6 6" />,
    terminal: (
      <>
        <rect x="3" y="4" width="18" height="16" rx="2" />
        <path d="m7 9 3 3-3 3M13 15h4" />
      </>
    ),
    hex: <path d="m12 3 7 4v10l-7 4-7-4V7l7-4ZM12 3v18M5 7l14 10M19 7 5 17" />,
    list: <path d="M8 6h13M8 12h13M8 18h13M3.5 6h.01M3.5 12h.01M3.5 18h.01" />,
  };
  return (
    <svg
      className={cx("h-4 w-4", className)}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      {paths[name]}
    </svg>
  );
}

