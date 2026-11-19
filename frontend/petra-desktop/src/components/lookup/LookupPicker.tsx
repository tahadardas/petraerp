import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from "react";

export interface LookupItem {
  id: string;
  code: string;
  name: string;
}

export interface LookupPickerProps {
  /** Instant search against the API. Must honour the signal: a newer keystroke aborts the older request. `query === ""` lists everything. */
  search: (query: string, signal: AbortSignal) => Promise<readonly LookupItem[]>;
  value: LookupItem | null;
  onChange: (item: LookupItem | null) => void;
  ariaLabel: string;
  placeholder?: string;
  readOnly?: boolean;
  /** Milliseconds to wait after the last keystroke before searching (instant feel without a request per key). */
  debounceMs?: number;
  /** Focus automatically (used when the grid moves into the cell). */
  autoFocus?: boolean;
}

const display = (item: LookupItem) => `${item.code} ${item.name}`.trim();

/**
 * Accountant-speed picker: type part of a code or name and the matches appear at once; ArrowUp/Down highlight, Enter commits,
 * Escape closes. `*` (or Enter on an empty box) opens the full list. Requests are cancellable and never overwrite a newer answer.
 */
export function LookupPicker({ search, value, onChange, ariaLabel, placeholder, readOnly = false, debounceMs = 120, autoFocus = false }: LookupPickerProps) {
  const listId = useId();
  const [text, setText] = useState(value ? display(value) : "");
  const [open, setOpen] = useState(false);
  const [results, setResults] = useState<readonly LookupItem[]>([]);
  const [highlight, setHighlight] = useState(0);
  const [state, setState] = useState<"idle" | "loading" | "error">("idle");
  const controller = useRef<AbortController | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  // The visible text follows the committed value (e.g. cleared from outside).
  useEffect(() => setText(value ? display(value) : ""), [value]);

  const run = useCallback(
    (query: string, immediate: boolean) => {
      if (timer.current) clearTimeout(timer.current);
      controller.current?.abort();
      const go = () => {
        const current = new AbortController();
        controller.current = current;
        setState("loading");
        search(query, current.signal).then(
          (items) => {
            if (current.signal.aborted) return; // a newer request owns the list
            setResults(items);
            setHighlight(0);
            setState("idle");
            setOpen(true);
          },
          (error: unknown) => {
            if (current.signal.aborted || (error instanceof DOMException && error.name === "AbortError")) return;
            setState("error");
            setResults([]);
          },
        );
      };
      if (immediate || debounceMs <= 0) go();
      else timer.current = setTimeout(go, debounceMs);
    },
    [search, debounceMs],
  );

  useEffect(
    () => () => {
      controller.current?.abort();
      if (timer.current) clearTimeout(timer.current);
    },
    [],
  );

  const commit = (item: LookupItem) => {
    onChange(item);
    setText(display(item));
    setOpen(false);
  };

  const onInputChange = (next: string) => {
    if (readOnly) return;
    if (next === "*") {
      setText("");
      run("", true);
      return;
    }
    setText(next);
    if (value) onChange(null); // editing the text un-selects the previous value
    if (next.trim() === "") {
      controller.current?.abort();
      setOpen(false);
      return;
    }
    run(next.trim(), false);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (readOnly) return;
    if (event.key === "ArrowDown") {
      event.preventDefault();
      if (!open) run(text.trim(), true);
      else setHighlight((h) => Math.min(h + 1, Math.max(results.length - 1, 0)));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (event.key === "Enter") {
      const chosen = results[highlight];
      if (open && chosen) {
        // Commit and keep the key from also moving the surrounding grid cell: the next Enter does that.
        event.preventDefault();
        event.stopPropagation();
        commit(chosen);
      } else if (text.trim() === "") {
        event.preventDefault();
        event.stopPropagation();
        run("", true);
      }
    } else if (event.key === "Escape" && open) {
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
      setText(value ? display(value) : "");
    }
  };

  return (
    <div style={{ position: "relative", width: "100%" }}>
      <input
        role="combobox"
        aria-label={ariaLabel}
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-busy={state === "loading"}
        aria-invalid={state === "error"}
        autoFocus={autoFocus}
        readOnly={readOnly}
        value={text}
        placeholder={placeholder ?? ""}
        onChange={(e) => onInputChange(e.target.value)}
        onKeyDown={onKeyDown}
        onBlur={() => setTimeout(() => setOpen(false), 120)}
        style={{ width: "100%", boxSizing: "border-box", height: "var(--control-height)", background: "var(--bg-input)", color: "var(--text)", border: "1px solid var(--border)", borderRadius: "var(--radius)", paddingInline: "var(--space-2)" }}
      />
      {open ? (
        <ul
          id={listId}
          role="listbox"
          aria-label={ariaLabel}
          style={{ position: "absolute", insetInlineStart: 0, zIndex: 20, margin: 0, padding: 0, listStyle: "none", minWidth: "100%", maxHeight: 280, overflow: "auto", background: "var(--bg-input)", border: "1px solid var(--border)", borderRadius: "var(--radius)" }}
        >
          {results.length === 0 ? (
            <li role="option" aria-disabled="true" aria-selected="false" style={{ padding: "var(--space-2)", color: "var(--text-muted)" }}>
              {state === "error" ? "!" : "—"}
            </li>
          ) : (
            results.map((item, index) => (
              <li
                key={item.id}
                role="option"
                aria-selected={index === highlight}
                onMouseDown={(e) => {
                  e.preventDefault();
                  commit(item);
                }}
                style={{ padding: "var(--space-1) var(--space-2)", cursor: "pointer", background: index === highlight ? "var(--bg-sunken)" : "transparent" }}
              >
                <span className="numeric" style={{ marginInlineEnd: "var(--space-2)", color: "var(--text-muted)" }}>{item.code}</span>
                {item.name}
              </li>
            ))
          )}
        </ul>
      ) : null}
    </div>
  );
}
