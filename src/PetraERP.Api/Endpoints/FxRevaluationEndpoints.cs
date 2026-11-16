using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Currencies;
using PetraERP.Application.Security;
using PetraERP.Contracts.Currencies;
using PetraERP.Contracts.Financial;

namespace PetraERP.Api.Endpoints;

internal static class FxRevaluationEndpoints
{
    internal static void MapFxRevaluationEndpoints(this RouteGroupBuilder api)
    {
        var root = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/currencies/fx-revaluation")
            .WithTags("Currencies and exchange rates");

        var preview = PetraPolicies.Permission(PermissionCatalog.FxRevaluationView);
        var execute = PetraPolicies.Permission(PermissionCatalog.FxRevaluationExecute);

        root.MapPost("/preview", PreviewAsync)
            .WithName("PreviewFxRevaluation")
            .WithSummary("Previews unrealized FX gains/losses across accounts with foreign currency balances as of date.")
            .RequireAuthorization(preview)
            .Validate<FxRevaluationPreviewRequest>()
            .Produces<FxRevaluationPreviewResponse>()
            .ProducesStandardProblems();

        root.MapPost("/rate-audit", AuditRatesAsync)
            .WithName("AuditFxRates")
            .WithSummary("Read-only audit of posted foreign-currency lines whose own rate differs from the exchange-rate bulletin on their date.")
            .RequireAuthorization(preview)
            .Validate<FxRateAuditRequest>()
            .Produces<FxRateAuditResponse>()
            .ProducesStandardProblems();

        root.MapPost("/execute", ExecuteAsync)
            .WithName("ExecuteFxRevaluation")
            .WithSummary("Executes period-end FX revaluation and posts the balancing journal entry.")
            .RequireAuthorization(execute)
            .Validate<ExecuteFxRevaluationRequest>()
            .Produces<FxRevaluationExecutionResponse>()
            .ProducesStandardProblems();
    }

    // A supplied override must be a well-formed, strictly-positive decimal; a malformed or nonpositive value is a
    // validation error, never a silent "no override" fallback.
    private static decimal? ParseOverride(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate) || rate <= 0m)
            throw new RequestValidationException("fx.override_rate_invalid", "The override closing rate must be a positive decimal number.", "overrideClosingRate");
        return rate;
    }

    private static async Task<IResult> PreviewAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] FxRevaluationPreviewRequest request,
        IFxRevaluationService service,
        CancellationToken cancellationToken)
    {
        var overrideRate = ParseOverride(request.OverrideClosingRate);

        var result = await service.PreviewRevaluationAsync(companyId, branchId,
            new FxRevaluationPreviewCommand(request.AsOfDate, request.CurrencyCode, overrideRate),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new FxRevaluationPreviewResponse
        {
            AsOfDate = result.AsOfDate,
            BaseCurrencyCode = result.BaseCurrencyCode,
            Items = result.Items.Select(i => new FxAccountRevaluationItemDto
            {
                AccountId = i.AccountId,
                AccountCode = i.AccountCode,
                AccountNameArabic = i.AccountNameArabic,
                CurrencyCode = i.CurrencyCode,
                ForeignBalance = FinancialDecimalText.Format(i.ForeignBalance, FinancialDecimalText.MoneyScale),
                BookBaseBalance = FinancialDecimalText.Format(i.BookBaseBalance, FinancialDecimalText.MoneyScale),
                ClosingRate = FinancialDecimalText.Format(i.ClosingRate, FinancialDecimalText.RateScale),
                RevaluedBaseBalance = FinancialDecimalText.Format(i.RevaluedBaseBalance, FinancialDecimalText.MoneyScale),
                UnrealizedDifference = FinancialDecimalText.Format(i.UnrealizedDifference, FinancialDecimalText.MoneyScale),
                IsGain = i.IsGain
            }).ToList(),
            TotalUnrealizedGains = FinancialDecimalText.Format(result.TotalUnrealizedGains, FinancialDecimalText.MoneyScale),
            TotalUnrealizedLosses = FinancialDecimalText.Format(result.TotalUnrealizedLosses, FinancialDecimalText.MoneyScale),
            NetDifference = FinancialDecimalText.Format(result.NetDifference, FinancialDecimalText.MoneyScale)
        });
    }

    private static async Task<IResult> AuditRatesAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] FxRateAuditRequest request,
        IFxRevaluationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AuditRatesAsync(companyId, branchId,
            new FxRateAuditCommand(request.From, request.To, request.CurrencyCode), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new FxRateAuditResponse
        {
            From = result.From,
            To = result.To,
            BaseCurrencyCode = result.BaseCurrencyCode,
            LinesChecked = result.LinesChecked,
            LinesWithoutBulletinRate = result.LinesWithoutBulletinRate,
            Items = result.Items.Select(i => new FxRateAuditItemDto
            {
                JournalId = i.JournalId,
                PostingSequence = i.PostingSequence,
                AccountingDate = i.AccountingDate,
                AccountId = i.AccountId,
                AccountCode = i.AccountCode,
                AccountNameArabic = i.AccountNameArabic,
                Description = i.Description,
                CurrencyCode = i.CurrencyCode,
                ForeignAmount = FinancialDecimalText.Format(i.ForeignAmount, FinancialDecimalText.MoneyScale),
                BookedRate = FinancialDecimalText.Format(i.BookedRate, FinancialDecimalText.RateScale),
                BulletinRate = FinancialDecimalText.Format(i.BulletinRate, FinancialDecimalText.RateScale),
                BookedBase = FinancialDecimalText.Format(i.BookedBase, FinancialDecimalText.MoneyScale),
                BulletinBase = FinancialDecimalText.Format(i.BulletinBase, FinancialDecimalText.MoneyScale),
                Difference = FinancialDecimalText.Format(i.Difference, FinancialDecimalText.MoneyScale),
            }).ToList(),
            TotalDifference = FinancialDecimalText.Format(result.TotalDifference, FinancialDecimalText.MoneyScale),
        });
    }

    private static async Task<IResult> ExecuteAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] ExecuteFxRevaluationRequest request,
        IFxRevaluationService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var overrideRate = ParseOverride(request.OverrideClosingRate);

        var result = await service.ExecuteRevaluationAsync(companyId, branchId, http.RequireSession().UserId,
            new ExecuteFxRevaluationCommand(
                request.AsOfDate,
                request.GainAccountId,
                request.LossAccountId,
                request.IdempotencyKey,
                request.CurrencyCode,
                overrideRate,
                request.Description,
                request.SelectedAccountIds),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new FxRevaluationExecutionResponse
        {
            JournalEntryId = result.JournalEntryId,
            JournalEntryNumber = result.JournalEntryNumber,
            RevaluedAccountsCount = result.RevaluedAccountsCount,
            TotalGainAmount = FinancialDecimalText.Format(result.TotalGainAmount, FinancialDecimalText.MoneyScale),
            TotalLossAmount = FinancialDecimalText.Format(result.TotalLossAmount, FinancialDecimalText.MoneyScale),
            NetRevaluationAmount = FinancialDecimalText.Format(result.NetRevaluationAmount, FinancialDecimalText.MoneyScale)
        });
    }
}
