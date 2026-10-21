# MachineSessionManager

A small Windows-only .NET console application for checking a named Windows user's sessions on an explicit list of machines, optionally logging off **remote** sessions, and sending a consolidated email report.

## Open in Visual Studio

1. Extract the ZIP.
2. Open **MachineSessionManager.sln** in Visual Studio with a .NET 8 SDK installed (and a Visual Studio version supporting .NET 8).
3. Edit `MachineSessionManager/appsettings.json`: enter the actual username and machine names.
4. Explicitly set `Session.ModeType` to `Check` and `Notification.NotificationModeType` to `Never` for the first run. Select the **Configured job** debug profile and press F5, or run from a terminal. The profile does not override your settings.
5. Review the console output before enabling sign-out.

NuGet restore downloads `Microsoft.Extensions.Hosting`, `MailKit`, and `Scriban` on first build. The ZIP contains source code, not prebuilt executables or NuGet packages.

## Run

From the project folder:

```powershell
dotnet run
```

This uses the explicitly configured modes in `appsettings.json`. To override them for a particular invocation:

```powershell
dotnet run -- --Session:ModeType=Check --Notification:NotificationModeType=Never
```

With your notification addresses and SMTP settings configured:

```powershell
dotnet run -- --Session:ModeType=Check --Notification:NotificationModeType=Relevant
```

**Caution — logs off users and can discard unsaved work:**

```powershell
dotnet run -- --Session:ModeType=SignOut
```

For domain users, prefer `DOMAIN\username` (as JSON: `"DOMAIN\\username"`) to avoid collisions between domains. When a bare username is configured, it matches any domain with that username.

## appsettings.json

- `Session.ModeType`: **required**, `Check` or `SignOut`; there is no code default
- `Session.UserName`: Windows user, preferably domain-qualified
- `Session.Machines`: specific machine names (no network-wide discovery)
- `Session.MachineTimeout`: **required**, a positive duration such as `"00:01:00"` (one minute). Limits how long the job waits for each machine, including discovery, all session checks, and sign-out verification. There is no code default.
- `Notification.NotificationModeType`: **required**, `Never`, `Relevant`, or `Always`; there is no code default
  - `Never`: skip all emails
  - `Relevant`: email when Check finds a matching signed-in session, or SignOut finds a matching session to act on, or either mode reports any error/skipped/unconfirmed operation. Skip email only when every machine reports NotSignedIn.
  - `Always`: email every completed check run, including runs with no sessions and runs containing errors
- `Notification.From`: sender address for the report
- `Notification.To`, `Notification.Cc`, `Notification.Bcc`: arrays of recipient addresses
- `Email.Host`: SMTP host used to deliver messages
- `Email.Port`: `null` (MailKit default), or an explicit port
- `Email.UseSsl`: `true` means **required STARTTLS**, typically port 587; `false` means no TLS (only for a trusted unauthenticated internal SMTP relay)

Email delivery uses an unauthenticated SMTP relay.

Email will not be attempted when `Notification.NotificationModeType = Never`, so you can test Check mode without a functional SMTP relay.

Configuration is split by responsibility: `Session` controls the Windows operation, `Notification` controls policy and addresses, and `Email` controls SMTP transport. `Notification:NotificationModeType` replaces the old `Email:When` key; move `From`, `To`, `Cc`, and `Bcc` into `Notification` when migrating an existing configuration. The old key is not a fallback. Existing configurations must also rename `Session:Mode` to `Session:ModeType` and `Notification:NotificationMode` to `Notification:NotificationModeType`; these settings remain required.

```json
{
  "Notification": {
    "NotificationModeType": "Relevant",
    "From": "notifications@company.com",
    "To": [
      "userx@company.com"
    ],
    "Cc": [],
    "Bcc": []
  },
  "Email": {
    "Host": "smtp.company.com",
    "Port": 587,
    "UseSsl": true
  }
}
```

