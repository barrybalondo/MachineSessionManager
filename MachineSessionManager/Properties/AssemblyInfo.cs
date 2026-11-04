using System.Runtime.InteropServices;

// Avoid probing user folders for Windows DLLs while impersonating another account.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
