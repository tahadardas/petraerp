# ADR-0003 — WPF to React + TypeScript + Tauri 2 Desktop Migration

**الحالة:** مقبول (قرار مالك المشروع، 19 نوفمبر 2026). **يلغي** أي تعليمات سابقة تُلزم ببناء شاشات WPF، بما فيها ما ورد في `AGENTS.md`.

## السياق
واجهة Petra الحالية WPF/MVVM (`src/PetraERP.Desktop`). المالك يريد برنامجاً مكتبياً محاسبياً احترافياً بتقنيات الويب، بنظام نوافذ داخلية وجداول كثيفة وإدخال سريع ودعم RTL/LTR وسمتين، يعمل على Windows ويستهلك API الحالي فقط.

## القرار
1. **الواجهة الرسمية:** React + TypeScript (Strict) + Vite + Tauri 2، منصة Windows Desktop، في `frontend/petra-desktop/`، بنظام تصميم «Petra Calm Enterprise».
2. **WPF:** `Legacy / Frozen / Pending Retirement`. لا ميزات WPF جديدة. تبقى الشيفرة في Git مرجعاً وظيفياً؛ يُقترح حذفها من الفرع الأساسي فقط بعد إثبات التكافؤ الوظيفي لكل سير عمل.
3. **Backend دون تغيير:** C#/.NET، ASP.NET Core Web API، Domain/Application/Infrastructure، SQL Server. API هو المصدر الوحيد للبيانات والعمليات.
4. **لا تغيير في قواعد الأعمال والمحاسبة** بسبب الانتقال: الترحيل والأرصدة والمجاميع والترقيم والموافقات والأنماط من الخادم؛ لا يُنسخ منطقها إلى React.
5. **توزيع المسؤوليات:** Claude Code = Backend (API، OpenAPI، الأمن، الترحيل، التقارير، اختبارات التكامل). Antigravity = Frontend. كل وكيل على فرع/Worktree مستقل، ولا يُدمج غير المتحقق في `main`.

## البدائل المرفوضة
- الاستمرار بـ WPF: مرفوض بقرار المالك.
- كتابة Electron: مرفوض لصالح Tauri 2 (حجم أصغر وصلاحيات أصلية مضبوطة).
- إعادة كتابة Backend: خارج النطاق.

## الحماية والتراجع
- نقطة رجوع: الوسم `legacy/wpf-final-2026-11-19` (آخر Commit كان WPF فيه هدف التطوير).
- لا حذف لـ`legacy-data/` ولا لخدمات Backend أو DTOs أو العقود أو مكتبات التقارير أو المكونات المشتركة بسبب استخدام WPF لها.
- بناء Backend منفصل عن WPF: `PetraERP.Backend.slnx` (يستثني `PetraERP.Desktop` و`PetraERP.Desktop.Tests`). `PetraERP.slnx` يبقى يضم كل شيء حتى يُقرَّر سحب WPF.

## نتائج تدقيق الاعتماد على WPF (19 نوفمبر 2026)
- مشاريع المصدر: `PetraERP.Desktop` وحده `UseWPF`، ولا يشير إليه أي مشروع مصدر آخر؛ المستهلك الوحيد `PetraERP.Desktop.Tests`.
- يعتمد WPF على `PetraERP.ApiClient` و`PetraERP.Reporting.OpenSource` (مشتركان؛ لا يُحذفان).
- سكربتات `tools/` (`Start-LocalPreview.ps1`, `Verify-PetraERP-Full.ps1`, …) و`*.bat` تشغّل WPF؛ تبقى للمرجع وتُستبدل تدريجياً بسكربتات الواجهة الجديدة.
- لا يوجد `.github/` (لا CI يعتمد على WPF حالياً).

## معايير سحب WPF (لاحقاً)
تكافؤ وظيفي موثّق لكل سير عمل في `docs/closure/`، اختبارات الواجهة الجديدة خضراء، موافقة المالك الصريحة، ثم اقتراح حذف منفصل.

## مخاطر معروفة
- تعذّر بناء Tauri محلياً دون Rust/MSVC/WebView2؛ التحقق من غلاف Tauri يحتاج بيئة بناء كاملة (انظر `frontend/petra-desktop/README.md`).
- عقود API غير مكتملة لبعض الشاشات (انظر `docs/closure/API_WITHOUT_DESKTOP_CLASSIFICATION.md`).
