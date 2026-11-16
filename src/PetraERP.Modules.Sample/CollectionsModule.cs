using System.Globalization;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Modules.Sample;

/// <summary>
/// The smallest useful external module: it records a cash collection and can cancel it. It is also the template for
/// real modules - everything financial goes through <see cref="IFinancialPostingGateway"/>, nothing else is reachable.
/// </summary>
public sealed class CollectionsModule : IModule, IModuleCommandHandler, IModuleMenuContributor, IModulePermissionContributor
{
    public const string ModuleId = "sample.collections";
    public const string RecordCommand = "collections.record";
    public const string CancelCommand = "collections.cancel";
    public const string PostPermission = "Ledger.Journals.Post";
    public const string ReversePermission = "Ledger.Journals.Reverse";

    private ModuleServiceReferences? services;

    public string MenuPath => "Sales";

    public ModuleDescriptor Describe() => new(
        ModuleId, "Collections (sample)", "1.0.0", ModuleIdentity.CurrentContractVersion, "1.0.0", null, "petra",
        [PostPermission, ReversePermission], []);

    public Task InitializeAsync(ModuleActivationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        services = context.Services;
        context.Log("collections module started", null);
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        services = null;
        return Task.CompletedTask;
    }

    public IReadOnlyList<ModuleMenuItem> GetMenuItems(ModuleRequestContext context) =>
    [
        new("collections.record", "تحصيل نقدي", "Record collection", RecordCommand, PostPermission, 10, []),
        new("collections.cancel", "إلغاء تحصيل", "Cancel collection", CancelCommand, ReversePermission, 20, []),
    ];

    public IReadOnlyList<ModulePermissionDefinition> GetPermissions() =>
    [
        new(PostPermission, "ترحيل القيود", "Post journals", "Ledger", IsFinancialWrite: true),
        new(ReversePermission, "عكس القيود", "Reverse journals", "Ledger", IsFinancialWrite: true),
    ];

    public async Task<ModuleCommandResult> ExecuteAsync(
        ModuleRequestContext context, string commandKey, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        var posting = (services ?? throw new InvalidOperationException("The module is not running.")).Posting;
        try
        {
            switch (commandKey)
            {
                case RecordCommand:
                {
                    var amount = decimal.Parse(Required(arguments, "amount"), CultureInfo.InvariantCulture);
                    var proposal = new PostingProposal(
                        DateOnly.Parse(Required(arguments, "date"), CultureInfo.InvariantCulture),
                        "Cash collection", Required(arguments, "reference"), Guid.Parse(Required(arguments, "key")),
                        [
                            new PostingProposalLine(Guid.Parse(Required(arguments, "cashAccount")), Required(arguments, "currency"), amount, 0m, 1m, "Cash", null),
                            new PostingProposalLine(Guid.Parse(Required(arguments, "revenueAccount")), Required(arguments, "currency"), 0m, amount, 1m, "Revenue", null),
                        ]);
                    var check = await posting.ValidateAsync(context, proposal, cancellationToken).ConfigureAwait(false);
                    if (!check.IsValid) return new ModuleCommandResult(false, check.Reason ?? "The collection is not valid.", new Dictionary<string, string>());
                    var receipt = await posting.PostAsync(context, proposal, cancellationToken).ConfigureAwait(false);
                    return Done(receipt, "Collection recorded.");
                }

                case CancelCommand:
                {
                    var receipt = await posting.ReverseAsync(context, Guid.Parse(Required(arguments, "journalId")), Guid.Parse(Required(arguments, "key")),
                        Required(arguments, "reason"), cancellationToken).ConfigureAwait(false);
                    return Done(receipt, "Collection cancelled.");
                }

                default:
                    return new ModuleCommandResult(false, $"Unknown command '{commandKey}'.", new Dictionary<string, string>());
            }
        }
        catch (ArgumentException exception)
        {
            return new ModuleCommandResult(false, exception.Message, new Dictionary<string, string>());
        }
        catch (FormatException exception)
        {
            return new ModuleCommandResult(false, exception.Message, new Dictionary<string, string>());
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> arguments, string name) =>
        arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"'{name}' is required.");

    private static ModuleCommandResult Done(PostingReceipt receipt, string message) => new(true, message, new Dictionary<string, string>
    {
        ["journalId"] = receipt.JournalId.ToString("D"),
        ["sequence"] = receipt.PostingSequence.ToString(CultureInfo.InvariantCulture),
        ["replayed"] = receipt.WasAlreadyPosted ? "true" : "false",
    });
}
