using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Api.Hosting;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Workspaces;
using PetraERP.Application.Workspaces;
using System.Text.Json;

namespace PetraERP.Api.Cli;

/// <summary>
/// Server-side installation commands. They are deliberately unavailable over HTTP: running them requires
/// operating-system access to the API host and its protected configuration.
/// </summary>
internal static class CliCommands
{
    private const string Migrate = "migrate";
    private const string BootstrapAdmin = "bootstrap-admin";
    private const string PreviewInitialize = "preview-initialize";
    private const string ModulesInstall = "modules-install";
    private static readonly JsonSerializerOptions PreviewJson = new() { WriteIndented = true };

    internal static bool IsCommand(string value) =>
        string.Equals(value, Migrate, StringComparison.Ordinal) || string.Equals(value, BootstrapAdmin, StringComparison.Ordinal)
        || string.Equals(value, PreviewInitialize, StringComparison.Ordinal) || string.Equals(value, ModulesInstall, StringComparison.Ordinal);

    internal static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        CliArguments options;
        try
        {
            options = CliArguments.Parse(args.Skip(1).ToArray());
        }
        catch (RequestValidationException exception)
        {
            await Console.Error.WriteLineAsync($"Rejected ({exception.Code}): {exception.Message}").ConfigureAwait(false);
            return 2;
        }

