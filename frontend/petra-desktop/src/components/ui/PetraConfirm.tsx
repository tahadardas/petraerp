import { PetraButton } from "./PetraButton";
import { PetraDialog } from "./PetraDialog";

export interface PetraConfirmProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function PetraConfirm({
  open,
  title,
  message,
  confirmLabel = "تأكيد",
  cancelLabel = "إلغاء",
  destructive = false,
  onConfirm,
  onCancel,
}: PetraConfirmProps) {
  return (
    <PetraDialog
      open={open}
      onClose={onCancel}
      title={title}
      width={440}
      actions={
        <>
          <PetraButton variant="secondary" onClick={onCancel}>
            {cancelLabel}
          </PetraButton>
          <PetraButton
            variant={destructive ? "danger" : "primary"}
            onClick={() => {
              onConfirm();
              onCancel();
            }}
          >
            {confirmLabel}
          </PetraButton>
        </>
      }
    >
      <div style={{ fontSize: "var(--fs-body)", lineHeight: 1.6, color: "var(--text)" }}>
        {message}
      </div>
    </PetraDialog>
  );
}