The sample `appsettings.json` explicitly sets `ModeType` to `Check` and `NotificationModeType` to `Never`. These are sample choices, not fallbacks: removing either value stops startup with exit code 2. Named values are case-insensitive; numeric enum values are rejected. Command-line arguments may explicitly override the configured values for separate scheduled tasks.

`Check` reports the configured account's sessions. `SignOut` ends all matching RDP sessions, including active and disconnected sessions. It closes their applications; it does not merely disconnect the RDP connection. Console and other non-RDP sessions are skipped. Machine names are trimmed and deduplicated case-insensitively.

## Publish and schedule

From `MachineSessionManager`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Apps\MachineSessionManager
```

Use Windows Task Scheduler with a suitable authorized account. Task action:

- Program: `C:\Apps\MachineSessionManager\MachineSessionManager.exe`
- Check arguments: `--Session:ModeType=Check`
- Sign-out arguments: `--Session:ModeType=SignOut`
- Start in: `C:\Apps\MachineSessionManager`

Using separate tasks allows independently choosing days and times without running a background service. Prevent overlapping runs of the same scheduled task.

The runtime must be installed on the scheduled machine for a framework-dependent publish. An alternative is `--self-contained true`, which produces a larger deployment.

## Results and exit codes

- `0`: normal completion without session issues
- `1`: completed with an issue such as AccessDenied, Unreachable, Skipped, NotConfirmed, TimedOut, or Error
- `2`: fatal job/configuration/email failure, or user cancellation

Each matching session and each failed session query gets its own result. A machine that cannot be opened or enumerated gets a machine-level error, and processing continues to the next machine. Successful results are retained when another query fails. A machine with unreadable sessions is not incorrectly reported as NotSignedIn.

Machines run concurrently. Console results, plain-text reports, and HTML reports stay grouped in `Session.Machines` configuration order, regardless of which machine finishes first. Within a machine, session results retain the order returned by Windows.

When a machine exceeds `Session.MachineTimeout`, completed session results are retained and the unfinished check is reported as `TimedOut`. If sign-out was in progress, it is conservatively reported as `NotConfirmed`, because Windows may already have received the request. Both outcomes are issues (exit code 1) and trigger `Relevant` email. Any remaining sessions on that machine are not checked. Late results cannot change the completed report, and timed-out operations are not retried within that run.

The timeout stops waiting and requests cancellation; it cannot interrupt a synchronous WTS call already inside Windows. Each worker owns its connections until it returns. Cancellation checks prevent further work, including additional logoff requests, once control returns to the application. A logoff request already received by Windows may still finish. Background tasks do not keep the console process alive after the job exits; each scheduled invocation has fresh execution state. Configure Task Scheduler to prevent overlapping runs.

Press Ctrl+C to cancel the job. The application stops waiting for native work, requests cancellation of every machine, and exits with code 2. An interrupted run does not send a consolidated email. Task Scheduler forcibly terminating the process can bypass application cleanup.

Session outcomes are logged to console. Retain console output with a task wrapper/redirection, or add a file logging provider for historical results. Task Scheduler history records task execution events; it does not retain this application's console report. If the app cannot load its configuration, never starts, or cannot send email, it cannot reliably email a report: use Task Scheduler history/monitoring as a fallback. `Always` and `Relevant` include machine, session-query, and logoff errors in the consolidated report.

## Windows permissions / limitations

- Run on **Windows** with network access and the Windows Remote Desktop Services management permissions needed to query and log off sessions on the configured computers. Windows or corporate firewall/policy may block remote session APIs. The program does not bypass such restrictions.
- `SignOut` is **not** a graceful warning: it forcibly ends eligible sessions; unsaved work may be lost.
- Console sessions are intentionally skipped; the application is intended mainly for Remote Desktop sessions. It does not guarantee forced sign-out of a physical interactive desktop.
- It treats unreachable machines and permission errors separately from NotSignedIn, with original Windows error details retained.
- Before logoff, the exact account (including domain) is rechecked to reduce the risk of acting on a reused session ID. Windows' `WTSClientProtocolType` identifies RDP sessions; station display names are not used as the protocol test.
- After logoff, the program checks the target session up to 3 times, 1.5 seconds apart. It confirms that the original account no longer occupies that session. Unrelated session errors cannot invalidate this verification. If verification fails, it reports NotConfirmed rather than Success. The verification interval is currently fixed.
- `WTSOpenServerW`, `WTSEnumerateSessionsW`, `WTSQuerySessionInformationW`, and `WTSLogoffSession` are called directly with P/Invoke. Windows-only test on actual machines is required before enabling SignOut.
- Using a domain-qualified username is recommended; `UserName` must be exactly the Windows account name, not email/UPN unless Windows reports it in that form.

## Structure and conventions

- `Program.cs`: configuration, DI registration, starting the job, and fatal startup handling
- `ConsoleCancellation.cs`: Ctrl+C handling and safe cancellation-source cleanup
- `Configuration/SettingsLoader.cs`: explicit configuration parsing into immutable settings
- `Configuration/ConfigurationKeys.cs`: shared configuration keys and diagnostic paths
- `SessionJob.cs`: run the session batch, log results, invoke notification, and choose the process exit code
- `Native/WtsServer.cs`: Windows API calls and unmanaged memory ownership
- `Native/IWtsServer.cs`: injectable Windows boundary; tests use simulated sessions
- `Native/IWtsServerFactory.cs`: creates an open connection owned by its calling method
- `Native/WtsServerFactory.cs`: creates the concrete Windows connection; each public interface has its own file
- `Sessions/MachineSessionManager.cs`: concurrent machine checks, timeouts, ordered results, account matching, and per-machine/per-session error isolation
- `Sessions/MachineSessionProgress.cs`: synchronized per-machine progress and immutable result snapshots when a wait ends
- `Sessions/SessionSignOut.cs`: eligibility/ownership checks, a single logoff request, and verification
- `Sessions/SessionResults.cs`: native-error classification and structured result creation
- `Email/EmailSender.cs`: MailKit delivery with constructor-injected SMTP transport settings and an addressed `EmailMessage`
- `Email/EmailNotification.cs`: notification decisions, message addressing, and report/delivery coordination
- `Reporting/ReportBuilder.cs`: email subject, plain text, and HTML-encoded Scriban model
- `Reporting/SessionReportModel.cs` and `SessionReportRow.cs`: the named, immutable contract consumed by the HTML template
- `Templates/SessionReport.html`: Scriban HTML email with table layout, inline styles, and Outlook-specific width/DPI handling
- `MachineSessionManager.Tests/`: behavior tests that do not contact machines or SMTP servers

Settings, messages, results, and enums live directly in their owning `Email/` or `Sessions/` folder. Reporting display models live in `Reporting/`. Model properties are get-only, and collection inputs are copied into read-only collections.

`CreateLastWindowsException` (previously `LastError`) creates a `Win32Exception` from the Windows error code preserved by P/Invoke. `throw CreateLastWindowsException(...)` then throws that returned exception. It is an ordinary helper method, not special C# syntax. The native wrapper includes comments on the WTS calls, buffers, and protocol values. The native interop structure uses fields to match Windows' memory layout; application models use get-only properties.

Project enum types use the `Type` suffix, for example `SessionModeType`, `NotificationModeType`, and `SessionStatusType`. Enum values retain their existing names. Enum-valued settings also use the `Type` suffix: `Session.ModeType` and `Notification.NotificationModeType`.

Public properties appear before constructors; their position is a project convention, not a C# requirement. Properties remain get-only. Local variables use `var` where the initializer can provide the type; explicit types remain for null/collection-expression initializers that need a target type. Control-flow statements use braces, including single-line bodies; `.editorconfig` records the typing and brace conventions. Requested cancellation uses `catch (OperationCanceledException) when (...)` filters. Cancellation checks remain at operation boundaries and around blocking Windows calls. Settings are injected directly and do not use mutable options objects or runtime service lookups in the job logic.

The Scriban model uses named classes so its fields are visible and discoverable without inspecting an anonymous projection. It is a display model: `ReportBuilder` HTML-encodes its text before rendering; the plain-text report uses the original values.

Each class owns one cohesive part of the workflow:

| Class | Responsibility |
| --- | --- |
| `Program` | Startup, configuration loading, dependency registration, and fatal startup handling |
| `SettingsLoader` | Parse required settings into immutable configuration objects |
| `SessionJob` | Coordinate the run, log results, and return an exit code |
| `MachineSessionManager` | Inspect the configured machines/accounts and isolate failures |
| `SessionSignOut` | Open its connection, safely end one eligible session, verify the outcome, and close its connection |
| `SessionResults` | Translate session observations and failures into result objects |
| `WtsServer` | Call Windows and manage native handles/buffers |
| `EmailNotification` | Decide whether to notify, build an addressed message, and request delivery |
| `ReportBuilder` | Build the report's subject, plain-text body, and Scriban HTML body |
| `SessionReportModel` / `SessionReportRow` | Describe the template's immutable display data |
| `EmailSender` | Validate delivery settings, construct MIME, and transmit over SMTP |

`EmailSender` never interprets `NotificationModeType`. `EmailNotification` never opens SMTP connections. `EmailSender` receives `EmailSettings` containing only SMTP transport configuration. `EmailNotification` receives separate `NotificationSettings` for policy and addressing, and passes a fully addressed `EmailMessage` to the sender. SMTP connection security and MIME formatting are delivery concerns, while eligibility checks and verification are part of a single safe sign-out operation.

Connections are owned by the method performing the operation. Each machine worker opens and disposes its own discovery connection. `SessionSignOut.ExecuteAsync` opens a separate connection through its constructor-injected `IWtsServerFactory` and disposes it after sign-out/verification, including early returns, failures, and cancellation. A timeout never disposes a connection while a native call is still using it. Open connections are not passed between these classes. The factory allows tests to create simulated connections.

`WtsServer.cs` contains a step-by-step explanation of handles, native arrays, UTF-16 strings, cleanup, `DllImport`, and numeric selectors. The private nested enum is a C# visibility choice: only the wrapper needs Windows' query-selector definitions. The values 5, 7, and 16 match [Windows' WTS_INFO_CLASS declaration](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ne-wtsapi32-wts_info_class); they cannot be renumbered. [`DllImport`/P/Invoke](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke) connects the declarations to functions supplied by the Windows DLL.

The email uses tables and inline styles to accommodate [classic Outlook's HTML limitations](https://learn.microsoft.com/en-us/dynamics365/customer-insights/journeys/email-troubleshoot-rendering). Actual rendering still needs a test delivery to the Outlook versions you use.

## Tests

With the .NET 8 SDK:

```powershell
dotnet test MachineSessionManager.sln
```

Tests cover failures between successful queries, concurrent machines finishing out of order, ordered console/report inputs, timeout preservation of partial results, cancellation around native calls, late work not starting further sign-outs, independent subsequent runs, notification policies, required settings, read-only collections, account matching/reused IDs, RDP eligibility, logoff verification, email delivery failures, and HTML encoding/template rendering. They use fake Windows connections and an email sender; they never sign out real users or send messages. Real Windows API, SMTP, and Outlook integration tests are separate deployment checks.

## Security

Only operate on machines/accounts you are authorized to manage. Restrict the Task Scheduler run-as account to the required permissions. Keep `appsettings.json` access controlled if it lists internal hostnames or email addresses. The sample hostnames and addresses are placeholders.
