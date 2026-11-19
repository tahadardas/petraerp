import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface BranchesScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

interface BranchCard {
  id: string;
  companyId: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
  isActive: boolean;
  parentBranchId?: string | null;
  numberPrefix?: string | null;
  openedOn?: string | null;
  address?: string | null;
  phone?: string | null;
  email?: string | null;
  notes?: string | null;
  defaultWarehouseId?: string | null;
  defaultCashAccountId?: string | null;
  defaultBankAccountId?: string | null;
  defaultCostCenterId?: string | null;
  version: string;
}

interface BranchTreeNode {
  branch: BranchCard;
  depth: number;
  children: BranchTreeNode[];
}

export default function BranchesScreen({ params: _params }: BranchesScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [roots, setRoots] = useState<BranchTreeNode[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  const [selectedBranch, setSelectedBranch] = useState<BranchCard | null>(null);

  // Create/Edit Dialog
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [parentForNew, setParentForNew] = useState<BranchCard | null>(null);
  const [code, setCode] = useState("");
  const [nameArabic, setNameArabic] = useState("");
  const [nameEnglish, setNameEnglish] = useState("");
  const [numberPrefix, setNumberPrefix] = useState("");
  const [address, setAddress] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [notes, setNotes] = useState("");
  const [saving, setSaving] = useState(false);

  const loadTree = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/branch-admin/tree", {
        params: {
          path: { companyId, branchId },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل تحميل شجرة الفروع" });
      } else if (res.data) {
        const fetchedRoots = (res.data.roots ?? []) as unknown as BranchTreeNode[];
        setRoots(fetchedRoots);
        if (!selectedBranch && fetchedRoots.length > 0 && fetchedRoots[0]) {
          setSelectedBranch(fetchedRoots[0].branch);
        }
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadTree();
  }, [companyId, branchId]);

  const handleOpenCreate = (parent?: BranchCard | null) => {
    setParentForNew(parent ?? null);
    setCode("");
    setNameArabic("");
    setNameEnglish("");
    setNumberPrefix("");
    setAddress("");
    setPhone("");
    setEmail("");
    setNotes("");
    setCreateDialogOpen(true);
  };

  const handleSaveBranch = async () => {
    if (!companyId || !branchId || !code.trim() || !nameArabic.trim()) return;

    setSaving(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/branch-admin", {
        params: {
          path: { companyId, branchId },
        },
        body: {
          code: code.trim(),
          nameArabic: nameArabic.trim(),
          ...(nameEnglish.trim() ? { nameEnglish: nameEnglish.trim() } : {}),
          ...(parentForNew ? { parentBranchId: parentForNew.id } : {}),
          ...(numberPrefix.trim() ? { numberPrefix: numberPrefix.trim() } : {}),
          ...(address.trim() ? { address: address.trim() } : {}),
          ...(phone.trim() ? { phone: phone.trim() } : {}),
          ...(email.trim() ? { email: email.trim() } : {}),
          ...(notes.trim() ? { notes: notes.trim() } : {}),
        },
      });

      if (res.error) {
        toast.error("فشل حفظ بيانات الفرع الجديد");
      } else {
        toast.success(`تم إنشاء الفرع "${nameArabic}" بنجاح`);
        setCreateDialogOpen(false);
        await loadTree();
      }
    } catch {
      toast.error("حدث خطأ في الاتصال أثناء حفظ الفرع");
    } finally {
      setSaving(false);
    }
  };

  const handleToggleActive = async (branch: BranchCard) => {
    if (!companyId || !branchId) return;

    try {
      const res = await api.PUT("/api/v1/companies/{companyId}/branches/{branchId}/branch-admin/{target}/active", {
        params: {
          path: { companyId, branchId, target: branch.id },
        },
        body: {
          isActive: !branch.isActive,
          version: branch.version,
          reason: "تعديل حالة تفعيل الفرع",
        },
      });

      if (res.error) {
        toast.error("فشل تغيير حالة الفرع");
      } else {
        toast.success(`تم ${!branch.isActive ? "تفعيل" : "تعطيل"} الفرع بنجاح`);
        setSelectedBranch({ ...branch, isActive: !branch.isActive });
        await loadTree();
      }
    } catch {
      toast.error("خطأ أثناء تحديث حالة التفعيل");
    }
  };

  const renderTreeNode = (node: BranchTreeNode) => {
    const isSelected = selectedBranch?.id === node.branch.id;
    return (
      <div key={node.branch.id} style={{ display: "flex", flexDirection: "column" }}>
        <div
          onClick={() => setSelectedBranch(node.branch)}
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "var(--space-2) var(--space-3)",
            paddingRight: `calc(var(--space-3) + ${node.depth * 20}px)`,
            cursor: "pointer",
            background: isSelected ? "var(--bg-sunken)" : "transparent",
            borderRight: isSelected ? "3px solid var(--primary)" : "3px solid transparent",
            borderBottom: "1px solid var(--border-light)",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
            <span style={{ fontFamily: "monospace", fontWeight: 700, color: "var(--primary)" }}>
              {node.branch.code}
            </span>
            <span style={{ fontWeight: 600 }}>{node.branch.nameArabic}</span>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
            <span
              style={{
                padding: "2px 8px",
                borderRadius: "var(--radius)",
                fontSize: "var(--fs-caption)",
                background: node.branch.isActive ? "var(--color-success-bg, #e6f4ea)" : "var(--color-danger-bg, #fce8e6)",
                color: node.branch.isActive ? "var(--color-success)" : "var(--color-danger)",
              }}
            >
              {node.branch.isActive ? "نشط" : "معطل"}
            </span>
          </div>
        </div>

        {node.children && node.children.length > 0 && (
          <div>{node.children.map(renderTreeNode)}</div>
        )}
      </div>
    );
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Create Dialog */}
      <PetraDialog
        open={createDialogOpen}
        onClose={() => setCreateDialogOpen(false)}
        title={parentForNew ? `إضافة فرع تابع لـ (${parentForNew.nameArabic})` : "إضافة فرع رئيسي جديد"}
        width={520}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setCreateDialogOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton
              variant="primary"
              disabled={!code.trim() || !nameArabic.trim() || saving}
              onClick={handleSaveBranch}
            >
              {saving ? "جاري الحفظ..." : "حفظ الفرع"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="رمز الفرع (Code)" required>
              <input
                type="text"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                placeholder="مثال: BR-01"
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>

            <PetraField label="بادئة الترقيم (Prefix)">
              <input
                type="text"
                value={numberPrefix}
                onChange={(e) => setNumberPrefix(e.target.value)}
                placeholder="مثال: AMM-"
                maxLength={8}
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="الاسم العربي" required>
              <input
                type="text"
                value={nameArabic}
                onChange={(e) => setNameArabic(e.target.value)}
                placeholder="اسم الفرع بالعربية"
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>

            <PetraField label="الاسم الإنجليزي">
              <input
                type="text"
                value={nameEnglish}
                onChange={(e) => setNameEnglish(e.target.value)}
                placeholder="Branch English Name"
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="رقم الهاتف">
              <input
                type="text"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="+962 6 ..."
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>

            <PetraField label="البريد الإلكتروني">
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="branch@company.com"
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              />
            </PetraField>
          </div>

          <PetraField label="العنوان الجغرافي">
            <input
              type="text"
              value={address}
              onChange={(e) => setAddress(e.target.value)}
              placeholder="المدينة، الشارع، المبنى"
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            />
          </PetraField>

          <PetraField label="ملاحظات">
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              style={{
                padding: "var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
                resize: "vertical",
              }}
            />
          </PetraField>
        </div>
      </PetraDialog>

      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "var(--space-2) var(--space-3)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            إدارة وهيكلية الفروع (Branches Management)
          </h2>
          <span style={{ fontSize: "var(--fs-caption)", color: "var(--text-muted)" }}>
            (عزل الشركات ومجالات الصلاحيات)
          </span>
        </div>

        <div style={{ display: "flex", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => handleOpenCreate(null)}>
            إضافة فرع رئيسي
          </PetraButton>
          <PetraButton
            variant="secondary"
            size="sm"
            disabled={!selectedBranch}
            onClick={() => handleOpenCreate(selectedBranch)}
          >
            إضافة فرع تابع
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadTree} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Two-Pane Body */}
      <div style={{ flex: 1, display: "grid", gridTemplateColumns: "360px 1fr", overflow: "hidden" }}>
        {/* Left Tree Pane */}
        <div
          style={{
            background: "var(--bg-surface)",
            borderLeft: "1px solid var(--border)",
            display: "flex",
            flexDirection: "column",
            overflow: "auto",
          }}
        >
          <div
            style={{
              padding: "var(--space-2) var(--space-3)",
              background: "var(--bg-sunken)",
              borderBottom: "1px solid var(--border)",
              fontWeight: 700,
              fontSize: "var(--fs-caption)",
              color: "var(--text-muted)",
            }}
          >
            الهيكل الهرمي للفروع
          </div>

          <div style={{ flex: 1, overflow: "auto" }}>
            {loading ? (
              <PetraLoadingSkeleton rows={8} />
            ) : error ? (
              <PetraErrorDetails status={error.status} message={error.message} onRetry={loadTree} />
            ) : roots.length === 0 ? (
              <PetraEmptyState title="لا توجد فروع مسجلة" description="قم بإنشاء الفرع الرئيسي الأول للشركة." />
            ) : (
              roots.map(renderTreeNode)
            )}
          </div>
        </div>

        {/* Right Details Pane */}
        <div style={{ overflow: "auto", padding: "var(--space-4)" }}>
          {selectedBranch ? (
            <div
              style={{
                background: "var(--bg-surface)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                padding: "var(--space-4)",
                maxWidth: "700px",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  marginBottom: "var(--space-4)",
                  borderBottom: "1px solid var(--border-light)",
                  paddingBottom: "var(--space-3)",
                }}
              >
                <div>
                  <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
                    <h3 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
                      {selectedBranch.nameArabic}
                    </h3>
                    <span style={{ fontFamily: "monospace", color: "var(--primary)", fontWeight: 700 }}>
                      ({selectedBranch.code})
                    </span>
                  </div>
                  {selectedBranch.nameEnglish && (
                    <div style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", marginTop: "2px" }}>
                      {selectedBranch.nameEnglish}
                    </div>
                  )}
                </div>

                <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
                  <span
                    style={{
                      padding: "2px 8px",
                      borderRadius: "var(--radius)",
                      fontSize: "var(--fs-caption)",
                      background: selectedBranch.isActive ? "var(--color-success-bg, #e6f4ea)" : "var(--color-danger-bg, #fce8e6)",
                      color: selectedBranch.isActive ? "var(--color-success)" : "var(--color-danger)",
                    }}
                  >
                    {selectedBranch.isActive ? "نشط" : "معطل"}
                  </span>
                  <PetraButton
                    variant={selectedBranch.isActive ? "danger" : "secondary"}
                    size="sm"
                    onClick={() => handleToggleActive(selectedBranch)}
                  >
                    {selectedBranch.isActive ? "تعطيل الفرع" : "تفعيل الفرع"}
                  </PetraButton>
                </div>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                <div>
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", display: "block" }}>
                    بادئة الترقيم التلقائي
                  </span>
                  <strong style={{ fontFamily: "monospace" }}>{selectedBranch.numberPrefix ?? "—"}</strong>
                </div>

                <div>
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", display: "block" }}>
                    تاريخ الافتتاح
                  </span>
                  <span>{selectedBranch.openedOn ?? "—"}</span>
                </div>

                <div>
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", display: "block" }}>
                    الهاتف
                  </span>
                  <span>{selectedBranch.phone ?? "—"}</span>
                </div>

                <div>
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", display: "block" }}>
                    البريد الإلكتروني
                  </span>
                  <span>{selectedBranch.email ?? "—"}</span>
                </div>

                <div style={{ gridColumn: "span 2" }}>
                  <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", display: "block" }}>
                    العنوان
                  </span>
                  <span>{selectedBranch.address ?? "—"}</span>
                </div>

                {selectedBranch.notes && (
                  <div style={{ gridColumn: "span 2" }}>
                    <span style={{ color: "var(--text-muted)", fontSize: "var(--fs-caption)", display: "block" }}>
                      ملاحظات
                    </span>
                    <p style={{ margin: "4px 0 0 0", background: "var(--bg-sunken)", padding: "var(--space-2)", borderRadius: "var(--radius)" }}>
                      {selectedBranch.notes}
                    </p>
                  </div>
                )}
              </div>
            </div>
          ) : (
            <PetraEmptyState title="حدد فرعاً من الشجرة" description="اختر فرعاً لعرض تفاصيله أو إضافة فرع تابع له." />
          )}
        </div>
      </div>
    </div>
  );
}
