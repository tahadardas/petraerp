using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Currencies;
using PetraERP.Application.Security;
using PetraERP.Contracts.Currencies;
using PetraERP.Contracts.Financial;

namespace PetraERP.Api.Endpoints;

/// <summary>
/// Company currencies and the append-only exchange-rate register. Reading needs company scope; changing settings or
/// registering a rate needs <c>Ledger.Accounts.Manage</c> (company-level accounting configuration). Rates are decimal strings.
/// </summary>
internal static class CurrencyEndpoints
{
    internal static void MapCurrencyEndpoints(this RouteGroupBuilder api)
    {
        var currencies = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/currencies").WithTags("Currencies and exchange rates");
        var manage = PetraPolicies.Permission(PermissionCatalog.AccountsManage);

        currencies.MapGet("", SettingsAsync).WithName("GetCompanyCurrencySettings")
            .WithSummary("Base currency (with its lock state) and the currencies enabled for the company.")
            .RequireAuthorization(PetraPolicies.CompanyScope).Produces<CompanyCurrencySettingsResponse>().ProducesStandardProblems();
        currencies.MapPost("", EnableAsync).WithName("EnableCompanyCurrency")
            .WithSummary("Defines a currency when new (for example SYP or USD) and enables it for the company, with a reason.")
            .RequireAuthorization(manage).Validate<EnableCurrencyRequest>().Produces<CompanyCurrencySettingsResponse>().ProducesStandardProblems();
        currencies.MapPut("/base", ChangeBaseAsync).WithName("ChangeCompanyBaseCurrency")
            .WithSummary("Changes the base currency only while the company has no journals, documents, stock, prices or rates; otherwise 422 currency.base_locked.")
            .RequireAuthorization(manage).Validate<ChangeBaseCurrencyRequest>().Produces<CompanyCurrencySettingsResponse>().ProducesStandardProblems();
        currencies.MapPut("/{code}/card", UpdateCardAsync).WithName("UpdateCurrencyCard")
            .WithSummary("Edits the currency card (symbol, minor-unit names, decimal places 0..4, rounding mode) with a reason; audited with before/after.")
            .RequireAuthorization(manage).Validate<UpdateCurrencyCardRequest>().Produces<CompanyCurrencySettingsResponse>().ProducesStandardProblems();
        currencies.MapGet("/rates", ListRatesAsync).WithName("ListExchangeRates")
            .WithSummary("Exchange-rate register entries, newest effective date first.")
            .RequireAuthorization(PetraPolicies.CompanyScope).Produces<ExchangeRatePageResponse>().ProducesStandardProblems();
        currencies.MapPost("/rates", RegisterRateAsync).WithName("RegisterExchangeRate")
            .WithSummary("Appends a rate with effective date, source and reason. Entries are never edited; a correction is a new entry.")
            .RequireAuthorization(manage).Validate<RegisterExchangeRateRequest>().Produces<ExchangeRateDto>(StatusCodes.Status201Created).ProducesStandardProblems();
        currencies.MapPost("/rates/bulletin", RegisterBulletinAsync).WithName("RegisterExchangeRateBulletin")
            .WithSummary("Registers a daily rate bulletin (1..50 currencies) atomically; every entry is append-only like a single rate.")
            .RequireAuthorization(manage).Validate<RegisterRateBulletinRequest>().Produces<List<ExchangeRateDto>>(StatusCodes.Status201Created).ProducesStandardProblems();
        currencies.MapGet("/rates/effective", EffectiveRateAsync).WithName("GetEffectiveExchangeRate")
            .WithSummary("The register rate a document dated 'date' would use, or found=false; the base currency is always 1.")
            .RequireAuthorization(PetraPolicies.CompanyScope).Produces<EffectiveExchangeRateResponse>().ProducesStandardProblems();
    }

