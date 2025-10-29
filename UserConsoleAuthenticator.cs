using SteamKit2.Authentication;

namespace SteamIconRestorer;

public class UserConsoleAuthenticator : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
    {
        if (previousCodeWasIncorrect)
        {
            Console.Error.WriteLine("The previous code was incorrect.");
        }

        Console.Write("Enter 2FA code from your authenticator app: ");
        var code = Console.ReadLine();
        return Task.FromResult(code?.Trim() ?? string.Empty);
    }

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
    {
        if (previousCodeWasIncorrect)
        {
            Console.Error.WriteLine("The previous code was incorrect.");
        }

        Console.Write($"Enter the code sent to {email}: ");
        var code = Console.ReadLine();
        return Task.FromResult(code?.Trim() ?? string.Empty);
    }

    public Task<bool> AcceptDeviceConfirmationAsync()
    {
        Console.WriteLine("Please confirm this login in your Steam Mobile App...");
        return Task.FromResult(true);
    }
}