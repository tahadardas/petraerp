import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import {
  PetraButton,
  PetraField,
  PetraDialog,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";
import { toast } from "@/components/ui/toastStore";

interface RoleItem {
  id: string;
  code: string;
  nameArabic: string;
  nameEnglish?: string | null;
  permissionCount: number;
  userCount: number;
}

interface PermissionItem {
  code: string;
  scope: string;
}

export default function RolesScreen() {
  const api = useApi();

  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [permissions, setPermissions] = useState<PermissionItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New Role Dialog
  const [isNewDialogOpen, setIsNewDialogOpen] = useState(false);
  const [code, setCode] = useState("");
  const [nameArabic, setNameArabic] = useState("");
  const [nameEnglish, setNameEnglish] = useState("");
  const [selectedPermissions, setSelectedPermissions] = useState<Set<string>>(
    new Set()
  );
  const [creating, setCreating] = useState(false);

  const loadRolesAndPermissions = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [rolesRes, permsRes] = await Promise.all([
        api.GET("/api/v1/admin/roles"),
        api.GET("/api/v1/admin/permissions"),
      ]);

      if (rolesRes.error) {
        setError(JSON.stringify(rolesRes.error));
      } else if (rolesRes.data) {
        setRoles(rolesRes.data.items || []);
      }

      if (permsRes.data) {
        setPermissions(permsRes.data.items || []);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api]);

  useEffect(() => {
    loadRolesAndPermissions();
  }, [loadRolesAndPermissions]);

  const togglePermission = (permCode: string) => {
    setSelectedPermissions((prev) => {
      const next = new Set(prev);
      if (next.has(permCode)) {
        next.delete(permCode);
      } else {
        next.add(permCode);
      }
      return next;
    });
  };

  const handleCreateRole = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim() || !nameArabic.trim()) {
      toast.error("رمز واسم الدور مطلوبان");
      return;
    }

    setCreating(true);
    try {
      const res = await api.POST("/api/v1/admin/roles", {
        body: {
          code: code.trim(),
          nameArabic: nameArabic.trim(),
          nameEnglish: nameEnglish.trim() || null,
          permissionCodes: Array.from(selectedPermissions),
        },
      });

      if (res.error) {
        toast.error(JSON.stringify(res.error));
      } else {
        toast.success("تم إنشاء الدور بنجاح");
        setIsNewDialogOpen(false);
        setCode("");
        setNameArabic("");
        setNameEnglish("");
        setSelectedPermissions(new Set());
        loadRolesAndPermissions();
      }
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : String(err));
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header Bar */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            الأدوار والصلاحيات (ADM-02)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            العدد: {roles.length}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraButton
            variant="secondary"
            onClick={() => loadRolesAndPermissions()}
            loading={loading}
          >
            تحديث
          </PetraButton>
          <PetraButton
            variant="primary"
            onClick={() => setIsNewDialogOpen(true)}
            shortcut="F2"
          >
            + دور جديد
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={6} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadRolesAndPermissions} />
        ) : roles.length === 0 ? (
          <PetraEmptyState
            title="لا توجد أدوار معرّفة"
            description="قم بإنشاء أدوار المستخدمين وتخصيص الصلاحيات المناسبة لكل دور."
            actionLabel="+ إضافة دور"
            onAction={() => setIsNewDialogOpen(true)}
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-32">رمز الدور</th>
                  <th className="p-2 border-l border-slate-700">الاسم العربي</th>
                  <th className="p-2 border-l border-slate-700">الاسم الأجنبي</th>
                  <th className="p-2 border-l border-slate-700 w-32 text-center">عدد الصلاحيات</th>
                  <th className="p-2 w-32 text-center">عدد المستخدمين</th>
                </tr>
              </thead>
              <tbody>
                {roles.map((r, idx) => (
                  <tr
                    key={r.id}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono font-bold text-[#087E87]">
                      {r.code}
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {r.nameArabic}
                    </td>
                    <td className="p-2 text-slate-500 font-mono text-xs">
                      {r.nameEnglish || "—"}
                    </td>
                    <td className="p-2 text-center font-mono">
                      <span className="bg-slate-100 text-slate-700 px-2 py-0.5 rounded text-xs">
                        {r.permissionCount} صلاحية
                      </span>
                    </td>
                    <td className="p-2 text-center font-mono">
                      <span className="bg-blue-50 text-blue-700 px-2 py-0.5 rounded text-xs font-bold">
                        {r.userCount} مستخدم
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* New Role Dialog */}
      <PetraDialog
        open={isNewDialogOpen}
        onClose={() => setIsNewDialogOpen(false)}
        title="إنشاء دور جديد (ADM-02)"
      >
        <form onSubmit={handleCreateRole} className="space-y-3">
          <PetraField label="رمز الدور" required>
            <input
              type="text"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="مثال: ACCOUNTANT_SENIOR"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الاسم العربي" required>
            <input
              type="text"
              value={nameArabic}
              onChange={(e) => setNameArabic(e.target.value)}
              placeholder="مثال: محاسب رئيسي"
              required
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none"
            />
          </PetraField>

          <PetraField label="الاسم الأجنبي">
            <input
              type="text"
              value={nameEnglish}
              onChange={(e) => setNameEnglish(e.target.value)}
              placeholder="Senior Accountant"
              className="w-full border border-[#CBD8E5] rounded px-3 py-1.5 text-sm bg-white focus:border-[#087E87] outline-none font-mono"
            />
          </PetraField>

          <PetraField label="الصلاحيات الممنوحة">
            <div className="border border-[#CBD8E5] rounded p-2 max-h-48 overflow-y-auto space-y-1.5 bg-slate-50">
              {permissions.length === 0 ? (
                <div className="text-xs text-slate-500 text-center py-2">
                  جاري تحميل الصلاحيات...
                </div>
              ) : (
                permissions.map((p) => (
                  <label
                    key={p.code}
                    className="flex items-center gap-2 text-xs text-slate-700 hover:bg-slate-100 p-1 rounded cursor-pointer select-none"
                  >
                    <input
                      type="checkbox"
                      checked={selectedPermissions.has(p.code)}
                      onChange={() => togglePermission(p.code)}
                      className="rounded text-[#087E87] focus:ring-0"
                    />
                    <span className="font-mono font-bold text-[#17334C]">
                      {p.code}
                    </span>
                    <span className="text-slate-400 text-[10px]">
                      ({p.scope})
                    </span>
                  </label>
                ))
              )}
            </div>
          </PetraField>

          <div className="flex justify-end gap-2 pt-3 border-t border-[#CBD8E5]">
            <PetraButton
              variant="secondary"
              onClick={() => setIsNewDialogOpen(false)}
              disabled={creating}
            >
              إلغاء
            </PetraButton>
            <PetraButton variant="primary" type="submit" loading={creating}>
              إنشاء الدور
            </PetraButton>
          </div>
        </form>
      </PetraDialog>
    </div>
  );
}
