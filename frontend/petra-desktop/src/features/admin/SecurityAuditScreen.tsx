import { useState, useEffect, useCallback } from "react";
import { useApi } from "@/services/api/ApiContext";
import {
  PetraButton,
  PetraEmptyState,
  PetraLoadingSkeleton,
  PetraErrorDetails,
} from "@/components/ui";

interface AuditEvent {
  id: string;
  occurredAtUtc: string;
  eventType: string;
  userNameSnapshot?: string | null;
  ipAddress?: string | null;
  reason?: string | null;
}

export default function SecurityAuditScreen() {
  const api = useApi();

  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);

  const loadEvents = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.GET("/api/v1/audit/security-events", {
        params: {
          query: {
            page,
            pageSize: 50,
          },
        },
      });
      if (res.error) {
        setError(JSON.stringify(res.error));
      } else if (res.data) {
        setEvents(res.data.items || []);
        setTotalCount(res.data.totalCount || 0);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [api, page]);

  useEffect(() => {
    loadEvents();
  }, [loadEvents]);

  return (
    <div className="petra-screen p-4 flex flex-col h-full bg-[#EEF3F8]">
      {/* Header Bar */}
      <div className="flex items-center justify-between bg-white p-3 border border-[#CBD8E5] rounded mb-3">
        <div className="flex items-center gap-3">
          <h2 className="text-lg font-bold text-[#17334C]">
            سجل التدقيق والأحداث الأمنية (ADM-03)
          </h2>
          <span className="text-xs text-slate-500 bg-slate-100 px-2 py-0.5 rounded">
            إجمالي الأحداث: {totalCount}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <PetraButton
            variant="secondary"
            onClick={() => loadEvents()}
            loading={loading}
          >
            تحديث
          </PetraButton>
        </div>
      </div>

      {/* Main Table */}
      <div className="flex-1 bg-white border border-[#CBD8E5] rounded overflow-hidden flex flex-col">
        {loading ? (
          <PetraLoadingSkeleton rows={10} />
        ) : error ? (
          <PetraErrorDetails message={error} onRetry={loadEvents} />
        ) : events.length === 0 ? (
          <PetraEmptyState
            title="لا توجد أحداث أمنية مسجلة"
            description="السجل فارغ حالياً؛ يتم تسجيل جميع عمليات تسجيل الدخول والعمليات الأمنية تلقائياً."
          />
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm border-collapse text-right">
              <thead className="bg-[#17334C] text-white sticky top-0 z-10 select-none">
                <tr>
                  <th className="p-2 border-l border-slate-700 w-44">التاريخ والوقت (UTC)</th>
                  <th className="p-2 border-l border-slate-700 w-48">نوع الحدث</th>
                  <th className="p-2 border-l border-slate-700 w-36">المستخدم</th>
                  <th className="p-2 border-l border-slate-700 w-36">عنوان IP</th>
                  <th className="p-2">السبب / التفاصيل</th>
                </tr>
              </thead>
              <tbody>
                {events.map((ev, idx) => (
                  <tr
                    key={ev.id || idx}
                    className={`border-b border-[#CBD8E5] hover:bg-[#E2ECF5] ${
                      idx % 2 === 1 ? "bg-[#EFF5FC]" : "bg-white"
                    }`}
                  >
                    <td className="p-2 font-mono text-xs text-slate-600">
                      {ev.occurredAtUtc
                        ? new Date(ev.occurredAtUtc).toLocaleString("ar-EG")
                        : "—"}
                    </td>
                    <td className="p-2">
                      <span className="font-mono text-xs font-bold text-[#087E87] bg-teal-50 px-2 py-0.5 rounded">
                        {ev.eventType}
                      </span>
                    </td>
                    <td className="p-2 font-medium text-[#253447]">
                      {ev.userNameSnapshot || "—"}
                    </td>
                    <td className="p-2 font-mono text-xs text-slate-500">
                      {ev.ipAddress || "—"}
                    </td>
                    <td className="p-2 text-xs text-slate-700">
                      {ev.reason || "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination Bar */}
        <div className="bg-[#EEF3F8] border-t border-[#CBD8E5] p-2 flex items-center justify-between text-xs text-slate-600">
          <span>صفحة {page}</span>
          <div className="flex gap-1">
            <PetraButton
              variant="secondary"
              size="sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              السابق
            </PetraButton>
            <PetraButton
              variant="secondary"
              size="sm"
              disabled={events.length < 50}
              onClick={() => setPage((p) => p + 1)}
            >
              التالي
            </PetraButton>
          </div>
        </div>
      </div>
    </div>
  );
}
