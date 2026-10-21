using MachineSessionManager.Configuration;
using MachineSessionManager.Email;
using MachineSessionManager.Native;
using MachineSessionManager.Reporting;
using MachineSessionManager.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SessionManager = MachineSessionManager.Sessions.MachineSessionManager;

namespace MachineSessionManager;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("MachineSessionManager requires Windows.");
                return 2;
            }

            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    Args = args,
                    ContentRootPath = AppContext.BaseDirectory
                });

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
                Console.Error.WriteLine("Machine session job cancelled. Any submitted logoff may still complete.");
                return 2;
            }
        }
        catch (Exception ex)
        {
            // Covers malformed JSON and DI/startup failures before a logger exists.
            Console.Error.WriteLine($"Machine session job failed: {ex}");
            return 2;
        }
    }
}
