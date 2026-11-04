# MachineSessionManager

Windows-only .NET 8 console job that checks a target user's sessions on configured machines, optionally signs out matching RDP sessions, and emails a report.

## Run

Install the .NET 8 SDK on Windows and edit `MachineSessionManager/appsettings.json`. You can also open `MachineSessionManager.sln` in Visual Studio.

From the repository root, start in Check mode with email disabled:

```powershell
dotnet run --project MachineSessionManager -- --Session:ModeType=Check --Notification:NotificationModeType=Never
```

To sign out matching active or disconnected RDP sessions:

```powershell
dotnet run --project MachineSessionManager -- --Session:ModeType=SignOut
```

Sign-out closes applications and can discard unsaved work. Console and other non-RDP sessions are skipped.

## Configuration

| Setting | Purpose |
| --- | --- |
| `Session:ModeType` | Required: `Check` or `SignOut`. |
| `Session:UserName` | Target user to check, preferably domain-qualified. |
| `Session:Machines` | Explicit list of target machines. |
| `Session:MachineTimeout` | Required positive duration, such as `00:01:00`. |
| `Idira:BaseUrl` | HTTPS CCP base URL without a trailing slash. |
| `Idira:AppId`, `Idira:Safe` | Registered application ID and account Safe. |
| `Impersonation:UserName` | Account performing remote operations, in UPN or `DOMAIN\user` format. Must match the Vault account's `UserName` property. |
| `Notification:NotificationModeType` | Required: `Never`, `Relevant` (matching sessions or issues), or `Always`. |
| `Notification:From`, `Notification:To`, `Notification:Cc`, `Notification:Bcc` | Sender address and recipient arrays. |
| `Email:Host`, `Email:Port`, `Email:UseSsl` | SMTP relay settings. A null port uses MailKit defaults; `UseSsl=true` requires STARTTLS, while `false` disables TLS. |

`Session:UserName` is the target user; `Impersonation:UserName` is the separate account authorized to manage sessions. Email uses an unauthenticated SMTP relay and is skipped in `Never` mode.

## Credentials and permissions

The job retrieves the password once per execution through the [CCP GET API](https://docs.cyberark.com/credential-providers/latest/en/content/ccp/calling-the-web-service-using-rest.htm), using AppID, Safe, and the impersonation username. It reads `Content`, validates Windows batch logon, and runs remote operations under that account. Retrieval or logon failure stops the job without falling back to the scheduled task identity. No password belongs in configuration.

Required access:

- Existing CCP machine/application authorization and Safe retrieval permissions. Safe and UserName must identify exactly one account; HTTPS must be reachable and trusted.
- The impersonation account needs **Log on as a batch job** on the job host, with domain authentication available.
- On targets, it needs RDS **Query Information** permission for checks and **Reset** permission for sign-outs, plus the required network/firewall access.

## Publish and schedule

```powershell
dotnet publish MachineSessionManager -c Release -r win-x64 --self-contained false -o C:\Apps\MachineSessionManager
```

Install the .NET 8 runtime on the job host. Configure Task Scheduler:

- Program: `C:\Apps\MachineSessionManager\MachineSessionManager.exe`
- Arguments: `--Session:ModeType=Check` or `--Session:ModeType=SignOut`
- Start in: `C:\Apps\MachineSessionManager`

The task account needs application, CCP, and report-delivery access; remote permissions belong to the impersonation account. Prevent overlapping runs.

## Verify

1. Run Check against a test machine using a task account without remote session permissions. Confirm the impersonation account can perform the check.
2. Confirm the logged execution identity and corroborate it with the service account's network logon in the target's Security log when auditing is enabled.
3. Test SignOut only against disposable RDP sessions and confirm the reported outcome.
4. Deny CCP access or use an invalid service-account credential in a test environment. Confirm exit code 2 and no remote operations.

Exit codes: **0** success, **1** session issues, **2** fatal failure or cancellation. Timeouts and Ctrl+C stop waiting but cannot undo logoff requests already submitted to Windows.

Run unit tests from the repository root:

```powershell
dotnet test MachineSessionManager.sln
```

Tests use fakes; live Windows, CCP, and SMTP verification is separate.
