# FRONTEND_BACKEND_GAPS — سجل فجوات الواجهة الأمامية مع الخادم (Backend Gaps)

**تاريخ التوثيق:** 19 نوفمبر 2026  
**الجهة المحررة:** Antigravity (مالك الواجهة الأمامية React + TypeScript + Tauri)  
**الجهة المستهدفة:** Claude Code (مالك الباك إند C# + ASP.NET Core + OpenAPI)  
**المرجع:** `srs/PetraERP_Next_React_TypeScript_SRS_MASTER_v2_6_AR.md` و `frontend/petra-desktop/src/services/api/schema.d.ts`

---

## 1. فجوة فلترة المصادر في دفتر الأستاذ (Account Ledger Sources Filter)

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

## 3. فجوة الأوراق المالية والشيكات والتسوية البنكية (Cheques & Bank Reconciliation)

- **المسارات المفقودة:**  
  - لا توجد مسارات `/api/v1/.../treasury/cheques` لإدارة دورة حياة الشيكات (استلام، برسم التحصيل، تحصيل، إرجاع).
  - لا توجد مسارات `/api/v1/.../treasury/bank-reconciliation` لاستيراد كشوف البنك والمطابقة الآلية.
- **إجراء الواجهة:**  
  تم إدراج الشاشات كـ `SCHEDULED` في مصفوفة الشاشات مع تعليق التنفيذ حتى توفير عقود OpenAPI.

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
