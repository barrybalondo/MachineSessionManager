# MachineSessionManager

A .NET 8 console job that checks a user's sessions across Windows machines. It can also sign out matching RDP sessions and email a report.

Remote access uses a separate Windows account. Its password is fetched from Idira at the start of each run.

## Quick start

Requires Windows and the .NET 8 SDK. Replace the sample values in [appsettings.json](MachineSessionManager/appsettings.json) with your machines, accounts, and CCP settings. From the repository root:

```powershell
dotnet build MachineSessionManager.sln
dotnet run --project MachineSessionManager -- --Session:ModeType=Check --Notification:NotificationModeType=Never
```

To sign out matching RDP sessions:

```powershell
dotnet run --project MachineSessionManager -- --Session:ModeType=SignOut
```

Pass a setting on the command line to override it for that run. In Visual Studio, open `MachineSessionManager.sln` and run the project with the settings in `appsettings.json`.

## Configuration

| Setting | Required | Description |
| --- | --- | --- |
| `Session:ModeType` | Yes | `Check` lists matching sessions. `SignOut` signs out matching RDP sessions. |
| `Session:UserName` | Yes | User to check, preferably `DOMAIN\user`. |
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
| `Email:Host` | When sending email | SMTP relay hostname. |
| `Email:Port` | No | SMTP port. Leave as `null` to use MailKit's defaults. |
| `Email:UseSsl` | No | `true` requires STARTTLS, usually on port 587. Defaults to `false` (no TLS). |

`Session:UserName` is the user being checked. `Impersonation:UserName` is the account doing the work. In JSON, write a domain-qualified name as `"DOMAIN\\user"`. A target username without a domain matches that username in any domain.

Both mode settings are required. Mode values ignore case. Email uses an SMTP relay without authentication. If TLS is off, use a trusted internal relay.

## Credentials and access

The job calls the [CCP GET API](https://docs.cyberark.com/credential-providers/latest/en/content/ccp/calling-the-web-service-using-rest.htm) once per run, using AppID, Safe, and the impersonation username. Those values must resolve to one Vault account. The password comes from `Content` and is used for outbound Windows credentials (`LOGON32_LOGON_NEW_CREDENTIALS`). Local file access, reporting, and email use the account that started the job.

CCP lookup or Windows token creation failures stop the job. The remote machine validates the password when the job connects. Remote access does not fall back to the scheduled task account. CCP errors include the HTTP status and the provider's error text. Keep passwords out of configuration; successful credential responses are not logged.

The host and accounts need:

- Existing CCP authorization for the host and AppID, access to retrieve from the Safe, and a reachable HTTPS endpoint with a trusted certificate.
- The remote machines must be able to authenticate the configured account.
- RDS **Query Information** permission for the impersonation account on target machines for checks, and **Reset** permission for sign-outs. Firewalls must allow the remote connections.

The scheduled task account needs access to the app, configuration, CCP, and SMTP relay. The impersonation account needs the remote session permissions.

## Session behavior

`Check` lists matching sessions. `SignOut` signs out matching active or disconnected RDP sessions. The job checks session ownership again before sign-out, then verifies that the session ended. Sign-out skips console and other non-RDP sessions. Signing out closes applications and can lose unsaved work.

Machines are checked in parallel. Results follow the order in `Session:Machines`. Access denied, unreachable machines, and failures to read a session are reported as issues.

When a machine times out, completed results are kept. Unfinished work is marked `TimedOut`, or `NotConfirmed` if a sign-out was in progress. A timeout or Ctrl+C cannot interrupt a Windows call already running or undo a sign-out already submitted.

`Relevant` emails include matching sessions, errors, skipped operations, timeouts, and unconfirmed sign-outs. Startup, credential, or impersonation failures, missing Windows DLLs, and cancelled runs do not send a report; check the console output for the error. If sending email fails, the job returns exit code `2` even if the session work has already finished.

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
