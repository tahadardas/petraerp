# FRONTEND_BACKEND_GAPS — سجل فجوات الواجهة الأمامية مع الخادم (Backend Gaps)

**تاريخ التوثيق:** 19 نوفمبر 2026  
**الجهة المحررة:** Antigravity (مالك الواجهة الأمامية React + TypeScript + Tauri)  
**الجهة المستهدفة:** Claude Code (مالك الباك إند C# + ASP.NET Core + OpenAPI)  
**المرجع:** `srs/PetraERP_Next_React_TypeScript_SRS_MASTER_v2_6_AR.md` و `frontend/petra-desktop/src/services/api/schema.d.ts`

---

## 1. [مُغلقة 19 نوفمبر 2026] فجوة فلترة المصادر في دفتر الأستاذ (Account Ledger Sources Filter)

> أُضيف المعامل `sources` (قائمة مفصولة بفواصل: JOURNAL, SALES_INVOICE, PURCHASE_INVOICE, VOUCHER) إلى المسار؛ جدّدوا `api:generate`.

- **المسار الحالي:**  
  `GET /api/v1/companies/{companyId}/branches/{branchId}/ledger/reports/account-ledger/{accountId}`
- **المعلمات المدعومة حالياً في OpenAPI:**  
  `from` (string), `to` (string), `consolidated` (boolean), `page` (integer), `pageSize` (integer), `cursor` (string).
- **الفجوة:**  
  المسار **لا يدعم** معامل `sources?: string[]` لتصفية حركات الحساب حسب مصدر القيد (مبيعات، مشتريات، مقبوضات، مدفوعات، قيود عامة)، بينما يدعم مسار ميزان المراجعة `trial-balance` ذلك المعامل.
- **إجراء الواجهة:**  
  تم استبعاد حقل اختيار المصادر من واجهة `AccountLedgerScreen` منعاً لإيهام المحاسب بأن الخادم يطبق التصفية.
- **المطلوب من Claude Code:**  
  إضافة `sources?: string[]` إلى معلمات استعلام كشف الحساب وتطبيق الفلترة على `PostingSource` في استعلام SQL.

---

## 2. فجوة سندات الخزينة متعددة طرق الدفع (Multi-Method Treasury Vouchers)

- **المسار الحالي:**  
  `POST /api/v1/companies/{companyId}/branches/{branchId}/treasury/vouchers`
- **الوضع الحالي:**  
  العقد الحالي يدعم إسناد حساب نقدي واحد `cashAccountId` للسند مع سطور توزيع متعددة.
- **الفجوة:**  
  السند متعدد طرق القبض/الدفع (نقدي + شيك + تحويل بنكي في نفس الإيصال) مؤجل وفق قرار المالك في GATE-000 ولا يمتلك عقداً في `schema.d.ts`.
- **إجراء الواجهة:**  
  تم قصر واجهة `TreasuryVoucherCardScreen` على السند أحادي الصندوق/الحساب النقدي مع التوزيع على الأطراف والمصروفات.

---

## 3. [مُحدثة 19 نوفمبر 2026] الأوراق التجارية والتسوية البنكية (Commercial Papers & Bank Reconciliation)

- **المسارات الفعلية في OpenAPI:**  
  - الأوراق التجارية والشيكات: `GET/POST /api/v1/companies/{companyId}/branches/{branchId}/treasury/commercial-papers`, `GET /alerts`, `POST /{paperId}/status`
  - التسويات البنكية: `GET/POST /api/v1/companies/{companyId}/branches/{branchId}/treasury/bank-reconciliations`, `POST /{id}/lines`, `POST /{id}/complete`, `POST /{id}/discard`
- **الحالة:**  
  العقود الخادمية جاهزة في `schema.d.ts` ومتاحة للتنفيذ الأمامي في شاشات `TR-03` (Commercial Papers / Cheques) و `TR-04` (Bank Reconciliations).

---

## 4. فجوة النسخ الاحتياطي المشفر والاسترداد في الكوارث (DR-01 / ADM-08)

- **المسار الحالي:**  
  `GET /api/v1/administration/backups`, `POST /api/v1/administration/backups`
- **الفجوة الموثقة في SRS:**  
  الخادم يدعم النسخ الاحتياطي والاختباري فقط دون إدارة المفاتيح المشفرة من جهة العميل أو استرداد الشركات في بيئة جديدة تماماً قبل تسجيل الدخول.
- **الحالة:**  
  الشاشة مصنفة `BLOCKED_BY_BACKEND` وفق نص SRS الصريح: «استكمال API v2 وBundle/Recovery/المفاتيح والتدقيق الموصوف في SEC-DR-SRS شرط قبل الواجهة الإنتاجية».

---

## 5. فجوة التجميع والتفكيك (INV-09)

- **الحالة:**  
  محجوبة إنتاجياً بحسب تدقيق المالك في SRS `INV-09`، لأن الـ WPF القديم كان يغير الحالة محلياً دون دعم خادمي. لن تنقل إلى React حتى يكتمل الباك إند.