        var environment = options.Optional("--environment")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production;
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            EnvironmentName = environment,
            ApplicationName = typeof(CliCommands).Assembly.GetName().Name,
        });
        builder.Services.AddPetraCore();
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        try
        {
            return args[0] switch
            {
                Migrate => await RunMigrateAsync(scope.ServiceProvider, options).ConfigureAwait(false),
                PreviewInitialize => await RunPreviewInitializeAsync(scope.ServiceProvider, options, environment).ConfigureAwait(false),
                ModulesInstall => await RunModulesInstallAsync(scope.ServiceProvider, options).ConfigureAwait(false),
                _ => await RunBootstrapAsync(scope.ServiceProvider, options).ConfigureAwait(false),
            };
        }
        catch (RequestValidationException exception)
        {
            await Console.Error.WriteLineAsync($"Rejected ({exception.Code}): {exception.Message}").ConfigureAwait(false);
            return 2;
        }
        catch (BusinessRuleException exception)
        {
            await Console.Error.WriteLineAsync($"Rejected ({exception.Code}): {exception.Message}").ConfigureAwait(false);
            return 3;
        }
        catch (ResourceConflictException exception)
        {
            await Console.Error.WriteLineAsync($"Rejected ({exception.Code}): {exception.Message}").ConfigureAwait(false);
            return 3;
        }
    }

    /// <summary>
    /// Installs a verified module package. It stays a server-side action: it needs the host's files and its trusted-publisher
    /// list, and it only installs - an operator enables the module afterwards.
    /// </summary>
    private static async Task<int> RunModulesInstallAsync(IServiceProvider services, CliArguments options)
    {
        var package = options.Require("--package");
        var configured = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PetraERP.Infrastructure.Modules.ModuleHostOptions>>().Value;
        var trusted = new HashSet<string>(configured.TrustedPublishers, StringComparer.Ordinal);
        if (trusted.Count == 0)
        {
            await Console.Error.WriteLineAsync("Rejected (modules.no_trusted_publishers): set Modules:TrustedPublishers before installing a package.").ConfigureAwait(false);
            return 3;
        }

        var manager = services.GetRequiredService<PetraERP.Modules.Host.ModuleManager>();
        var result = await manager.InstallAsync(package, PetraERP.Api.Endpoints.PlatformEndpoints.ProductVersion, trusted, CancellationToken.None).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(result.Message).ConfigureAwait(false);
        foreach (var issue in result.Issues) await Console.Out.WriteLineAsync($"  {issue.Failure}: {issue.Detail}").ConfigureAwait(false);
        return result.Success ? 0 : 3;
    }

    private static async Task<int> RunPreviewInitializeAsync(IServiceProvider services, CliArguments options, string environment)
    {
        if (!string.Equals(environment, Environments.Development, StringComparison.Ordinal))
            throw new RequestValidationException("preview.environment_required", "Local preview initialization is available only in Development.", "environment");
        if (!Guid.TryParse(options.Require("--installation-id"), out var installationId) || installationId == Guid.Empty)
            throw new RequestValidationException("preview.installation_invalid", "A valid preview installation ID is required.", "installationId");
        if (!options.Has("--password-stdin"))
            throw new RequestValidationException("preview.password_stdin_required", "Supply the protected local preview password through standard input.", "password");
        var password = (await Console.In.ReadLineAsync().ConfigureAwait(false) ?? string.Empty).TrimStart('\uFEFF');
        var localDate = DateOnly.FromDateTime(DateTime.Today);
        if (options.Optional("--date") is { } date && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out localDate))
            throw new RequestValidationException("preview.date_invalid", "The local preview date must be yyyy-MM-dd.", "date");
        var initializer = new SqlLocalPreviewInitializer(services.GetRequiredService<SystemDbContext>(),
            services.GetRequiredService<ICompanyProvisioningService>(), services.GetRequiredService<IFiscalDatabaseResolver>(),
            services.GetRequiredService<IAccountingDbContextFactory>(), services.GetRequiredService<IAdministratorConfirmation>());
        var result = await initializer.InitializeAsync(installationId, options.Require("--username"), password, localDate).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(result, PreviewJson);
        var resultPath = Path.GetFullPath(options.Require("--result-file"));
        await File.WriteAllTextAsync(resultPath, json, new UTF8Encoding(false)).ConfigureAwait(false);
        Console.WriteLine($"Independent preview workspace READY: {result.CompanyCode}, FY{result.YearNumber}, {result.BranchCode}, {result.WorkspaceId}.");
        return 0;
    }

    private static async Task<int> RunMigrateAsync(IServiceProvider services, CliArguments options)
    {
        var context = services.GetRequiredService<PetraDbContext>();
        var pending = (await context.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
        if (pending.Count == 0)
        {
            Console.WriteLine("The database schema is up to date. No migration was applied.");
            return 0;
        }

        Console.WriteLine("Pending reviewed migrations:");
        foreach (var migration in pending)
        {
            Console.WriteLine("  " + migration);
        }

        if (!options.Has("--confirm"))
        {
            Console.WriteLine("Nothing was changed. Take a verified backup, then re-run with --confirm to apply.");
            return 1;
        }

        await context.Database.MigrateAsync().ConfigureAwait(false);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Applied {pending.Count} migration(s)."));
        return 0;
    }

    private static async Task<int> RunBootstrapAsync(IServiceProvider services, CliArguments options)
    {
        var bootstrapper = services.GetRequiredService<ISecurityBootstrapper>();
        if (await bootstrapper.IsCompletedAsync(CancellationToken.None).ConfigureAwait(false))
        {
            await Console.Error.WriteLineAsync("Bootstrap is disabled: this installation already has users.").ConfigureAwait(false);
            return 3;
        }

        var userName = options.Require("--username");
        var displayArabic = options.Require("--display-name-ar");
        var displayEnglish = options.Optional("--display-name-en");
        BootstrapOrganization? organization = null;
        if (options.Optional("--company-code") is { } companyCode)
        {
            organization = new BootstrapOrganization(
                companyCode,
                options.Require("--company-name-ar"),
                options.Optional("--company-name-en"),
                options.Require("--currency"),
                options.Require("--currency-name-ar"),
                options.Require("--branch-code"),
                options.Require("--branch-name-ar"),
                options.Optional("--branch-name-en"));
        }

        var password = options.Has("--password-stdin")
            // Some Windows hosts prepend a UTF-8 BOM to piped text; it can never be part of the password.
            ? (await Console.In.ReadLineAsync().ConfigureAwait(false) ?? string.Empty).TrimStart('\uFEFF')
            : ConsolePassword.ReadConfirmed();
        var result = await bootstrapper.BootstrapAsync(
            new BootstrapAdministratorCommand(userName, displayArabic, displayEnglish, password, organization),
            CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Administrator created ({result.AdministratorUserId}). Scoped to {result.CompanyScopeCount} company(ies) and {result.BranchScopeCount} branch(es). Bootstrap is now permanently disabled."));
        return 0;
    }
}

/// <summary>Minimal <c>--name value</c> argument reader. Passwords are never accepted as arguments.</summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);

    private CliArguments()
    {
    }

    internal static CliArguments Parse(string[] args)
    {
        var parsed = new CliArguments();
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new RequestValidationException("cli.argument_invalid", $"Unexpected argument '{name}'.", name);
            }

            if (string.Equals(name, "--password", StringComparison.Ordinal))
            {
                throw new RequestValidationException("cli.password_argument_forbidden",
                    "Passwords are never accepted on the command line. Omit it to be prompted, or use --password-stdin.", name);
            }

            string? value = null;
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }

            parsed.values[name] = value;
        }

        return parsed;
    }

    internal bool Has(string name) => values.ContainsKey(name);

    internal string? Optional(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    internal string Require(string name) => Optional(name)
        ?? throw new RequestValidationException("cli.argument_missing", $"The argument {name} is required.", name);
}

internal static class ConsolePassword
{
    internal static string ReadConfirmed()
    {
        var first = Read("Administrator password: ");
        var second = Read("Repeat password: ");
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            throw new RequestValidationException("cli.password_mismatch", "The passwords do not match.", "password");
        }

        return first;
    }

    private static string Read(string prompt)
    {
        Console.Write(prompt);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
