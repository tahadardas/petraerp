using PetraERP.Api.Cli;

namespace PetraERP.Api.Hosting;

/// <summary>
/// Entry point. With no command the HTTP API runs. Installation commands run without opening any listener:
/// <c>migrate</c> applies reviewed EF migrations, <c>bootstrap-admin</c> creates the first administrator once.
/// </summary>
internal static class ApiProgram
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && CliCommands.IsCommand(args[0]))
        {
            return await CliCommands.RunAsync(args).ConfigureAwait(false);
        }

        var app = BuildWebApplication(args);
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    internal static WebApplication BuildWebApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddPetraServices();
        var app = builder.Build();
        app.UsePetraPipeline();
        return app;
    }
}
