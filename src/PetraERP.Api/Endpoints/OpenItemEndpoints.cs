using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Common;
using PetraERP.Application.Parties.OpenItems;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Parties;

namespace PetraERP.Api.Endpoints;

internal static class OpenItemEndpoints
{
    internal static void MapOpenItemEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/open-items")
            .WithTags("Open items");

        var view = PetraPolicies.Permission(PermissionCatalog.PartiesView);
        var manage = PetraPolicies.Permission(PermissionCatalog.JournalsManage);

        group.MapGet("", ListAsync)
            .WithName("ListOpenItems")
            .WithSummary("Lists open customer and supplier items.")
            .RequireAuthorization(view)
            .Produces<IReadOnlyList<OpenItemResponse>>()
            .ProducesStandardProblems();

        group.MapGet("/party-balances", GetPartyBalancesAsync)
            .WithName("GetPartyBalancesSummary")
            .WithSummary("Gets party balance summaries from open items.")
            .RequireAuthorization(view)
            .Produces<IReadOnlyList<PartyBalanceSummaryResponse>>()
            .ProducesStandardProblems();

        group.MapGet("/{openItemId:guid}", GetAsync)
            .WithName("GetOpenItem")
            .WithSummary("Gets an open item by id.")
            .RequireAuthorization(view)
            .Produces<OpenItemResponse>()
            .ProducesStandardProblems();

        group.MapPost("", CreateAsync)
            .WithName("CreateOpenItem")
            .WithSummary("Creates an open item manually or for initial setup.")
            .RequireAuthorization(manage)
            .Validate<CreateOpenItemRequest>()
            .Produces<OpenItemResponse>(StatusCodes.Status201Created)
            .ProducesStandardProblems();
    }

    private static decimal ParseDecimal(string raw, string field)
    {
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new RequestValidationException("open_items.invalid_decimal", $"The value '{raw}' is not a valid decimal.", field);
        return value;
    }

    private static OpenItemResponse ToResponse(this OpenItemDto dto)
    {
        return new OpenItemResponse
        {
            Id = dto.Id,
            CompanyId = dto.CompanyId,
            BranchId = dto.BranchId,
            PartyId = dto.PartyId,
            PartyNameArabic = dto.PartyNameArabic,
            PartyType = dto.PartyType,
            OriginFiscalYearId = dto.OriginFiscalYearId,
            OriginDocumentId = dto.OriginDocumentId,
            OriginDocumentNumber = dto.OriginDocumentNumber,
            OriginDocumentDateUtc = dto.OriginDocumentDateUtc,
            DueDateUtc = dto.DueDateUtc,
            CurrencyCode = dto.CurrencyCode,
            OriginalAmount = FinancialDecimalText.Format(dto.OriginalAmount, FinancialDecimalText.MoneyScale),
            SettledAmount = FinancialDecimalText.Format(dto.SettledAmount, FinancialDecimalText.MoneyScale),
            OutstandingAmount = FinancialDecimalText.Format(dto.OutstandingAmount, FinancialDecimalText.MoneyScale),
            ExchangeRate = FinancialDecimalText.Format(dto.ExchangeRate, FinancialDecimalText.RateScale),
            OriginalBaseCarryingAmount = FinancialDecimalText.Format(dto.OriginalBaseCarryingAmount, FinancialDecimalText.MoneyScale),
            CurrentBaseCarryingAmount = FinancialDecimalText.Format(dto.CurrentBaseCarryingAmount, FinancialDecimalText.MoneyScale),
            OriginType = dto.OriginType,
            SourceReference = dto.SourceReference,
            RolloverBatchId = dto.RolloverBatchId,
            AccountId = dto.AccountId,
            AccountCode = dto.AccountCode,
            State = dto.State,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            VersionBase64 = dto.VersionBase64
        };
    }

    private static async Task<IResult> ListAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] Guid? partyId,
        [FromQuery] byte? partyType,
        [FromQuery] byte? state,
        [FromQuery] string? currencyCode,
        IOpenItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ListOpenItemsAsync(companyId, branchId, partyId, partyType, state, currencyCode, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetPartyBalancesAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] byte? partyType,
        IOpenItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPartyBalancesAsync(companyId, branchId, partyType, cancellationToken).ConfigureAwait(false);
        var response = result.Select(r => new PartyBalanceSummaryResponse
        {
            PartyId = r.PartyId,
            PartyNameArabic = r.PartyNameArabic,
            PartyType = r.PartyType,
            CurrencyCode = r.CurrencyCode,
            TotalOriginal = FinancialDecimalText.Format(r.TotalOriginal, FinancialDecimalText.MoneyScale),
            TotalSettled = FinancialDecimalText.Format(r.TotalSettled, FinancialDecimalText.MoneyScale),
            TotalOutstanding = FinancialDecimalText.Format(r.TotalOutstanding, FinancialDecimalText.MoneyScale),
            TotalBaseCarrying = FinancialDecimalText.Format(r.TotalBaseCarrying, FinancialDecimalText.MoneyScale)
        }).ToList();
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> GetAsync(
        Guid companyId,
        Guid branchId,
        Guid openItemId,
        IOpenItemService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetOpenItemAsync(companyId, openItemId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(result.ToResponse());
    }

    private static async Task<IResult> CreateAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] CreateOpenItemRequest request,
        IOpenItemService service,
        CancellationToken cancellationToken)
    {
        var command = new CreateOpenItemCommand(
            request.BranchId,
            request.PartyId,
            request.PartyType,
            request.OriginFiscalYearId,
            request.OriginDocumentId,
            request.OriginDocumentNumber,
            request.OriginDocumentDateUtc,
            request.DueDateUtc,
            request.CurrencyCode,
            ParseDecimal(request.OriginalAmount, nameof(request.OriginalAmount)),
            ParseDecimal(request.ExchangeRate, nameof(request.ExchangeRate)),
            request.OriginType,
            request.SourceReference,
            request.RolloverBatchId,
            request.AccountId);

        var result = await service.CreateOpenItemAsync(companyId, command, cancellationToken).ConfigureAwait(false);
        var location = $"/api/v1/companies/{companyId:D}/branches/{branchId:D}/open-items/{result.Id:D}";
        return TypedResults.Created(location, result.ToResponse());
    }
}
