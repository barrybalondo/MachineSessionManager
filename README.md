# MachineSessionManager

A .NET 8 console job that checks a user's sessions across Windows machines. It can also sign out matching user sessions and email a report.

Remote access uses a separate Windows account. Its password is fetched from Idira at the start of each run.

## Quick start

Requires Windows and the .NET 8 SDK. Replace the sample values in [appsettings.json](MachineSessionManager/appsettings.json) with your machines, accounts, and CCP settings. From the repository root:

```powershell
dotnet build MachineSessionManager.sln
dotnet run --project MachineSessionManager -- --Session:ModeType=Check --Notification:NotificationModeType=Never
```

To sign out matching user sessions:

```powershell
dotnet run --project MachineSessionManager -- --Session:ModeType=SignOut
```

Pass a setting on the command line to override it for that run. In Visual Studio, open `MachineSessionManager.sln` and run the project with the settings in `appsettings.json`.

## Configuration

| Setting | Required | Description |
| --- | --- | --- |
| `Session:ModeType` | Yes | `Check` lists matching sessions. `SignOut` signs out matching user sessions. |
| `Session:UserName` | Yes | User to check. In JSON, use `"DOMAIN\\user"`; omit the domain to match the username in any domain. |
| `Session:Machines` | Yes | Machine names, e.g. `["MACHINE01", "MACHINE02"]`. Names are trimmed and duplicates removed. |
| `Session:MachineTimeout` | Yes | Time allowed per machine, including discovery, session checks, and sign-out verification. Must be positive, e.g. `00:01:00`. |
| `Idira:BaseUrl` | Yes | HTTPS CCP base URL, e.g. `https://ccp.company.com`. Omit the trailing slash. |
| `Idira:AppId` | Yes | Application ID registered with CCP, e.g. `MachineSessionManager`. |
| `Idira:Safe` | Yes | Safe holding the service account, e.g. `ServiceAccounts`. |
| `Impersonation:UserName` | Yes | Windows account used for remote access, as a UPN or `DOMAIN\user`. Must match the Vault account's `UserName`. |
| `Notification:NotificationModeType` | Yes | `Never`: no email. `Relevant`: matching sessions or issues. `Always`: every completed run. |
| `Notification:From` | When sending email | Sender email address. |
| `Notification:To` | When sending email | Array of recipient addresses. At least one is required. |
| `Notification:Cc` | No | Array of CC addresses. Use `[]` for none. |
| `Notification:Bcc` | No | Array of BCC addresses. Use `[]` for none. |
| `Email:Host` | When sending email | SMTP relay hostname. Authentication is not supported. |
| `Email:Port` | No | SMTP port. Leave as `null` to use MailKit's defaults. |
| `Email:UseSsl` | No | `true` requires STARTTLS, usually on port 587. Defaults to `false` (no TLS; use a trusted internal relay). |

## Credentials and access

The job retrieves the password from Idira's [CCP API](https://docs.cyberark.com/credential-providers/latest/en/content/ccp/calling-the-web-service-using-rest.htm) once per run. `AppId`, `Safe`, and `Impersonation:UserName` must identify one Vault account.

Remote operations use the impersonation account. Local files, reporting, and email use the account that starts the job. Credential setup failures stop the run without falling back to that account.

Required access:

- **CCP:** Grant the machine access to retrieve the service account password, and configure the correct `AppId` and `Safe`.
- **Target machines:** Grant the impersonation account RDS **Query Information** permission for checks and **Reset** permission for sign-outs. Allow authentication and remote connections through the firewall.
- **Launching account:** Access to the app, configuration, CCP, and SMTP relay.

Keep passwords out of configuration.

## Session behavior

- `Check` reports matching sessions.
- `SignOut` signs out matching sessions, including disconnected and console sessions. It rechecks ownership and verifies completion. Unsaved work can be lost.
- Machines run in parallel; report results follow the configured order.
- Timeouts preserve completed results. Unfinished work becomes `TimedOut`, or `NotConfirmed` when a sign-out was underway. Submitted Windows calls may finish after timeout or cancellation.
- `Relevant` emails include matching sessions and issues. Fatal errors and cancellation stop the run without a report.

## Deployment

Publish a Windows executable:

```powershell
dotnet publish MachineSessionManager -c Release -r win-x64 --self-contained false -o C:\Apps\MachineSessionManager
```

Install the .NET 8 runtime on the job host, or publish with `--self-contained true` to include it.

Create two Task Scheduler entries using the same executable:

- Program: `C:\Apps\MachineSessionManager\MachineSessionManager.exe`
- Arguments: `--Session:ModeType=Check` or `--Session:ModeType=SignOut`
- Start in: `C:\Apps\MachineSessionManager`

Give Check and SignOut their own schedules. Set each task to skip starting another instance while it is already running.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Finished without session issues. |
| `1` | Finished with an error, skipped operation, timeout, or unconfirmed sign-out. |
| `2` | Configuration, credential, impersonation, missing Windows DLL, or email failure, or the run was cancelled. |
