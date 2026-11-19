# Petra ERP Next v2.6 — START HERE / Antigravity — أنماط السندات

## 0. مراجع إلزامية
اقرأ كاملًا وبالترتيب:
1. `PetraERP_Next_React_TypeScript_SRS_MASTER_v2_6_AR.md`، خصوصًا الملحق `AMN-VPAT-026` في نهايته.
2. `PetraERP_Next_AMEEN10_VOUCHER_PATTERN_SRS_v2_6_AR.md`.
3. `PetraERP_Next_AMEEN10_VOUCHER_PATTERN_GAP_MATRIX_v2_6.xlsx`.
4. الشيفرة الحالية للمشروع والأقسام `src/PetraERP.Domain/Patterns`, `src/PetraERP.Infrastructure/Patterns`, `src/PetraERP.Api/Endpoints/PatternEndpoints.cs`, `src/PetraERP.Contracts/Treasury/TreasuryContracts.cs`, `src/PetraERP.Infrastructure/Treasury`, `src/PetraERP.Desktop/Shell/DailyVoucherViewModel.cs`, `src/PetraERP.Application/CompanyOptions`, `src/PetraERP.Infrastructure/Parties`.

## 1. قاعدة لا تتغير
- طرف واحد له صفة زبون ومورد = بطاقة واحدة وحساب Ledger واحد. وثّق خطة تغيير Backend الموجودة قبل أي تنفيذ.
- كشف الطرف كل حركة بسطر بالتاريخ ورقم الأصل والبيان والرصيد الجاري.
- نافذة دفتر الأستاذ الافتراضية: `scope=one,period=year,sources=sales|purchase|receipts|payments|journal,detail=detailed`؛ تحقق API أولًا.
- اختيارات النمط تؤثر في API وDB الحقيقيين، لا موك ولا تصميم وهمي.
- فصل نوع `BusinessOperationKind` عن نوع `VoucherFormKind`، واستحضار أن `LockDocumentDate` ليس تاريخ القطع الذي يوثقه الأمين.
- لا تغيّر قيودًا مرحلة أو مراجع مستندات قائمة دون خطة تحويل وتدقيق ونسخة احتياطية واستعادة اختبارية.

## 2. المهمة الأولى فقط: `AMN-VPAT-GATE-000` — تحليل، دون تعديل الشيفرة
1. اقرأ Git HEAD الحالي، سجل SHA، لا تفترض أنه مرجع v2.6.
2. اقرأ OpenAPI/DTO/Validators/Handlers/Services/EF mapping للمستندات والانماط والصلاحيات والأوراق التجارية.
3. طابق 42 VP requirements مع دليل الأمين الرسمي وروابط السندات الدقيقة.
4. صنف لكل مطلب `IMPLEMENTED+RUNTIME-PASS`, `STATIC_PRESENT`, `CONFIRMED_GAP`, `VERIFY`, `NOT_APPLICABLE` مع file:line أو API test.
5. أنشئ تقرير `VoucherPattern_API_Impact`, خطة Migrations غير هدامة، خطة سلامة الحساب الموحد، Risk Register، وعشرين حالة UAT.
6. **اعرض النتائج على مالك المشروع وتوقف؛ لا تبدأ coding ولا شاشة حتى يتلقى موافقته الصريحة.**

## 3. بعد اعتماد البوابة
نفذ بوابة واحدة في المرة، شاشة واحدة في المرة، افتحها للمستخدم لمراجعة البصرية والعمليات على API حقيقي. لا تتخط موافقته ولا تكتف بنجاح البناء. عند ظهور gap في backend، أصلحه باختبار تكامل قبل متابعة React.
