import { useState, useEffect } from "react";
import { useApi } from "@/services/api/ApiContext";
import { useSession } from "@/app/permissions";
import { PetraButton, PetraLoadingSkeleton, PetraEmptyState, PetraErrorDetails, PetraDialog, PetraField } from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

export interface CommercialPapersScreenProps {
  params?: Readonly<Record<string, string>> | undefined;
}

type PaperKind = "ChequeReceived" | "ChequeIssued" | "PromissoryNote";
type PaperStatus = "Received" | "UnderCollection" | "Cleared" | "Bounced" | "Cancelled" | "Endorsed";

interface CommercialPaperDto {
  id: string;
  paperKind: PaperKind;
  paperKindName: string;
  paperNumber: string;
  bankName: string;
  branchName?: string | null;
  drawerName: string;
  payeeName?: string | null;
  issueDate: string;
  dueDate: string;
  amount: string;
  currencyCode: string;
  status: PaperStatus;
  statusName: string;
  partyName?: string | null;
  notes?: string | null;
}

export default function CommercialPapersScreen({ params: _params }: CommercialPapersScreenProps) {
  const api = useApi();
  const { companyId, branchId } = useSession((s) => ({ companyId: s.companyId, branchId: s.branchId }));

  const [kindFilter, setKindFilter] = useState<string>("");
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [searchQuery, setSearchQuery] = useState("");

  const [items, setItems] = useState<CommercialPaperDto[]>([]);
  const [totalAmount, setTotalAmount] = useState<string>("0.00");
  const [totalCount, setTotalCount] = useState<number>(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<{ status?: number; message?: string } | null>(null);

  // Register Dialog
  const [registerOpen, setRegisterOpen] = useState(false);
  const [regKind, setRegKind] = useState<PaperKind>("ChequeReceived");
  const [regNumber, setRegNumber] = useState("");
  const [regBank, setRegBank] = useState("");
  const [regDrawer, setRegDrawer] = useState("");
  const [regPayee, setRegPayee] = useState("");
  const [regIssueDate, setRegIssueDate] = useState(() => new Date().toISOString().substring(0, 10));
  const [regDueDate, setRegDueDate] = useState(() => new Date().toISOString().substring(0, 10));
  const [regAmount, setRegAmount] = useState("");
  const [regCurrency, setRegCurrency] = useState("JOD");
  const [regNotes, setRegNotes] = useState("");
  const [registering, setRegistering] = useState(false);

  // Transition Dialog
  const [transitionPaper, setTransitionPaper] = useState<CommercialPaperDto | null>(null);
  const [targetStatus, setTargetStatus] = useState<PaperStatus>("Cleared");
  const [transitionReason, setTransitionReason] = useState("");
  const [transitionDate, setTransitionDate] = useState(() => new Date().toISOString().substring(0, 10));
  const [transitioning, setTransitioning] = useState(false);

  const loadPapers = async () => {
    if (!companyId || !branchId) return;

    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/companies/{companyId}/branches/{branchId}/treasury/commercial-papers", {
        params: {
          path: { companyId, branchId },
          query: {
            ...(kindFilter ? { kind: kindFilter as PaperKind } : {}),
            ...(statusFilter ? { status: statusFilter as PaperStatus } : {}),
            ...(searchQuery.trim() ? { query: searchQuery.trim() } : {}),
          },
        },
      });

      if (res.error) {
        setError({ status: res.response.status, message: "فشل استرجاع الأوراق التجارية والشيكات" });
      } else if (res.data) {
        setItems(res.data.items ?? []);
        setTotalAmount(res.data.totalAmount ?? "0.00");
        setTotalCount(res.data.totalCount ?? 0);
      }
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : "خطأ غير متوقع" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadPapers();
  }, [companyId, branchId, kindFilter, statusFilter]);

  const copyToExcel = () => {
    if (items.length === 0) return;
    const header = "رقم الورقة\tالنوع\tالبنك\tالساحب\tالمستفيد\tتاريخ الإصدار\tتاريخ الاستحقاق\tالمبلغ\tالعملة\tالحالة";
    const rows = items.map((i) =>
      `${i.paperNumber}\t${i.paperKindName}\t${i.bankName}\t${i.drawerName}\t${i.payeeName ?? ""}\t${i.issueDate}\t${i.dueDate}\t${i.amount}\t${i.currencyCode}\t${i.statusName}`
    );
    navigator.clipboard?.writeText([header, ...rows].join("\r\n")).then(() => {
      toast.success("تم نسخ قائمة الأوراق التجارية والشيكات إلى الحافظة");
    });
  };

  const handleRegister = async () => {
    if (!companyId || !branchId || !regNumber.trim() || !regBank.trim() || !regDrawer.trim() || !regAmount.trim()) {
      toast.error("يرجى ملء جميع الحقول الإلزامية");
      return;
    }

    setRegistering(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/commercial-papers", {
        params: {
          path: { companyId, branchId },
        },
        body: {
          paperKind: regKind,
          paperNumber: regNumber.trim(),
          bankName: regBank.trim(),
          drawerName: regDrawer.trim(),
          ...(regPayee.trim() ? { payeeName: regPayee.trim() } : {}),
          issueDate: regIssueDate,
          dueDate: regDueDate,
          amount: regAmount.trim(),
          currencyCode: regCurrency.toUpperCase(),
          ...(regNotes.trim() ? { notes: regNotes.trim() } : {}),
        },
      });

      if (res.error) {
        toast.error("فشل تسجيل الورقة التجارية");
      } else {
        toast.success("تم تسجيل الورقة التجارية / الشيك بنجاح");
        setRegisterOpen(false);
        setRegNumber("");
        setRegBank("");
        setRegDrawer("");
        setRegAmount("");
        loadPapers();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setRegistering(false);
    }
  };

  const handleTransition = async () => {
    if (!companyId || !branchId || !transitionPaper) return;

    setTransitioning(true);
    try {
      const res = await api.POST("/api/v1/companies/{companyId}/branches/{branchId}/treasury/commercial-papers/{paperId}/status", {
        params: {
          path: { companyId, branchId, paperId: transitionPaper.id },
        },
        body: {
          newStatus: targetStatus,
          transitionDate: transitionDate,
          ...(transitionReason.trim() ? { reason: transitionReason.trim() } : {}),
        },
      });

      if (res.error) {
        toast.error("فشل تغيير حالة الورقة التجارية");
      } else {
        toast.success(`تم تحديث حالة الشيك إلى ${targetStatus}`);
        setTransitionPaper(null);
        setTransitionReason("");
        loadPapers();
      }
    } catch {
      toast.error("خطأ أثناء الاتصال بالخادم");
    } finally {
      setTransitioning(false);
    }
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%", background: "var(--bg-app)" }}>
      {/* Register Dialog */}
      <PetraDialog
        open={registerOpen}
        onClose={() => setRegisterOpen(false)}
        title="تسجيل ورقة تجارية / شيك جديد"
        width={500}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setRegisterOpen(false)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" disabled={registering} onClick={handleRegister}>
              {registering ? "جاري التسجيل..." : "تسجيل الشيك"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="نوع الورقة" required>
              <select
                value={regKind}
                onChange={(e) => setRegKind(e.target.value as PaperKind)}
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                }}
              >
                <option value="ChequeReceived">شيك مقبوض (وارد)</option>
                <option value="ChequeIssued">شيك مدفوع (صادر)</option>
                <option value="PromissoryNote">كمبيالة (سند لأمر)</option>
              </select>
            </PetraField>

            <PetraField label="رقم الورقة / الشيك" required>
              <input
                type="text"
                value={regNumber}
                onChange={(e) => setRegNumber(e.target.value)}
                placeholder="مثال: 004812"
                style={{
                  height: "var(--control-height)",
                  padding: "0 var(--space-2)",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--bg-input)",
                  color: "var(--text)",
                  fontFamily: "monospace",
                }}
              />
            </PetraField>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="اسم البنك المسحوب عليه" required>
              <input
                type="text"
                value={regBank}
                onChange={(e) => setRegBank(e.target.value)}
                placeholder="مثال: البنك العربي"
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

            <PetraField label="اسم الساحب" required>
              <input
                type="text"
                value={regDrawer}
                onChange={(e) => setRegDrawer(e.target.value)}
                placeholder="اسم محرر الشيك"
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
            <PetraField label="المستفيد">
              <input
                type="text"
                value={regPayee}
                onChange={(e) => setRegPayee(e.target.value)}
                placeholder="اسم المستفيد لأمره"
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

            <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: "var(--space-1)" }}>
              <PetraField label="المبلغ" required>
                <input
                  type="text"
                  value={regAmount}
                  onChange={(e) => setRegAmount(e.target.value)}
                  placeholder="0.00"
                  style={{
                    height: "var(--control-height)",
                    padding: "0 var(--space-2)",
                    borderRadius: "var(--radius)",
                    border: "1px solid var(--border)",
                    background: "var(--bg-input)",
                    color: "var(--text)",
                    fontFamily: "monospace",
                  }}
                />
              </PetraField>

              <PetraField label="العملة">
                <input
                  type="text"
                  value={regCurrency}
                  onChange={(e) => setRegCurrency(e.target.value)}
                  maxLength={3}
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
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-2)" }}>
            <PetraField label="تاريخ التحرير" required>
              <input
                type="date"
                value={regIssueDate}
                onChange={(e) => setRegIssueDate(e.target.value)}
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

            <PetraField label="تاريخ الاستحقاق" required>
              <input
                type="date"
                value={regDueDate}
                onChange={(e) => setRegDueDate(e.target.value)}
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

          <PetraField label="ملاحظات">
            <textarea
              value={regNotes}
              onChange={(e) => setRegNotes(e.target.value)}
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

      {/* Transition Dialog */}
      <PetraDialog
        open={!!transitionPaper}
        onClose={() => setTransitionPaper(null)}
        title={`تغيير حالة الشيك #${transitionPaper?.paperNumber}`}
        width={450}
        actions={
          <>
            <PetraButton variant="secondary" onClick={() => setTransitionPaper(null)}>
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" disabled={transitioning} onClick={handleTransition}>
              {transitioning ? "جاري التحديث..." : "تأكيد تغيير الحالة"}
            </PetraButton>
          </>
        }
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
          <PetraField label="الحالة الجديدة" required>
            <select
              value={targetStatus}
              onChange={(e) => setTargetStatus(e.target.value as PaperStatus)}
              style={{
                height: "var(--control-height)",
                padding: "0 var(--space-2)",
                borderRadius: "var(--radius)",
                border: "1px solid var(--border)",
                background: "var(--bg-input)",
                color: "var(--text)",
              }}
            >
              <option value="UnderCollection">برسم التحصيل (Under Collection)</option>
              <option value="Cleared">محصل ومودع في الحساب (Cleared)</option>
              <option value="Bounced">مرتجع (Bounced)</option>
              <option value="Cancelled">ملغى (Cancelled)</option>
              <option value="Endorsed">مجير إلى طرف آخر (Endorsed)</option>
            </select>
          </PetraField>

          <PetraField label="تاريخ الحركة" required>
            <input
              type="date"
              value={transitionDate}
              onChange={(e) => setTransitionDate(e.target.value)}
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

          <PetraField label="السبب أو البيان">
            <textarea
              value={transitionReason}
              onChange={(e) => setTransitionReason(e.target.value)}
              rows={2}
              placeholder="بيان حركة الشيك أو سبب الإرجاع"
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
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-3)" }}>
          <h2 style={{ margin: 0, fontSize: "var(--fs-title)", fontWeight: 700 }}>
            حافظة الشيكات والأوراق التجارية (Commercial Papers & Cheques)
          </h2>
          <div style={{ display: "flex", gap: "var(--space-2)", fontSize: "var(--fs-caption)" }}>
            <span style={{ background: "var(--bg-sunken)", padding: "2px 8px", borderRadius: "var(--radius)" }}>
              العدد: <strong style={{ fontFamily: "monospace" }}>{totalCount}</strong>
            </span>
            <span style={{ background: "var(--bg-sunken)", padding: "2px 8px", borderRadius: "var(--radius)" }}>
              الإجمالي: <strong style={{ fontFamily: "monospace", color: "var(--primary)" }}>{totalAmount}</strong>
            </span>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-2)" }}>
          <PetraButton variant="secondary" size="sm" onClick={() => setRegisterOpen(true)}>
            تسجيل شيك / ورقة
          </PetraButton>
          <PetraButton variant="secondary" size="sm" onClick={copyToExcel} disabled={items.length === 0}>
            نسخ إلى Excel
          </PetraButton>
          <PetraButton variant="primary" size="sm" onClick={loadPapers} disabled={loading}>
            {loading ? "جاري التحميل..." : "تحديث"}
          </PetraButton>
        </div>
      </div>

      {/* Filters Bar */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: "var(--space-3)",
          padding: "var(--space-2) var(--space-3)",
          background: "var(--bg-surface)",
          borderBottom: "1px solid var(--border)",
          fontSize: "var(--fs-caption)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-1)" }}>
          <span>النوع:</span>
          <select
            value={kindFilter}
            onChange={(e) => setKindFilter(e.target.value)}
            style={{
              height: "28px",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          >
            <option value="">جميع الأنواع</option>
            <option value="ChequeReceived">شيكات مقبوضة (واردة)</option>
            <option value="ChequeIssued">شيكات مدفوعة (صادرة)</option>
            <option value="PromissoryNote">كمبيالات</option>
          </select>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "var(--space-1)" }}>
          <span>الحالة:</span>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            style={{
              height: "28px",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          >
            <option value="">جميع الحالات</option>
            <option value="Received">مستلمة (في الصندوق)</option>
            <option value="UnderCollection">برسم التحصيل</option>
            <option value="Cleared">محصلة</option>
            <option value="Bounced">مرتجعة</option>
            <option value="Cancelled">ملغاة</option>
            <option value="Endorsed">مجيرة</option>
          </select>
        </div>

        <div style={{ flex: 1 }}>
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") loadPapers();
            }}
            placeholder="بحث برقم الشيك، اسم البنك، أو الساحب... (Enter للبحث)"
            style={{
              width: "100%",
              height: "28px",
              padding: "0 var(--space-2)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--bg-input)",
              color: "var(--text)",
            }}
          />
        </div>
      </div>

      {/* Main Table */}
      <div style={{ flex: 1, overflow: "auto", padding: "var(--space-3)" }}>
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails status={error.status} message={error.message} onRetry={loadPapers} />
        ) : items.length === 0 ? (
          <PetraEmptyState title="لا توجد أوراق تجارية مسجلة" description="قم بتسجيل الشيكات أو تعديل خيارات الفلترة." />
        ) : (
          <div
            style={{
              background: "var(--bg-surface)",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              overflow: "hidden",
            }}
          >
            <table
              style={{
                width: "100%",
                borderCollapse: "collapse",
                textAlign: "right",
                fontSize: "var(--fs-body)",
              }}
            >
              <thead>
                <tr
                  style={{
                    background: "var(--bg-sunken)",
                    borderBottom: "2px solid var(--border)",
                    color: "var(--text-muted)",
                    fontSize: "var(--fs-caption)",
                  }}
                >
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>رقم الشيك</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>النوع</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>البنك</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>الساحب</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>المستفيد</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)" }}>الاستحقاق</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left" }}>المبلغ</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>الحالة</th>
                  <th style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>تغيير الحالة</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item, idx) => (
                  <tr
                    key={item.id}
                    style={{
                      borderBottom: "1px solid var(--border-light)",
                      background: idx % 2 === 1 ? "var(--bg-sunken)" : "transparent",
                    }}
                  >
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace", fontWeight: 700 }}>
                      {item.paperNumber}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)", fontSize: "var(--fs-caption)" }}>
                      {item.paperKindName}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)" }}>{item.bankName}</td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontWeight: 600 }}>{item.drawerName}</td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", color: "var(--text-muted)" }}>{item.payeeName ?? "—"}</td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", fontFamily: "monospace" }}>{item.dueDate}</td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "left", fontFamily: "monospace", fontWeight: 700, color: "var(--primary)" }}>
                      {item.amount} {item.currencyCode}
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
                      <span
                        style={{
                          padding: "2px 8px",
                          borderRadius: "var(--radius)",
                          fontSize: "var(--fs-caption)",
                          background: item.status === "Cleared" ? "var(--color-success-bg, #e6f4ea)" : item.status === "Bounced" || item.status === "Cancelled" ? "var(--color-danger-bg, #fce8e6)" : "var(--color-warning-bg, #fef7e0)",
                          color: item.status === "Cleared" ? "var(--color-success)" : item.status === "Bounced" || item.status === "Cancelled" ? "var(--color-danger)" : "var(--color-warning)",
                        }}
                      >
                        {item.statusName}
                      </span>
                    </td>
                    <td style={{ padding: "var(--space-2) var(--space-3)", textAlign: "center" }}>
                      {item.status !== "Cleared" && item.status !== "Cancelled" && (
                        <PetraButton
                          variant="secondary"
                          size="sm"
                          onClick={() => {
                            setTransitionPaper(item);
                            setTargetStatus(item.status === "Received" ? "UnderCollection" : "Cleared");
                          }}
                        >
                          تغيير الحالة
                        </PetraButton>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
