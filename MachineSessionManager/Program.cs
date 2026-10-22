using MachineSessionManager.Configuration;
using MachineSessionManager.Email;
using MachineSessionManager.Native;
using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var bootstrapLogger = new LoggerConfiguration()
            .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Warning)
            .CreateLogger();
        Log.Logger = bootstrapLogger;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Log.Fatal("MachineSessionManager requires Windows.");
                return 2;
            }

            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    Args = args,
                    ContentRootPath = AppContext.BaseDirectory
                });

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            builder.Logging.ClearProviders();
            // Main owns disposal so failures can still be logged after host cleanup.
            builder.Services.AddSerilog(Log.Logger, dispose: false);

            var session = SettingsLoader.LoadSession(builder.Configuration);
            var notification = SettingsLoader.LoadNotification(builder.Configuration);
            var email = SettingsLoader.LoadEmail(builder.Configuration);
            builder.Services.AddSingleton(session);
            builder.Services.AddSingleton(notification);
            builder.Services.AddSingleton(email);
            builder.Services.AddSingleton<IWtsServerFactory, WtsServerFactory>();
            builder.Services.AddSingleton<SessionManager>();
            builder.Services.AddSingleton<SessionSignOut>();
            builder.Services.AddSingleton<ReportBuilder>();
            builder.Services.AddSingleton<IEmailSender, EmailSender>();
            builder.Services.AddSingleton<EmailNotification>();
            builder.Services.AddSingleton<SessionJob>();

            using var host = builder.Build();
            using var cancellation = new ConsoleCancellation();
            try
            {
                return await host.Services.GetRequiredService<SessionJob>().RunAsync(cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
            {
                Log.Warning("Machine session job cancelled. Any submitted logoff may still complete.");
                return 2;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Machine session job failed");
            return 2;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