    private static async Task<IResult> SettingsAsync(Guid companyId, Guid branchId, ICurrencyService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(Map(await service.GetSettingsAsync(companyId, cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> EnableAsync(Guid companyId, Guid branchId, [FromBody] EnableCurrencyRequest request, ICurrencyService service,
        HttpContext http, CancellationToken cancellationToken) =>
        TypedResults.Ok(Map(await service.EnableCurrencyAsync(companyId, http.RequireSession().UserId,
            new EnableCurrencyCommand(request.Code, request.NameArabic, request.NameEnglish, request.Reason), cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> UpdateCardAsync(Guid companyId, Guid branchId, string code, [FromBody] UpdateCurrencyCardRequest request, ICurrencyService service,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PetraERP.Domain.FinancialValues.CurrencyRoundingMode>(request.RoundingMode, ignoreCase: false, out var mode) || !Enum.IsDefined(mode))
            throw new RequestValidationException("currency.rounding_mode_invalid", "Unknown rounding mode.", "roundingMode");
        return TypedResults.Ok(Map(await service.UpdateCardAsync(companyId, http.RequireSession().UserId,
            new UpdateCurrencyCardCommand(code, request.Symbol, request.MinorUnitNameArabic, request.MinorUnitNameEnglish, request.DecimalPlaces, mode, request.Reason),
            cancellationToken).ConfigureAwait(false)));
    }

    private static async Task<IResult> ChangeBaseAsync(Guid companyId, Guid branchId, [FromBody] ChangeBaseCurrencyRequest request, ICurrencyService service,
        HttpContext http, CancellationToken cancellationToken) =>
        TypedResults.Ok(Map(await service.ChangeBaseCurrencyAsync(companyId, http.RequireSession().UserId,
            new ChangeBaseCurrencyCommand(request.Code, request.Reason), cancellationToken).ConfigureAwait(false)));

    private static async Task<IResult> ListRatesAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? from, DateOnly? to, int? page, int? pageSize,
        ICurrencyService service, CancellationToken cancellationToken)
    {
        var result = await service.ListRatesAsync(companyId, new ExchangeRateQuery(currencyCode, from, to, page ?? 1, pageSize ?? 50), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new ExchangeRatePageResponse
        {
            Items = result.Items.Select(Map).ToList(), Page = result.Page, PageSize = result.PageSize, TotalCount = result.TotalCount,
        });
    }

    private static async Task<IResult> RegisterRateAsync(Guid companyId, Guid branchId, [FromBody] RegisterExchangeRateRequest request, ICurrencyService service,
        HttpContext http, CancellationToken cancellationToken)
    {
        if (!FinancialDecimalText.TryParse(request.Rate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var rate) || rate.Value <= 0)
            throw new RequestValidationException("currency.rate_invalid", "Rate must be a positive decimal string with at most eight decimals.", "rate");
        var result = await service.RegisterRateAsync(companyId, http.RequireSession().UserId,
            new RegisterExchangeRateCommand(request.CurrencyCode, rate.Value, request.EffectiveDate, request.Source, request.Reason), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/companies/{companyId:D}/branches/{branchId:D}/currencies/rates/{result.Id:D}", Map(result));
    }

    private static async Task<IResult> RegisterBulletinAsync(Guid companyId, Guid branchId, [FromBody] RegisterRateBulletinRequest request, ICurrencyService service,
        HttpContext http, CancellationToken cancellationToken)
    {
        var lines = new List<BulletinRate>();
        foreach (var line in request.Rates)
        {
            if (!FinancialDecimalText.TryParse(line.Rate, FinancialDecimalText.RateScale, FinancialDecimalText.RateIntegerDigits, out var rate) || rate.Value <= 0)
                throw new RequestValidationException("currency.rate_invalid", "Rate must be a positive decimal string with at most eight decimals.", "rates");
            lines.Add(new BulletinRate(line.CurrencyCode, rate.Value));
        }
        var result = await service.RegisterRateBulletinAsync(companyId, http.RequireSession().UserId,
            new RegisterRateBulletinCommand(request.EffectiveDate, request.Source, request.Reason, lines), cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/v1/companies/{companyId:D}/branches/{branchId:D}/currencies/rates", result.Select(Map).ToList());
    }

    private static async Task<IResult> EffectiveRateAsync(Guid companyId, Guid branchId, string? currencyCode, DateOnly? date, ICurrencyService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || date is null)
            throw new RequestValidationException("currency.query_invalid", "'currencyCode' and 'date' are required.", "currencyCode");
        var r = await service.GetEffectiveRateAsync(companyId, currencyCode, date.Value, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new EffectiveExchangeRateResponse
        {
            CurrencyCode = r.CurrencyCode, BaseCurrencyCode = r.BaseCurrencyCode, Date = r.Date, Found = r.Found,
            Rate = r.Rate is { } value ? FinancialDecimalText.Format(value, FinancialDecimalText.RateScale) : null,
            RateId = r.RateId, EffectiveDate = r.EffectiveDate, Source = r.Source,
        });
    }

    private static CompanyCurrencySettingsResponse Map(CompanyCurrencySettingsResult r) => new()
    {
        CompanyId = r.CompanyId, BaseCurrencyCode = r.BaseCurrencyCode, BaseCurrencyLocked = r.BaseCurrencyLocked, BaseCurrencyLockReason = r.BaseCurrencyLockReason,
        Currencies = r.Currencies.Select(c => new CompanyCurrencyDto { Code = c.Code, NameArabic = c.NameArabic, NameEnglish = c.NameEnglish, IsActive = c.IsActive, IsBase = c.IsBase,
            Symbol = c.Symbol, MinorUnitNameArabic = c.MinorUnitNameArabic, MinorUnitNameEnglish = c.MinorUnitNameEnglish, DecimalPlaces = c.DecimalPlaces,
            RoundingMode = c.RoundingMode.ToString() }).ToList(),
    };

    private static ExchangeRateDto Map(ExchangeRateResult r) => new()
    {
        Id = r.Id, CurrencyCode = r.CurrencyCode, BaseCurrencyCode = r.BaseCurrencyCode, Rate = FinancialDecimalText.Format(r.Rate, FinancialDecimalText.RateScale),
        EffectiveDate = r.EffectiveDate, Source = r.Source, Reason = r.Reason, CreatedByUserId = r.CreatedByUserId, CreatedAtUtc = r.CreatedAtUtc,
    };
}
