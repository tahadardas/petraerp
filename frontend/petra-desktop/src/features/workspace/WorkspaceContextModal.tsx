import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraDialog, PetraButton, PetraField, PetraLoadingSkeleton } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface WorkspaceContextModalProps {
  open: boolean;
  onClose: () => void;
}

interface WorkspaceItem {
  workspaceId: string;
  companyId: string;
  companyCode: string;
  companyNameArabic: string;
  yearNumber: number;
  fiscalYearCode: string;
  baseCurrencyCode: string;
}

interface BranchItem {
  id: string;
  companyId: string;
  code: string;
  nameArabic: string;
}

export function WorkspaceContextModal({ open, onClose }: WorkspaceContextModalProps) {
  const api = useApi();
  const session = useSession();
  const [loading, setLoading] = useState(false);
  const [workspaces, setWorkspaces] = useState<WorkspaceItem[]>([]);
  const [branches, setBranches] = useState<BranchItem[]>([]);
  const [selectedWsId, setSelectedWsId] = useState<string>(session.workspaceId ?? "");
  const [selectedBranchId, setSelectedBranchId] = useState<string>(session.branchId ?? "");

  useEffect(() => {
    if (!open) return;

    let active = true;
    setLoading(true);

    api.GET("/api/v1/me/workspaces").then((res) => {
      if (!active) return;
      if (res.data?.items) {
        setWorkspaces(res.data.items);
        const current = res.data.items.find((w) => w.workspaceId === session.workspaceId) ?? res.data.items[0];
        if (current) {
          setSelectedWsId(current.workspaceId);
          loadBranches(current.companyId);
        }
      }
      setLoading(false);
    }).catch(() => {
      if (active) setLoading(false);
    });

    return () => {
      active = false;
    };
  }, [open, session.workspaceId]);

  const loadBranches = async (companyId: string) => {
    try {
      const res = await api.GET("/api/v1/me/companies/{companyId}/branches", {
        params: { path: { companyId } },
      });
      if (res.data?.items) {
        setBranches(res.data.items);
        const first = res.data.items[0];
        if (first) {
          setSelectedBranchId(first.id);
        }
      }
    } catch {
      // Ignored
    }
  };

  const handleWorkspaceChange = (wsId: string) => {
    setSelectedWsId(wsId);
    const ws = workspaces.find((w) => w.workspaceId === wsId);
    if (ws) {
      loadBranches(ws.companyId);
    }
  };

  const handleApply = () => {
    const ws = workspaces.find((w) => w.workspaceId === selectedWsId);
    const br = branches.find((b) => b.id === selectedBranchId) ?? branches[0];

    if (!ws) {
      onClose();
      return;
    }

    session.selectWorkspace(ws.workspaceId);
    session.setContext({
      companyId: ws.companyId,
      companyName: ws.companyNameArabic,
      fiscalYear: ws.yearNumber.toString(),
      branchId: br?.id ?? null,
      branchName: br?.nameArabic ?? null,
    });

    toast.success("تم تبديل مساحة العمل", `${ws.companyNameArabic} — سنة ${ws.yearNumber}`);
    onClose();
  };

  return (
    <PetraDialog
      open={open}
      onClose={onClose}
      title="اختيار سياق العمل (الشركة، السنة، الفرع)"
      width={480}
      actions={
        <>
          <PetraButton variant="secondary" onClick={onClose}>
            إلغاء
          </PetraButton>
          <PetraButton variant="primary" onClick={handleApply} disabled={loading || !selectedWsId}>
            تطبيق التبديل
          </PetraButton>
        </>
      }
    >
      {loading ? (
        <PetraLoadingSkeleton rows={3} />
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="الشركة والسنة المالية" required>
            <select
              value={selectedWsId}
              onChange={(e) => handleWorkspaceChange(e.target.value)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                fontFamily: "inherit",
              }}
            >
              {workspaces.map((w) => (
                <option key={w.workspaceId} value={w.workspaceId}>
                  {w.companyNameArabic} ({w.companyCode}) — سنة {w.yearNumber} [{w.baseCurrencyCode}]
                </option>
              ))}
            </select>
          </PetraField>

          <PetraField label="فرع الشركة" required>
            <select
              value={selectedBranchId}
              onChange={(e) => setSelectedBranchId(e.target.value)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                fontFamily: "inherit",
              }}
            >
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.nameArabic} ({b.code})
                </option>
              ))}
            </select>
          </PetraField>

          <div
            style={{
              padding: "var(--space-2)",
              background: "var(--bg-sunken)",
              borderRadius: "var(--radius)",
              fontSize: "var(--fs-caption)",
              color: "var(--text-muted)",
            }}
          >
            💡 تنبيه: تبديل مساحة العمل يعيد ضبط كاش الاستعلامات تلقائيًا لمنع تسرب بيانات شركة إلى أخرى.
          </div>
        </div>
      )}
    </PetraDialog>
  );
}
