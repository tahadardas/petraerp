using Microsoft.AspNetCore.Mvc;
using PetraERP.Api.Errors;
using PetraERP.Api.Security;
using PetraERP.Application.Parties;
using PetraERP.Application.Security;
using PetraERP.Contracts.Financial;
using PetraERP.Contracts.Parties;
using AppRole = PetraERP.Domain.Parties.PartyRole;
using AppCreditKind = PetraERP.Domain.Parties.CreditPolicyKind;
using ContractRole = PetraERP.Contracts.Parties.PartyRole;
using ContractCreditKind = PetraERP.Contracts.Parties.CreditPolicyKind;

namespace PetraERP.Api.Endpoints;

internal static class PartyEndpoints
{
    internal static void MapPartyEndpoints(this RouteGroupBuilder api)
    {
        var parties = api.MapGroup("/companies/{companyId:guid}/branches/{branchId:guid}/parties")
            .WithTags("Parties");

        var view = PetraPolicies.Permission(PermissionCatalog.PartiesView);
        var manage = PetraPolicies.Permission(PermissionCatalog.PartiesManage);

        parties.MapGet(string.Empty, ListPartiesAsync)
            .WithName("ListParties")
            .WithSummary("Lists parties (customers, suppliers, or both) for company.")
            .RequireAuthorization(view)
            .Produces<PartyListResponse>()
            .ProducesStandardProblems();

        parties.MapGet("/account-plan", GetAccountPlanAsync)
            .WithName("GetPartyAccountPlan")
            .WithSummary("Previews the heading and code of the account a party saved without one would get. Read-only.")
            .RequireAuthorization(view)
            .Produces<PartyAccountPlanDto>()
            .ProducesStandardProblems();

        parties.MapGet("/lookup", LookupPartiesAsync)
            .WithName("LookupParties")
            .WithSummary("Fast type-ahead lookup for parties with deterministic token-prefix matching.")
            .RequireAuthorization(view)
            .Produces<IReadOnlyList<PartyLookupDto>>()
            .ProducesStandardProblems();

        parties.MapPost(string.Empty, CreatePartyAsync)
            .WithName("CreateParty")
            .WithSummary("Creates a new party (customer, supplier, or both).")
            .RequireAuthorization(manage)
            .Validate<CreatePartyRequest>()
            .Produces<PartyDetailDto>(StatusCodes.Status201Created)
            .ProducesStandardProblems();

        parties.MapGet("/{partyId:guid}", GetPartyAsync)
            .WithName("GetParty")
            .WithSummary("Gets party details including contacts, addresses, and balance exposure.")
            .RequireAuthorization(view)
            .Produces<PartyDetailDto>()
            .ProducesStandardProblems();

        parties.MapPut("/{partyId:guid}", UpdatePartyAsync)
            .WithName("UpdateParty")
            .WithSummary("Updates party details with optimistic concurrency check.")
            .RequireAuthorization(manage)
            .Validate<UpdatePartyRequest>()
            .Produces<PartyDetailDto>()
            .ProducesStandardProblems();

        parties.MapPost("/{partyId:guid}/deactivate", DeactivatePartyAsync)
            .WithName("DeactivateParty")
            .WithSummary("Deactivates a party preserving history and linked documents.")
            .RequireAuthorization(manage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesStandardProblems();

        parties.MapPost("/{partyId:guid}/reactivate", ReactivatePartyAsync)
            .WithName("ReactivateParty")
            .WithSummary("Reactivates a previously deactivated party.")
            .RequireAuthorization(manage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesStandardProblems();

        parties.MapGet("/{partyId:guid}/credit-exposure", GetCreditExposureAsync)
            .WithName("GetPartyCreditExposure")
            .WithSummary("Gets party credit exposure, debits, credits, and current posted balance.")
            .RequireAuthorization(view)
            .Produces<PartyCreditExposureDto>()
            .ProducesStandardProblems();
    }

    private static async Task<IResult> ListPartiesAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] string? search,
        [FromQuery] ContractRole? role,
        [FromQuery] bool? isActive,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        IPartyService service,
        CancellationToken cancellationToken)
    {
        var query = new PartySearchQuery(
            SearchText: search,
            RoleFilter: role.HasValue ? (AppRole)role.Value : null,
            IsActiveFilter: isActive,
            PageNumber: page <= 0 ? 1 : page,
            PageSize: pageSize <= 0 ? 50 : pageSize);

        var result = await service.GetPartiesAsync(companyId, query, cancellationToken).ConfigureAwait(false);

        var response = new PartyListResponse
        {
            Items = result.Items.Select(MapSummary).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> GetAccountPlanAsync(Guid companyId, Guid branchId, IPartyService service, CancellationToken cancellationToken)
    {
        var plan = await service.GetAccountPlanAsync(companyId, cancellationToken).ConfigureAwait(false);
        static PartyAccountPlanSideDto Side(PetraERP.Application.Parties.PartyAccountPlanSide side) => new()
        {
            ParentAccountId = side.ParentAccountId, ParentCode = side.ParentCode, ParentNameArabic = side.ParentNameArabic,
            NextAccountCode = side.NextAccountCode, FromCompanyPolicy = side.FromCompanyPolicy, ParentWillBeCreated = side.ParentWillBeCreated,
        };
        return TypedResults.Ok(new PartyAccountPlanDto { Receivable = Side(plan.Receivable), Payable = Side(plan.Payable) });
    }

    private static async Task<IResult> LookupPartiesAsync(
        Guid companyId,
        Guid branchId,
        [FromQuery] string? search,
        [FromQuery] ContractRole? role,
        [FromQuery] int maxResults,
        IPartyService service,
        CancellationToken cancellationToken)
    {
        var appRole = role.HasValue ? (AppRole?)role.Value : null;
        var limit = maxResults <= 0 ? 20 : maxResults;

        var items = await service.LookupPartiesAsync(companyId, search, appRole, limit, cancellationToken).ConfigureAwait(false);
        var dtos = items.Select(MapLookup).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> CreatePartyAsync(
        Guid companyId,
        Guid branchId,
        [FromBody] CreatePartyRequest request,
        IPartyService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!FinancialDecimalText.TryParse(request.CreditLimit, FinancialDecimalText.MoneyScale, 15, out var creditLimit))
        {
            return TypedResults.Problem("Credit limit must be a valid monetary decimal.", statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new CreatePartyCommand(
            Code: request.Code,
            NameArabic: request.NameArabic,
            NameEnglish: request.NameEnglish,
            Role: (AppRole)request.Role,
            TaxNumber: request.TaxNumber,
            CommercialRegistration: request.CommercialRegistration,
            DefaultCurrencyCode: request.DefaultCurrencyCode,
            ReceivableAccountId: request.ReceivableAccountId,
            PayableAccountId: request.PayableAccountId,
            CreditLimit: creditLimit ?? 0m,
            PaymentTermDays: request.PaymentTermDays,
            PolicyKind: (AppCreditKind)request.PolicyKind,
            Contacts: request.Contacts?.Select(c => new PartyContactInput(c.Id, c.Name, c.Role, c.Phone, c.Mobile, c.Email)).ToList(),
            Addresses: request.Addresses?.Select(a => new PartyAddressInput(a.Id, a.Label, a.City, a.Street, a.PostalCode, a.Notes)).ToList(),
            Profile: PartyProfileMapping.ToData(request.Profile))
        {
            IsCreditBlocked = request.IsCreditBlocked,
            ChangedByUserId = httpContext.RequireSession().UserId,
            ChangedInBranchId = branchId,
        };

        var result = await service.CreatePartyAsync(companyId, command, cancellationToken).ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/companies/{companyId}/branches/{branchId}/parties/{result.Id}",
            MapDetail(result));
    }

    private static async Task<IResult> GetPartyAsync(
        Guid companyId,
        Guid branchId,
        Guid partyId,
        IPartyService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPartyByIdAsync(companyId, partyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> UpdatePartyAsync(
        Guid companyId,
        Guid branchId,
        Guid partyId,
        [FromBody] UpdatePartyRequest request,
        IPartyService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!FinancialDecimalText.TryParse(request.CreditLimit, FinancialDecimalText.MoneyScale, 15, out var creditLimit))
        {
            return TypedResults.Problem("Credit limit must be a valid monetary decimal.", statusCode: StatusCodes.Status400BadRequest);
        }

        var command = new UpdatePartyCommand(
            NameArabic: request.NameArabic,
            NameEnglish: request.NameEnglish,
            Role: (AppRole)request.Role,
            TaxNumber: request.TaxNumber,
            CommercialRegistration: request.CommercialRegistration,
            DefaultCurrencyCode: request.DefaultCurrencyCode,
            ReceivableAccountId: request.ReceivableAccountId,
            PayableAccountId: request.PayableAccountId,
            CreditLimit: creditLimit ?? 0m,
            PaymentTermDays: request.PaymentTermDays,
            PolicyKind: (AppCreditKind)request.PolicyKind,
            ExpectedVersion: request.ExpectedVersion,
            Contacts: request.Contacts?.Select(c => new PartyContactInput(c.Id, c.Name, c.Role, c.Phone, c.Mobile, c.Email)).ToList(),
            Addresses: request.Addresses?.Select(a => new PartyAddressInput(a.Id, a.Label, a.City, a.Street, a.PostalCode, a.Notes)).ToList(),
            Profile: PartyProfileMapping.ToData(request.Profile))
        {
            IsCreditBlocked = request.IsCreditBlocked,
            ChangedByUserId = httpContext.RequireSession().UserId,
            ChangedInBranchId = branchId,
        };

        var result = await service.UpdatePartyAsync(companyId, partyId, command, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(MapDetail(result));
    }

    private static async Task<IResult> DeactivatePartyAsync(
        Guid companyId,
        Guid branchId,
        Guid partyId,
        IPartyService service,
        CancellationToken cancellationToken)
    {
        await service.DeactivatePartyAsync(companyId, partyId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ReactivatePartyAsync(
        Guid companyId,
        Guid branchId,
        Guid partyId,
        IPartyService service,
        CancellationToken cancellationToken)
    {
        await service.ReactivatePartyAsync(companyId, partyId, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetCreditExposureAsync(
        Guid companyId,
        Guid branchId,
        Guid partyId,
        IPartyService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPartyCreditExposureAsync(companyId, partyId, cancellationToken).ConfigureAwait(false);

        var dto = new PartyCreditExposureDto
        {
            PartyId = result.PartyId,
            CreditLimit = FinancialDecimalText.Format(result.CreditLimit, FinancialDecimalText.MoneyScale),
            TotalDebit = FinancialDecimalText.Format(result.TotalDebit, FinancialDecimalText.MoneyScale),
            TotalCredit = FinancialDecimalText.Format(result.TotalCredit, FinancialDecimalText.MoneyScale),
            CurrentBalance = FinancialDecimalText.Format(result.CurrentBalance, FinancialDecimalText.MoneyScale),
            AvailableCredit = FinancialDecimalText.Format(result.AvailableCredit, FinancialDecimalText.MoneyScale),
            PolicyKind = (ContractCreditKind)result.PolicyKind,
            IsExceeded = result.IsExceeded
        };

        return TypedResults.Ok(dto);
    }

    private static PartySummaryDto MapSummary(PartySummaryResult r) => new()
    {
        Id = r.Id,
        CompanyId = r.CompanyId,
        Code = r.Code,
        NameArabic = r.NameArabic,
        NameEnglish = r.NameEnglish,
        Role = (ContractRole)r.Role,
        IsActive = r.IsActive,
        IsCreditBlocked = r.IsCreditBlocked,
        IsCashCustomer = r.IsCashCustomer,
        ReceivableBalance = FinancialDecimalText.Format(r.ReceivableBalance, FinancialDecimalText.MoneyScale),
        PayableBalance = FinancialDecimalText.Format(r.PayableBalance, FinancialDecimalText.MoneyScale),
        TaxNumber = r.TaxNumber,
        DefaultCurrencyCode = r.DefaultCurrencyCode,
        ReceivableAccountId = r.ReceivableAccountId,
        PayableAccountId = r.PayableAccountId,
        CreditLimit = FinancialDecimalText.Format(r.CreditLimit, FinancialDecimalText.MoneyScale),
        PaymentTermDays = r.PaymentTermDays,
        PolicyKind = (ContractCreditKind)r.PolicyKind,
        CurrentBalance = FinancialDecimalText.Format(r.CurrentBalance, FinancialDecimalText.MoneyScale),
        AvailableCredit = FinancialDecimalText.Format(r.AvailableCredit, FinancialDecimalText.MoneyScale)
    };

    private static PartyDetailDto MapDetail(PartyDetailResult r) => new()
    {
        Profile = PartyProfileMapping.ToDto(r.Profile),
        Id = r.Id,
        CompanyId = r.CompanyId,
        Code = r.Code,
        NameArabic = r.NameArabic,
        NameEnglish = r.NameEnglish,
        Role = (ContractRole)r.Role,
        IsActive = r.IsActive,
        IsCreditBlocked = r.IsCreditBlocked,
        IsCashCustomer = r.IsCashCustomer,
        ReceivableBalance = FinancialDecimalText.Format(r.ReceivableBalance, FinancialDecimalText.MoneyScale),
        PayableBalance = FinancialDecimalText.Format(r.PayableBalance, FinancialDecimalText.MoneyScale),
        TaxNumber = r.TaxNumber,
        CommercialRegistration = r.CommercialRegistration,
        DefaultCurrencyCode = r.DefaultCurrencyCode,
        ReceivableAccountId = r.ReceivableAccountId,
        PayableAccountId = r.PayableAccountId,
        CreditLimit = FinancialDecimalText.Format(r.CreditLimit, FinancialDecimalText.MoneyScale),
        PaymentTermDays = r.PaymentTermDays,
        PolicyKind = (ContractCreditKind)r.PolicyKind,
        CurrentBalance = FinancialDecimalText.Format(r.CurrentBalance, FinancialDecimalText.MoneyScale),
        AvailableCredit = FinancialDecimalText.Format(r.AvailableCredit, FinancialDecimalText.MoneyScale),
        Contacts = r.Contacts.Select(c => new PartyContactDto { Id = c.Id, Name = c.Name, Role = c.Role, Phone = c.Phone, Mobile = c.Mobile, Email = c.Email }).ToList(),
        Addresses = r.Addresses.Select(a => new PartyAddressDto { Id = a.Id, Label = a.Label, City = a.City, Street = a.Street, PostalCode = a.PostalCode, Notes = a.Notes }).ToList(),
        CreatedAtUtc = r.CreatedAtUtc,
        UpdatedAtUtc = r.UpdatedAtUtc,
        PolicyHistory = r.PolicyHistory.Select(a => new PartyPolicyAuditDto
        {
            Id = a.Id, ActorId = a.ActorId, BeforeCreditLimit = FinancialDecimalText.Format(a.BeforeCreditLimit,FinancialDecimalText.MoneyScale),
            AfterCreditLimit = FinancialDecimalText.Format(a.AfterCreditLimit,FinancialDecimalText.MoneyScale),
            BeforePolicy = a.BeforePolicy, AfterPolicy = a.AfterPolicy, BeforeBlocked = a.BeforeBlocked, AfterBlocked = a.AfterBlocked, DateUtc = a.DateUtc,
        }).ToList(),
        VersionToken = r.VersionToken
    };

    private static PartyLookupDto MapLookup(PartyLookupItem r) => new()
    {
        Id = r.Id,
        Code = r.Code,
        NameArabic = r.NameArabic,
        NameEnglish = r.NameEnglish,
        Role = (ContractRole)r.Role,
        IsActive = r.IsActive,
        IsCreditBlocked = r.IsCreditBlocked,
        IsCashCustomer = r.IsCashCustomer,
        ReceivableBalance = FinancialDecimalText.Format(r.ReceivableBalance, FinancialDecimalText.MoneyScale),
        PayableBalance = FinancialDecimalText.Format(r.PayableBalance, FinancialDecimalText.MoneyScale),
        ReceivableAccountId = r.ReceivableAccountId,
        PayableAccountId = r.PayableAccountId,
        CreditLimit = FinancialDecimalText.Format(r.CreditLimit, FinancialDecimalText.MoneyScale),
        CurrentBalance = FinancialDecimalText.Format(r.CurrentBalance, FinancialDecimalText.MoneyScale),
        AvailableCredit = FinancialDecimalText.Format(r.AvailableCredit, FinancialDecimalText.MoneyScale)
    };
}
