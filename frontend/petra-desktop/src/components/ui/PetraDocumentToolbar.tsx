import { PetraButton } from "./PetraButton";

export type DocumentPostingStatus = "draft" | "pending" | "posted" | "reversed" | "rejected";

export interface PetraDocumentToolbarProps {
  status?: DocumentPostingStatus;
  canSave?: boolean;
  canPost?: boolean;
  canReverse?: boolean;
  canDelete?: boolean;
  canPrint?: boolean;
  isSaving?: boolean;
  isPosting?: boolean;
  isReversing?: boolean;
  onNew?: () => void;
  onSave?: () => void;
  onPost?: () => void;
  onReverse?: () => void;
  onDelete?: () => void;
  onPrint?: () => void;
  onNavigateFirst?: () => void;
  onNavigatePrev?: () => void;
  onNavigateNext?: () => void;
  onNavigateLast?: () => void;
  extraActions?: React.ReactNode;
}

export function PetraDocumentToolbar({
  status = "draft",
  canSave = true,
  canPost = false,
  canReverse = false,
  canDelete = false,
  canPrint = true,
  isSaving = false,
  isPosting = false,
  isReversing = false,
  onNew,
  onSave,
  onPost,
  onReverse,
  onDelete,
  onPrint,
  onNavigateFirst,
  onNavigatePrev,
  onNavigateNext,
  onNavigateLast,
  extraActions,
}: PetraDocumentToolbarProps) {
  const getStatusBadge = () => {
    switch (status) {
      case "posted":
        return <span style={{ padding: "2px 8px", borderRadius: 4, background: "rgba(30, 130, 76, 0.15)", color: "var(--success)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>مُرَحَّل</span>;
      case "reversed":
        return <span style={{ padding: "2px 8px", borderRadius: 4, background: "rgba(192, 57, 43, 0.15)", color: "var(--danger)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>معكوس</span>;
      case "pending":
        return <span style={{ padding: "2px 8px", borderRadius: 4, background: "rgba(217, 130, 43, 0.15)", color: "var(--warning)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>بانتظار الموافقة</span>;
      default:
        return <span style={{ padding: "2px 8px", borderRadius: 4, background: "var(--bg-sunken)", color: "var(--text-muted)", fontWeight: 600, fontSize: "var(--fs-caption)" }}>مسودة</span>;
    }
  };

  return (
    <div
      role="toolbar"
      aria-label="شريط أوامر المستند"
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "var(--space-2) var(--space-4)",
        background: "var(--bg-sunken)",
        borderBottom: "1px solid var(--border)",
        gap: "var(--space-2)",
        flexWrap: "wrap",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
        {onNew ? (
          <PetraButton variant="secondary" size="sm" onClick={onNew} shortcut="Ctrl+N" title="مستند جديد">
            جديد
          </PetraButton>
        ) : null}

        {onSave ? (
          <PetraButton
            variant="primary"
            size="sm"
            onClick={onSave}
            disabled={!canSave || status === "posted" || status === "reversed"}
            loading={isSaving}
            shortcut="Ctrl+S"
            title="حفظ المستند"
          >
            حفظ
          </PetraButton>
        ) : null}

        {onPost ? (
          <PetraButton
            variant="secondary"
            size="sm"
            onClick={onPost}
            disabled={!canPost || status === "posted" || status === "reversed"}
            loading={isPosting}
            shortcut="F9"
            title="ترحيل القيد / المستند"
          >
            ترحيل
          </PetraButton>
        ) : null}

        {onReverse ? (
          <PetraButton
            variant="danger"
            size="sm"
            onClick={onReverse}
            disabled={!canReverse || status !== "posted"}
            loading={isReversing}
            title="عكس المستند بحركة معاكسة"
          >
            عكس
          </PetraButton>
        ) : null}

        {onDelete ? (
          <PetraButton
            variant="ghost"
            size="sm"
            onClick={onDelete}
            disabled={!canDelete || status === "posted" || status === "reversed"}
            title="حذف المسودة"
            style={{ color: "var(--danger)" }}
          >
            حذف
          </PetraButton>
        ) : null}

        {onPrint ? (
          <PetraButton variant="secondary" size="sm" onClick={onPrint} disabled={!canPrint} shortcut="Ctrl+P" title="طباعة المستند">
            طباعة
          </PetraButton>
        ) : null}

        {extraActions}
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
        {getStatusBadge()}

        {onNavigateFirst || onNavigatePrev || onNavigateNext || onNavigateLast ? (
          <div style={{ display: "inline-flex", borderRadius: "var(--radius)", overflow: "hidden", border: "1px solid var(--border)" }}>
            <PetraButton variant="ghost" size="sm" onClick={onNavigateFirst} title="الأول" style={{ borderRadius: 0, padding: "0 6px" }}>⏮</PetraButton>
            <PetraButton variant="ghost" size="sm" onClick={onNavigatePrev} title="السابق" style={{ borderRadius: 0, padding: "0 6px" }}>◀</PetraButton>
            <PetraButton variant="ghost" size="sm" onClick={onNavigateNext} title="التالي" style={{ borderRadius: 0, padding: "0 6px" }}>▶</PetraButton>
            <PetraButton variant="ghost" size="sm" onClick={onNavigateLast} title="الأخير" style={{ borderRadius: 0, padding: "0 6px" }}>⏭</PetraButton>
          </div>
        ) : null}
      </div>
    </div>
  );
}
