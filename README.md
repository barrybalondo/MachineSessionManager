# MachineSessionManager

A Windows-only .NET 8 console job for checking a user's sessions across configured machines, signing out matching RDP sessions, and sending a consolidated email report. Remote operations use a dedicated Windows account whose password is retrieved from Idira.

## Quick start

Requires Windows and the .NET 8 SDK. Edit [appsettings.json](MachineSessionManager/appsettings.json) with your machine names, accounts, and CCP settings, then run from the repository root:

```powershell
dotnet build MachineSessionManager.sln
dotnet run --project MachineSessionManager -- --Session:ModeType=Check --Notification:NotificationModeType=Never
```

To run sign-out mode:

```powershell
dotnet run --project MachineSessionManager -- --Session:ModeType=SignOut
```

Command-line arguments override the corresponding configuration values for that execution. You can also open `MachineSessionManager.sln` in Visual Studio and run the configured job.

## Configuration

Settings are read from `MachineSessionManager/appsettings.json`. The supplied values are placeholders.

| Setting | Required | Description |
| --- | --- | --- |
| `Session:ModeType` | Yes | `Check` reports matching sessions; `SignOut` ends matching RDP sessions. |
| `Session:UserName` | Yes | Target user as reported by Windows, preferably `DOMAIN\user`. |
| `Session:Machines` | Yes | Array of target machine names, such as `["MACHINE01", "MACHINE02"]`. Names are trimmed and deduplicated. |
| `Session:MachineTimeout` | Yes | Positive duration such as `00:01:00`, covering discovery, session checks, and sign-out verification per machine. |
| `Idira:BaseUrl` | Yes | HTTPS CCP server base URL without a trailing slash, such as `https://ccp.company.com`. |
| `Idira:AppId` | Yes | Application ID registered with the credential provider, such as `MachineSessionManager`. |
| `Idira:Safe` | Yes | Safe containing the service account, such as `ServiceAccounts`. |
| `Impersonation:UserName` | Yes | Account performing remote operations, in UPN or `DOMAIN\user` format. Must match the Vault account's `UserName` property. |
| `Notification:NotificationModeType` | Yes | `Never` disables email; `Relevant` sends for matching sessions or issues; `Always` sends after every completed session batch. |
| `Notification:From` | When sending email | Sender email address. |
| `Notification:To` | When sending email | Array of primary recipient addresses. At least one recipient is required. |
| `Notification:Cc` | No | Array of copy recipients; use `[]` for none. |
| `Notification:Bcc` | No | Array of blind-copy recipients; use `[]` for none. |
| `Email:Host` | When sending email | SMTP relay hostname. |
| `Email:Port` | No | Explicit SMTP port, or `null` to use MailKit defaults. |
| `Email:UseSsl` | No | `true` requires STARTTLS, typically on port 587. `false` disables TLS and is the default. |

`Session:UserName` selects whose sessions are checked; `Impersonation:UserName` is the separate account authorized to manage them. In JSON, write a domain-qualified name as `"DOMAIN\\user"`. A bare target username matches that name in any domain.

Mode names are case-insensitive and must be configured explicitly. Email uses an unauthenticated SMTP relay; use a trusted internal relay when TLS is disabled.

## Credentials and access

The job retrieves the password once per execution through the [CCP GET API](https://docs.cyberark.com/credential-providers/latest/en/content/ccp/calling-the-web-service-using-rest.htm), using AppID, Safe, and the impersonation username. The password is read from `Content`, then used for Windows batch logon and remote WTS operations. Email and reporting run outside impersonation.

The lookup must identify exactly one account. Credential retrieval or Windows logon failure stops remote work without falling back to the scheduled task identity. Provider errors retain their status and error text for debugging. Passwords must not be stored in configuration, and successful credential responses are not logged.

Required permissions:

- The job host and AppID must have existing CCP authorization and Safe retrieval access. The HTTPS endpoint must be reachable and trusted.
- The impersonation account needs **Log on as a batch job** on the job host and must not be denied that right. Domain authentication must be available.
- On target machines, the impersonation account needs RDS **Query Information** permission for checks and **Reset** permission for sign-outs, plus network/firewall access.

The scheduled task account needs application, configuration, CCP, and SMTP access. Remote session permissions belong to the impersonation account.

## Session behavior

Check mode reports matching sessions. SignOut ends matching active or disconnected RDP sessions, rechecks session ownership before logoff, and verifies completion afterward. Console and other non-RDP sessions are skipped during sign-out. Ending a session closes applications and can discard unsaved work.

Machines run concurrently, with results kept in configured machine order. Access-denied, unreachable, and unreadable-session errors are reported as issues rather than as no matching session.

Timeouts preserve completed results and report unfinished work as `TimedOut`, or `NotConfirmed` if sign-out was in progress. Timeouts and Ctrl+C cannot interrupt a Windows call already in progress or undo an already-submitted logoff.

Relevant notifications include errors, skipped operations, timeouts, and unconfirmed sign-outs. Fatal startup, credential, or impersonation failures and cancelled jobs do not send a consolidated session report; errors are written to the console. An email failure returns exit code 2 even if remote operations have already completed.

## Deployment

Publish a Windows executable:

```powershell
dotnet publish MachineSessionManager -c Release -r win-x64 --self-contained false -o C:\Apps\MachineSessionManager
```

This deployment requires the .NET 8 runtime on the job host. Use `--self-contained true` to include the runtime instead.

For Task Scheduler, configure:

- Program: `C:\Apps\MachineSessionManager\MachineSessionManager.exe`
- Arguments: `--Session:ModeType=Check` or `--Session:ModeType=SignOut`
- Start in: `C:\Apps\MachineSessionManager`

Separate Check and SignOut tasks can use different schedules. Prevent overlapping executions of the same job.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Completed without session issues. |
| `1` | Completed with an error, skipped operation, timeout, or unconfirmed sign-out. |
| `2` | Fatal configuration, credential, impersonation, or email failure, or cancellation. |
