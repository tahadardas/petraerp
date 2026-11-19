import { useEffect, useRef, type ReactNode } from "react";

export interface PetraDialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  actions?: ReactNode;
  width?: number | string;
}

export function PetraDialog({
  open,
  onClose,
  title,
  children,
  actions,
  width = 540,
}: PetraDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open) {
      if (!dialog.open) dialog.showModal();
    } else {
      if (dialog.open) dialog.close();
    }
  }, [open]);

  if (!open) return null;

  return (
    <dialog
      ref={dialogRef}
      onClose={onClose}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.stopPropagation();
          onClose();
        }
      }}
      style={{
        width: typeof width === "number" ? `${width}px` : width,
        maxWidth: "95vw",
        maxHeight: "90vh",
        padding: 0,
        borderRadius: "var(--radius)",
        border: "1px solid var(--border)",
        background: "var(--bg-surface)",
        color: "var(--text)",
        boxShadow: "0 8px 30px rgba(0, 0, 0, 0.25)",
        overflow: "hidden",
        display: "flex",
        flexDirection: "column",
      }}
    >
      <header
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-3) var(--space-4)",
          background: "var(--bg-sunken)",
          borderBottom: "1px solid var(--border)",
        }}
      >
        <h3 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 600 }}>{title}</h3>
        <button
          type="button"
          onClick={onClose}
          aria-label="إغلاق"
          style={{
            background: "none",
            border: 0,
            color: "var(--text-muted)",
            cursor: "pointer",
            fontSize: 16,
            padding: 4,
          }}
        >
          ✕
        </button>
      </header>

      <div style={{ padding: "var(--space-4)", overflowY: "auto", flex: 1 }}>
        {children}
      </div>

      {actions ? (
        <footer
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "flex-end",
            gap: "var(--space-2)",
            padding: "var(--space-3) var(--space-4)",
            background: "var(--bg-sunken)",
            borderTop: "1px solid var(--border)",
          }}
        >
          {actions}
        </footer>
      ) : null}
    </dialog>
  );
}
