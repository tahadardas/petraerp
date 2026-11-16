using System.Collections.Frozen;

namespace PetraERP.Api.Errors;

/// <summary>
/// Human-readable Arabic and English titles for error codes. Clients should localize by <c>code</c>;
/// these titles are a server-side fallback chosen from the request's Accept-Language.
/// </summary>
internal static class ApiErrorCatalog
{
    private static readonly FrozenDictionary<string, (string English, string Arabic)> Titles =
        new Dictionary<string, (string English, string Arabic)>(StringComparer.Ordinal)
        {
            [ApiErrorCodes.Unauthenticated] = ("Authentication is required.", "يلزم تسجيل الدخول."),
            [ApiErrorCodes.TokenExpired] = ("The access token has expired.", "انتهت صلاحية رمز الوصول."),
            [ApiErrorCodes.TokenInvalid] = ("The access token is invalid.", "رمز الوصول غير صالح."),
            [ApiErrorCodes.SessionRevoked] = ("The session has ended. Sign in again.", "انتهت الجلسة. سجّل الدخول من جديد."),
            [ApiErrorCodes.InvalidCredentials] = ("The user name or password is incorrect.", "اسم المستخدم أو كلمة المرور غير صحيحة."),
            [ApiErrorCodes.LockedOut] = ("The account is temporarily locked after repeated failed sign-ins.", "الحساب مقفل مؤقتاً بعد محاولات دخول فاشلة متكررة."),
            [ApiErrorCodes.AccountDisabled] = ("The account is disabled.", "الحساب معطّل."),
            [ApiErrorCodes.InvalidRefreshToken] = ("The session token is invalid.", "رمز الجلسة غير صالح."),
            [ApiErrorCodes.RefreshTokenReused] = ("The session was ended for security reasons.", "أُنهيت الجلسة لأسباب أمنية."),
            [ApiErrorCodes.SessionExpired] = ("The session has expired.", "انتهت صلاحية الجلسة."),
            [ApiErrorCodes.InvalidCurrentPassword] = ("The current password is incorrect.", "كلمة المرور الحالية غير صحيحة."),
            [ApiErrorCodes.PasswordChangeRequired] = ("You must change your password before continuing.", "يجب تغيير كلمة المرور قبل المتابعة."),
            [ApiErrorCodes.Forbidden] = ("You are not allowed to perform this operation.", "لا تملك صلاحية تنفيذ هذه العملية."),
            [ApiErrorCodes.PermissionDenied] = ("You do not have the required permission.", "لا تملك الصلاحية المطلوبة."),
            [ApiErrorCodes.CompanyScopeDenied] = ("You are not authorized for this company.", "غير مصرّح لك بالعمل على هذه الشركة."),
            [ApiErrorCodes.BranchScopeDenied] = ("You are not authorized for this branch.", "غير مصرّح لك بالعمل على هذا الفرع."),
            [ApiErrorCodes.UserInactive] = ("The user account is not active.", "حساب المستخدم غير فعّال."),
            [ApiErrorCodes.RequestInvalid] = ("The request is invalid.", "الطلب غير صالح."),
            [ApiErrorCodes.RequestMalformed] = ("The request body is malformed.", "صيغة محتوى الطلب غير صحيحة."),
            [ApiErrorCodes.RequestTooLarge] = ("The request is too large.", "حجم الطلب أكبر من المسموح."),
            [ApiErrorCodes.RequestRateLimited] = ("Too many requests. Try again shortly.", "طلبات كثيرة جداً. حاول بعد قليل."),
            [ApiErrorCodes.IdempotencyKeyInvalid] = ("A valid Idempotency-Key header is required.", "يلزم ترويسة Idempotency-Key صالحة."),
            [ApiErrorCodes.VersionInvalid] = ("The concurrency version is invalid.", "رقم الإصدار غير صالح."),
            [ApiErrorCodes.NotFound] = ("The requested resource was not found.", "المورد المطلوب غير موجود."),
            [ApiErrorCodes.MethodNotAllowed] = ("The HTTP method is not allowed.", "طريقة الطلب غير مسموحة."),
            [ApiErrorCodes.ConcurrencyConflict] = ("The record was changed by someone else. Reload and try again.", "عدّل مستخدم آخر هذا السجل. أعد التحميل وحاول مجدداً."),
            [ApiErrorCodes.IdempotencyConflict] = ("This idempotency key was already used for a different request.", "استُخدم مفتاح منع التكرار لطلب مختلف."),
            [ApiErrorCodes.PostingRejected] = ("The posting was rejected by accounting rules.", "رُفض الترحيل وفق القواعد المحاسبية."),
            [ApiErrorCodes.TransientFailure] = ("A temporary server condition occurred. Retry the same request.", "حدث عائق مؤقت في الخادم. أعد إرسال الطلب نفسه."),
            [ApiErrorCodes.Unexpected] = ("An unexpected server error occurred.", "حدث خطأ غير متوقع في الخادم."),
            [ApiErrorCodes.ServiceUnavailable] = ("The service is not ready.", "الخدمة غير جاهزة."),
            ["security.user_name_invalid"] = ("The login name is invalid.", "اسم الدخول غير صالح."),
            ["security.user_name_taken"] = ("The login name is already in use.", "اسم الدخول مستخدم مسبقاً."),
            ["security.role_code_invalid"] = ("The role code is invalid.", "رمز الدور غير صالح."),
            ["security.role_code_taken"] = ("The role code is already in use.", "رمز الدور مستخدم مسبقاً."),
            ["security.unknown_role"] = ("An unknown role was supplied.", "دور غير معروف."),
            ["security.unknown_permission"] = ("An unknown permission was supplied.", "صلاحية غير معروفة."),
            ["security.unknown_company"] = ("An unknown company was supplied.", "شركة غير معروفة."),
            ["security.branch_not_in_company"] = ("A branch does not belong to the selected company.", "فرع لا يتبع الشركة المحددة."),
            ["security.last_administrator"] = ("At least one active administrator must remain.", "يجب أن يبقى مدير نظام فعّال واحد على الأقل."),
            ["security.cannot_disable_self"] = ("You cannot disable your own account.", "لا يمكنك تعطيل حسابك."),
            ["security.bootstrap_completed"] = ("The installation is already initialized.", "تمت تهيئة التثبيت مسبقاً."),
            ["security.password_policy.too_short"] = ("The password must be at least 10 characters.", "يجب ألا تقل كلمة المرور عن 10 محارف."),
            ["security.password_policy.too_long"] = ("The password is too long.", "كلمة المرور طويلة جداً."),
            ["security.password_policy.contains_user_name"] = ("The password must not contain the login name.", "يجب ألا تحتوي كلمة المرور على اسم الدخول."),
            ["security.password_policy.reuses_current"] = ("The new password must differ from the current one.", "يجب أن تختلف كلمة المرور الجديدة عن الحالية."),
            ["security.password_policy.whitespace_only"] = ("The password cannot be only spaces.", "لا يمكن أن تكون كلمة المرور فراغات فقط."),
            ["security.password_policy.invalid_characters"] = ("The password contains unsupported characters.", "تحتوي كلمة المرور على محارف غير مدعومة."),
            ["security.password_policy.required"] = ("A password is required.", "كلمة المرور مطلوبة."),
            ["ledger.period_closed"] = ("The accounting period is closed for posting.", "الفترة المحاسبية مقفلة أمام الترحيل."),
            ["ledger.period_missing"] = ("No open fiscal year or period covers this date.", "لا توجد سنة مالية أو فترة تغطي هذا التاريخ."),
            ["ledger.period_transition_invalid"] = ("This period state change is not allowed.", "تغيير حالة الفترة هذا غير مسموح."),
            ["ledger.account_invalid"] = ("The account is inactive, a control account, or not in this company.", "الحساب غير فعّال أو حساب رئيسي أو لا يتبع هذه الشركة."),
            ["ledger.cost_center_required"] = ("This account requires a cost center.", "هذا الحساب يتطلب مركز كلفة."),
            ["ledger.cost_center_not_allowed"] = ("This account does not accept a cost center.", "هذا الحساب لا يقبل مركز كلفة."),
            ["ledger.cost_center_invalid"] = ("The cost center is inactive or not in this company.", "مركز الكلفة غير فعّال أو لا يتبع هذه الشركة."),
            ["ledger.currency_invalid"] = ("The currency is not permitted for this company or account.", "العملة غير مسموحة لهذه الشركة أو هذا الحساب."),
            ["ledger.journal_state_invalid"] = ("The journal is not in a state that allows this action.", "حالة القيد لا تسمح بهذا الإجراء."),
            ["ledger.journal_unbalanced"] = ("The journal is not balanced.", "القيد غير متوازن."),
            ["ledger.posting_conflict"] = ("The journal was already reversed or has a pending reversal.", "القيد معكوس مسبقاً أو له قيد عكسي قيد الإعداد."),
            ["ledger.scope_inactive"] = ("The company or branch is inactive.", "الشركة أو الفرع غير فعّال."),
            ["ledger.account_parent_invalid"] = ("The parent must be an active control account of the same type.", "يجب أن يكون الحساب الأب حساباً رئيسياً فعّالاً من النوع نفسه."),
            ["ledger.account_hierarchy_cycle"] = ("An account cannot be placed under itself or its descendant.", "لا يمكن وضع الحساب تحت نفسه أو تحت أحد فروعه."),
            ["ledger.account_in_use"] = ("This account has journal lines; its code, type, currency and posting capability are fixed.", "للحساب حركات قيود؛ رمزه ونوعه وعملته وقابلية الترحيل إليه ثابتة."),
            ["ledger.account_has_children"] = ("This account has child accounts.", "لهذا الحساب حسابات فرعية."),
            ["ledger.account_code_taken"] = ("The account code is already used in this company.", "رمز الحساب مستخدم في هذه الشركة."),
            ["ledger.cost_center_code_taken"] = ("The cost center code is already used in this company.", "رمز مركز الكلفة مستخدم في هذه الشركة."),
            ["ledger.cost_center_in_use"] = ("This cost center has journal lines; its code is fixed.", "لمركز الكلفة حركات قيود؛ رمزه ثابت."),
            ["ledger.fiscal_year_overlap"] = ("The fiscal year overlaps an existing fiscal year.", "تتداخل السنة المالية مع سنة موجودة."),
            ["ledger.fiscal_year_code_taken"] = ("The fiscal year code is already used.", "رمز السنة المالية مستخدم."),
            ["ledger.fiscal_year_invalid"] = ("The fiscal year or its periods are invalid.", "السنة المالية أو فتراتها غير صالحة."),
            ["ledger.report_range_invalid"] = ("The report range is invalid.", "نطاق التقرير غير صالح."),
            ["request.reason_invalid"] = ("A reason of up to 500 characters is required.", "يلزم ذكر سبب لا يتجاوز 500 محرف."),
            ["request.name_invalid"] = ("The name is invalid.", "الاسم غير صالح."),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static string Title(string code, int status, bool arabic)
    {
        if (Titles.TryGetValue(code, out var title))
        {
            return arabic ? title.Arabic : title.English;
        }

        return status switch
        {
            400 => arabic ? "الطلب غير صالح." : "The request is invalid.",
            401 => arabic ? "يلزم تسجيل الدخول." : "Authentication is required.",
            403 => arabic ? "العملية غير مسموحة." : "The operation is not allowed.",
            404 => arabic ? "المورد المطلوب غير موجود." : "The requested resource was not found.",
            409 => arabic ? "تعارض مع حالة السجل الحالية." : "The request conflicts with the current state.",
            422 => arabic ? "خالف الطلب قاعدة عمل." : "The request violates a business rule.",
            _ => arabic ? "حدث خطأ." : "An error occurred.",
        };
    }

    internal static string DefaultCode(int status) => status switch
    {
        400 => ApiErrorCodes.RequestInvalid,
        401 => ApiErrorCodes.Unauthenticated,
        403 => ApiErrorCodes.Forbidden,
        404 => ApiErrorCodes.NotFound,
        405 => ApiErrorCodes.MethodNotAllowed,
        409 => ApiErrorCodes.ConcurrencyConflict,
        413 => ApiErrorCodes.RequestTooLarge,
        429 => ApiErrorCodes.RequestRateLimited,
        503 => ApiErrorCodes.ServiceUnavailable,
        >= 500 => ApiErrorCodes.Unexpected,
        _ => ApiErrorCodes.RequestInvalid,
    };
}
