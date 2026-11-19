using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Patterns;
using PetraERP.Domain.Patterns;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Patterns;

public sealed partial class SqlPatternService(FinancialDbContext context) : IPatternService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public async Task<DocumentPatternDetailsDto> CreatePatternAsync(
        PatternScope scope,
        CreateDocumentPatternCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        await ValidateCompanyAndBranchAsync(scope.CompanyId, scope.BranchId, cancellationToken).ConfigureAwait(false);
        await ValidateBranchGrantsAsync(scope.CompanyId, command.BranchIds, cancellationToken).ConfigureAwait(false);
        var companyPolicy = await PetraERP.Infrastructure.CompanyOptions.SqlCompanyOptionsService.EffectiveAsync(context,scope.CompanyId,cancellationToken).ConfigureAwait(false);
        var initial = command.InitialConfiguration ?? new PatternConfiguration
        {
            NumberingPolicy = new PatternNumberingPolicy {Scope=(PatternNumberingScope)companyPolicy.DefaultNumberingScope,InitialSequence=companyPolicy.InitialNumberSequence},
            ApprovalPolicy = new PatternApprovalPolicy {RequiresApproval=companyPolicy.RequireDocumentApproval},
        };
        var config = await ValidateConfigurationAsync(scope.CompanyId, command.OperationKind, initial,
            cancellationToken).ConfigureAwait(false);

        var trimmedCode = command.Code.Trim();
        var existing = await context.DocumentPatterns
            .AnyAsync(p => p.CompanyId == scope.CompanyId
                        && p.OperationKind == (int)command.OperationKind
                        && p.Code == trimmedCode, cancellationToken)
            .ConfigureAwait(false);

        if (existing)
        {
            throw new ResourceConflictException("Pattern", $"A pattern with code '{trimmedCode}' and operation '{command.OperationKind}' already exists for this company.");
        }

        var patternId = Guid.NewGuid();
        var patternRow = new DocumentPatternRow
        {
            Id = patternId,
            CompanyId = scope.CompanyId,
            OperationKind = (int)command.OperationKind,
            Code = trimmedCode,
            NameArabic = command.NameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim(),
            ShortCode = command.ShortCode.Trim().ToUpperInvariant(),
            Icon = string.IsNullOrWhiteSpace(command.Icon) ? null : command.Icon.Trim(),
            Group = string.IsNullOrWhiteSpace(command.Group) ? null : command.Group.Trim(),
            IsActive = true,
            IsDefault = command.IsDefault,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        if (command.BranchIds is { Count: > 0 })
        {
            foreach (var branchId in command.BranchIds.Distinct())
            {
                patternRow.BranchGrants.Add(new PatternBranchGrantRow
                {
                    Id = Guid.NewGuid(),
                    PatternId = patternId,
                    BranchId = branchId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
            }
        }

        var initialRevision = new PatternRevisionRow
        {
            Id = Guid.NewGuid(),
            PatternId = patternId,
            RevisionNumber = 1,
            State = (int)PatternRevisionState.Draft,
            ConfigurationJson = JsonSerializer.Serialize(config, JsonOptions),
            CreatedByUserId = scope.ActorUserId,
            Reason = "Initial revision",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        patternRow.Revisions.Add(initialRevision);

        context.DocumentPatterns.Add(patternRow);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDetailsDto(patternRow, initialRevision);
    }

    public async Task<DocumentPatternDetailsDto> UpdatePatternAsync(
        PatternScope scope,
        Guid patternId,
        UpdateDocumentPatternCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var pattern = await context.DocumentPatterns
            .Include(p => p.Revisions)
            .Include(p => p.BranchGrants)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (pattern is null)
        {
            throw new ResourceNotFoundException("Pattern");
        }

        if (!command.ExpectedVersion.Matches(pattern.RowVersion))
        {
            throw new ConcurrencyConflictException("Pattern");
        }

        await ValidateBranchGrantsAsync(scope.CompanyId, command.BranchIds, cancellationToken).ConfigureAwait(false);

        pattern.NameArabic = command.NameArabic.Trim();
        pattern.NameEnglish = string.IsNullOrWhiteSpace(command.NameEnglish) ? null : command.NameEnglish.Trim();
        pattern.ShortCode = command.ShortCode.Trim().ToUpperInvariant();
        pattern.Icon = string.IsNullOrWhiteSpace(command.Icon) ? null : command.Icon.Trim();
        pattern.Group = string.IsNullOrWhiteSpace(command.Group) ? null : command.Group.Trim();
        pattern.IsActive = command.IsActive;
        pattern.IsDefault = command.IsDefault;
        pattern.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (command.BranchIds is not null)
        {
            context.PatternBranchGrants.RemoveRange(pattern.BranchGrants);
            pattern.BranchGrants.Clear();

            foreach (var branchId in command.BranchIds.Distinct())
            {
                pattern.BranchGrants.Add(new PatternBranchGrantRow
                {
                    Id = Guid.NewGuid(),
                    PatternId = patternId,
                    BranchId = branchId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var activeRev = pattern.Revisions.FirstOrDefault(r => r.Id == pattern.ActiveRevisionId);
        return ToDetailsDto(pattern, activeRev);
    }

    public async Task<DocumentPatternDetailsDto> GetPatternAsync(
        PatternScope scope,
        Guid patternId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var pattern = await context.DocumentPatterns
            .AsNoTracking()
            .Include(p => p.Revisions)
            .Include(p => p.BranchGrants)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (pattern is null)
        {
            throw new ResourceNotFoundException("Pattern");
        }

        var activeRev = pattern.Revisions.FirstOrDefault(r => r.Id == pattern.ActiveRevisionId);
        return ToDetailsDto(pattern, activeRev);
    }

    public async Task<PagedResult<DocumentPatternSummaryDto>> ListPatternsAsync(
        PatternScope scope,
        BusinessOperationKind? kind,
        bool? activeOnly,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(page);

        var query = context.DocumentPatterns
            .AsNoTracking()
            .Include(p => p.Revisions)
            .Where(p => p.CompanyId == scope.CompanyId);

        if (kind.HasValue)
        {
            query = query.Where(p => p.OperationKind == (int)kind.Value);
        }

        if (activeOnly == true)
        {
            query = query.Where(p => p.IsActive);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderBy(p => p.OperationKind)
            .ThenBy(p => p.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(p => new
            {
                p.Id,
                p.OperationKind,
                p.Code,
                p.NameArabic,
                p.NameEnglish,
                p.ShortCode,
                p.Icon,
                p.Group,
                p.IsActive,
                p.IsDefault,
                p.RowVersion,
                ActiveRevision = p.Revisions.FirstOrDefault(r => r.Id == p.ActiveRevisionId),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dtos = items.Select(i => new DocumentPatternSummaryDto
        {
            Id = i.Id,
            OperationKind = (BusinessOperationKind)i.OperationKind,
            Code = i.Code,
            NameArabic = i.NameArabic,
            NameEnglish = i.NameEnglish,
            ShortCode = i.ShortCode,
            Icon = i.Icon,
            Group = i.Group,
            IsActive = i.IsActive,
            IsDefault = i.IsDefault,
            IsRunnable = BusinessOperationCatalog.IsRunnable((BusinessOperationKind)i.OperationKind),
            ActiveRevisionNumber = i.ActiveRevision?.RevisionNumber,
            Version = ConcurrencyToken.FromBytes(i.RowVersion),
        }).ToList();

        return new PagedResult<DocumentPatternSummaryDto>(dtos, page.Page, page.PageSize, total);
    }

    public async Task<PatternRevisionDto> CreateRevisionAsync(
        PatternScope scope,
        Guid patternId,
        CreatePatternRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var pattern = await context.DocumentPatterns
            .Include(p => p.Revisions)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (pattern is null)
        {
            throw new ResourceNotFoundException("Pattern");
        }

        if (!command.ExpectedVersion.Matches(pattern.RowVersion))
        {
            throw new ConcurrencyConflictException("Pattern");
        }

        var configuration = await ValidateConfigurationAsync(scope.CompanyId, (BusinessOperationKind)pattern.OperationKind, command.Configuration,
            cancellationToken).ConfigureAwait(false);
        var nextRevNum = (pattern.Revisions.Count == 0 ? 0 : pattern.Revisions.Max(r => r.RevisionNumber)) + 1;

        var revision = new PatternRevisionRow
        {
            Id = Guid.NewGuid(),
            PatternId = patternId,
            RevisionNumber = nextRevNum,
            State = (int)PatternRevisionState.Draft,
            ConfigurationJson = JsonSerializer.Serialize(configuration, JsonOptions),
            CreatedByUserId = scope.ActorUserId,
            Reason = command.Reason.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        pattern.Revisions.Add(revision);
        pattern.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToRevisionDto(revision);
    }

    public async Task<DocumentPatternDetailsDto> PublishRevisionAsync(
        PatternScope scope,
        Guid patternId,
        PublishPatternRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var pattern = await context.DocumentPatterns
            .Include(p => p.Revisions)
            .Include(p => p.BranchGrants)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (pattern is null)
        {
            throw new ResourceNotFoundException("Pattern");
        }

        if (!command.ExpectedVersion.Matches(pattern.RowVersion))
        {
            throw new ConcurrencyConflictException("Pattern");
        }

        var opKind = (BusinessOperationKind)pattern.OperationKind;
        if (!BusinessOperationCatalog.IsRunnable(opKind))
        {
            throw new BusinessRuleException(
                RuleViolation.PatternOperationNotRunnable,
                $"Operation '{opKind}' is catalog-only and cannot be published until its engine is implemented.");
        }

        var targetRev = pattern.Revisions.FirstOrDefault(r => r.Id == command.RevisionId);
        if (targetRev is null)
        {
            throw new ResourceNotFoundException("PatternRevision");
        }

        if (targetRev.State is (int)PatternRevisionState.Published)
        {
            throw new BusinessRuleException(RuleViolation.PatternRevisionImmutable, "Revision is already published.");
        }

        if (targetRev.State is (int)PatternRevisionState.Retired)
        {
            throw new BusinessRuleException("pattern.revision_retired", "A retired revision cannot be published.");
        }

        // Fail closed: accounts may have been deactivated since the draft, and the engine needs its required roles.
        await ValidateForPublishAsync(scope.CompanyId, opKind, targetRev, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        // Retire previous published active revision
        var oldActive = pattern.Revisions.FirstOrDefault(r => r.Id == pattern.ActiveRevisionId);
        if (oldActive is not null && oldActive.Id != targetRev.Id)
        {
            oldActive.State = (int)PatternRevisionState.Retired;
            oldActive.EffectiveToUtc = now;
        }

        targetRev.State = (int)PatternRevisionState.Published;
        targetRev.EffectiveFromUtc = now;
        targetRev.EffectiveToUtc = null;

        pattern.ActiveRevisionId = targetRev.Id;
        pattern.UpdatedAtUtc = now;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDetailsDto(pattern, targetRev);
    }

    public async Task<DocumentPatternDetailsDto> RetirePatternAsync(
        PatternScope scope,
        Guid patternId,
        RetirePatternCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var pattern = await context.DocumentPatterns
            .Include(p => p.Revisions)
            .Include(p => p.BranchGrants)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (pattern is null)
        {
            throw new ResourceNotFoundException("Pattern");
        }

        if (!command.ExpectedVersion.Matches(pattern.RowVersion))
        {
            throw new ConcurrencyConflictException("Pattern");
        }

        var now = DateTimeOffset.UtcNow;
        pattern.IsActive = false;
        pattern.UpdatedAtUtc = now;

        var activeRev = pattern.Revisions.FirstOrDefault(r => r.Id == pattern.ActiveRevisionId);
        if (activeRev is not null)
        {
            activeRev.State = (int)PatternRevisionState.Retired;
            activeRev.EffectiveToUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDetailsDto(pattern, activeRev);
    }

    public async Task<PatternNumberResultDto> GenerateNextNumberAsync(
        PatternScope scope,
        GetNextPatternNumberCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await GenerateNumberAsync(scope, command, command.RevisionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PatternNumberResultDto> GenerateNumberAsync(PatternScope scope,
        GetNextPatternNumberCommand command, Guid? revisionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(command);

        var (pattern, activeRev, policy) = await ResolveNumberingAsync(scope, command.PatternId, revisionId, cancellationToken)
            .ConfigureAwait(false);

        return await ReserveDocumentNumberAsync(scope, command, pattern, activeRev, policy, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(DocumentPatternRow Pattern, PatternRevisionRow Revision, PatternNumberingPolicy Policy)> ResolveNumberingAsync(
        PatternScope scope,
        Guid patternId,
        Guid? revisionId,
        CancellationToken cancellationToken)
    {
        var pattern = await context.DocumentPatterns
            .AsNoTracking()
            .Include(p => p.Revisions)
            .Include(p => p.BranchGrants)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (pattern is null)
        {
            throw new ResourceNotFoundException("Pattern");
        }

        var activeRev = pattern.Revisions.FirstOrDefault(r => r.Id == (revisionId ?? pattern.ActiveRevisionId));
        if (activeRev is null || (activeRev.State != (int)PatternRevisionState.Published
            && !(revisionId.HasValue && activeRev.State == (int)PatternRevisionState.Retired)))
        {
            throw new BusinessRuleException(RuleViolation.PatternNoActiveRevision, "Pattern has no published active revision.");
        }

        var config = JsonSerializer.Deserialize<PatternConfiguration>(activeRev.ConfigurationJson, JsonOptions) ?? new();
        return (pattern, activeRev, config.NumberingPolicy);
    }

    public async Task<EffectivePatternDto> GetEffectivePatternAsync(PatternScope scope, Guid patternId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var pattern = await context.DocumentPatterns.AsNoTracking().Include(p => p.Revisions).Include(p => p.BranchGrants)
            .FirstOrDefaultAsync(p => p.Id == patternId && p.CompanyId == scope.CompanyId, cancellationToken).ConfigureAwait(false);
        var revision = pattern?.Revisions.FirstOrDefault(r => r.Id == pattern.ActiveRevisionId);
        // Same visibility as the runnable list, and one answer for "unknown", "inactive", "other branch" and "not published":
        // the endpoint never tells a caller which of them it was.
        if (pattern is null || revision is null || !pattern.IsActive || revision.State != (int)PatternRevisionState.Published
            || !BusinessOperationCatalog.IsRunnable((BusinessOperationKind)pattern.OperationKind)
            || pattern.BranchGrants.Count != 0 && !pattern.BranchGrants.Any(g => g.BranchId == scope.BranchId))
            throw new ResourceNotFoundException("Pattern");

        var kind = (BusinessOperationKind)pattern.OperationKind;
        var configuration = PatternConfigurationUpgrader.ToCurrent(JsonSerializer.Deserialize<PatternConfiguration>(revision.ConfigurationJson, JsonOptions) ?? new());
        if (VoucherPatternRules.IsVoucherOperation(kind))
            configuration = configuration with { Voucher = VoucherPatternRules.Effective(configuration) };
        return new EffectivePatternDto
        {
            PatternId = pattern.Id, RevisionId = revision.Id, RevisionNumber = revision.RevisionNumber, OperationKind = kind,
            Code = pattern.Code, Configuration = configuration,
        };
    }

    public async Task<IReadOnlyList<PatternAccountLookupItemDto>> SearchEligibleAccountsAsync(PatternScope scope, Guid patternId, string? search, int take,
        CancellationToken cancellationToken)
    {
        var effective = await GetEffectivePatternAsync(scope, patternId, cancellationToken).ConfigureAwait(false);
        var lookup = VoucherPatternRules.Effective(effective.Configuration).Lookup;
        var accounts = await context.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId && a.IsActive)
            .Select(a => new { a.Id, a.ParentId, a.Code, a.NameArabic, a.NameEnglish, a.PostingAllowed }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var eligible = lookup.RestrictsAccounts
            ? VoucherPatternRules.EligibleAccounts(lookup, accounts.ToDictionary(a => a.Id, a => a.ParentId))
            : null;
        var text = search?.Trim();
        return accounts
            .Where(a => a.PostingAllowed && (eligible is null || eligible.Contains(a.Id)))
            .Where(a => string.IsNullOrEmpty(text) || a.Code.StartsWith(text, StringComparison.OrdinalIgnoreCase)
                || a.NameArabic.Contains(text, StringComparison.OrdinalIgnoreCase)
                || a.NameEnglish is not null && a.NameEnglish.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Code, StringComparer.Ordinal).Take(Math.Clamp(take, 1, 100))
            .Select(a => new PatternAccountLookupItemDto(a.Id, a.Code, a.NameArabic, a.NameEnglish)).ToList();
    }

    public async Task<IReadOnlyList<DocumentPatternSummaryDto>> GetRunnablePatternsForBranchAsync(
        PatternScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var patterns = await context.DocumentPatterns
            .AsNoTracking()
            .Include(p => p.Revisions)
            .Include(p => p.BranchGrants)
            .Where(p => p.CompanyId == scope.CompanyId && p.IsActive && p.ActiveRevisionId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runnable = patterns
            .Where(p => BusinessOperationCatalog.IsRunnable((BusinessOperationKind)p.OperationKind))
            .Where(p => p.BranchGrants.Count == 0 || p.BranchGrants.Any(g => g.BranchId == scope.BranchId))
            .Select(p =>
            {
                var activeRev = p.Revisions.FirstOrDefault(r => r.Id == p.ActiveRevisionId);
                return new DocumentPatternSummaryDto
                {
                    Id = p.Id,
                    OperationKind = (BusinessOperationKind)p.OperationKind,
                    Code = p.Code,
                    NameArabic = p.NameArabic,
                    NameEnglish = p.NameEnglish,
                    ShortCode = p.ShortCode,
                    Icon = p.Icon,
                    Group = p.Group,
                    IsActive = p.IsActive,
                    IsDefault = p.IsDefault,
                    IsRunnable = true,
                    ActiveRevisionNumber = activeRev?.RevisionNumber,
                    Version = ConcurrencyToken.FromBytes(p.RowVersion),
                };
            })
            .OrderBy(p => p.OperationKind)
            .ThenBy(p => p.Code)
            .ToList();

        return runnable;
    }

    private async Task ValidateCompanyAndBranchAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        var branchBelongs = await context.Branches
            .AnyAsync(b => b.Id == branchId && b.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (!branchBelongs)
        {
            throw new ResourceNotFoundException("Branch");
        }
    }

    private static DocumentPatternDetailsDto ToDetailsDto(DocumentPatternRow pattern, PatternRevisionRow? activeRev)
    {
        return new DocumentPatternDetailsDto
        {
            Id = pattern.Id,
            CompanyId = pattern.CompanyId,
            OperationKind = (BusinessOperationKind)pattern.OperationKind,
            Code = pattern.Code,
            NameArabic = pattern.NameArabic,
            NameEnglish = pattern.NameEnglish,
            ShortCode = pattern.ShortCode,
            Icon = pattern.Icon,
            Group = pattern.Group,
            IsActive = pattern.IsActive,
            IsDefault = pattern.IsDefault,
            IsRunnable = BusinessOperationCatalog.IsRunnable((BusinessOperationKind)pattern.OperationKind),
            Version = ConcurrencyToken.FromBytes(pattern.RowVersion),
            BranchIds = pattern.BranchGrants.Select(g => g.BranchId).ToList(),
            ActiveRevision = activeRev is null ? null : ToRevisionDto(activeRev),
            Revisions = pattern.Revisions.OrderByDescending(r => r.RevisionNumber).Select(ToRevisionDto).ToList(),
        };
    }

    private static PatternRevisionDto ToRevisionDto(PatternRevisionRow rev)
    {
        var config = JsonSerializer.Deserialize<PatternConfiguration>(rev.ConfigurationJson, JsonOptions) ?? new();
        return new PatternRevisionDto
        {
            Id = rev.Id,
            PatternId = rev.PatternId,
            RevisionNumber = rev.RevisionNumber,
            State = (PatternRevisionState)rev.State,
            EffectiveFromUtc = rev.EffectiveFromUtc?.UtcDateTime,
            EffectiveToUtc = rev.EffectiveToUtc?.UtcDateTime,
            Configuration = config,
            CreatedByUserId = rev.CreatedByUserId,
            Reason = rev.Reason,
            CreatedAtUtc = rev.CreatedAtUtc.UtcDateTime,
        };
    }
}
