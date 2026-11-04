namespace MachineSessionManager.Security;

public interface IWindowsImpersonation
{
    Task<T> RunAsync<T>(string userName, string password, Func<Task<T>> action);
}
