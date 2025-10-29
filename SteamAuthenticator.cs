using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;

namespace SteamIconRestorer;

public class SteamAuthenticator
{
    private readonly string? _username;
    private readonly string? _password;
    private readonly bool _useQrCode;
    private readonly string _steamPath;
    private readonly SteamClient _steamClient;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private bool _isRunning;
    private string? _previouslyStoredGuardData;
    private readonly TaskCompletionSource<bool> _loginCompletionSource;

    public SteamAuthenticator(string? username, string? password, bool useQrCode, string steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath))
            throw new ArgumentException("Steam path cannot be null or empty", nameof(steamPath));

        _username = username;
        _password = password;
        _useQrCode = useQrCode;
        _steamPath = steamPath;

        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()
                     ?? throw new InvalidOperationException("Failed to get SteamUser handler");

        _loginCompletionSource = new TaskCompletionSource<bool>();

        // Register callbacks
        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
    }

    public async Task RunAsync()
    {
        try
        {
            Console.WriteLine("Connecting to Steam...");
            _isRunning = true;

            // Initiate the connection to Steam
            _steamClient.Connect();

            // Run callback loop in background
            var callbackTask = Task.Run(() =>
            {
                while (_isRunning)
                {
                    _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }
            });

            // Wait for login to complete
            var loginSuccessful = await _loginCompletionSource.Task;

            if (!loginSuccessful)
            {
                Console.Error.WriteLine("Login failed. Exiting...");
                _isRunning = false;
                await callbackTask;
                return;
            }

            // Perform icon restoration
            await PerformIconRestorationAsync();

            // Cleanup
            Console.WriteLine("Logging off from Steam...");
            _steamUser.LogOff();

            // Wait a bit for logoff to complete
            await Task.Delay(2000);
            _isRunning = false;
            await callbackTask;

            Console.WriteLine("Done!");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}");
            _isRunning = false;
            throw;
        }
    }

    private async Task PerformIconRestorationAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Starting icon restoration process...");

        try
        {
            var iconRestorer = new SteamIconRestorer(_steamPath, _steamClient, _callbackManager);
            await iconRestorer.RestoreIconsAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Icon restoration failed: {ex.Message}");
            throw;
        }
    }

    private async void OnConnected(SteamClient.ConnectedCallback callback)
    {
        Console.WriteLine("Connected to Steam!");

        try
        {
            if (_useQrCode)
            {
                await AuthenticateWithQrCodeAsync();
            }
            else
            {
                await AuthenticateWithCredentialsAsync();
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Authentication error: {ex.Message}");
            _isRunning = false;
            _loginCompletionSource.TrySetResult(false);
        }
    }

    private async Task AuthenticateWithCredentialsAsync()
    {
        if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
        {
            throw new InvalidOperationException("Username and password are required for credentials authentication");
        }

        Console.WriteLine($"Logging in as '{_username}'...");

        var authSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
            new AuthSessionDetails
            {
                Username = _username,
                Password = _password,
                IsPersistentSession = false,
                GuardData = _previouslyStoredGuardData,
                Authenticator = new UserConsoleAuthenticator()
            });

        var pollResult = await authSession.PollingWaitForResultAsync();

        if (pollResult?.NewGuardData != null)
        {
            _previouslyStoredGuardData = pollResult.NewGuardData;
            // TODO: Consider persisting guard data to file for future use
        }

        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = pollResult!.AccountName,
            AccessToken = pollResult.RefreshToken,
            ShouldRememberPassword = false
        });
    }

    private async Task AuthenticateWithQrCodeAsync()
    {
        Console.WriteLine("Starting QR Code authentication...");

        var qrAuthSession = await _steamClient.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails());

        // Handle QR code refresh
        qrAuthSession.ChallengeURLChanged = () =>
        {
            Console.WriteLine();
            Console.WriteLine("Steam has refreshed the challenge URL");
            DrawQrCode(qrAuthSession);
        };

        DrawQrCode(qrAuthSession);

        // Wait for authentication
        var pollResponse = await qrAuthSession.PollingWaitForResultAsync();

        Console.WriteLine($"Authenticated as '{pollResponse.AccountName}'");

        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = pollResponse.AccountName,
            AccessToken = pollResponse.RefreshToken,
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        Console.WriteLine($"Disconnected from Steam. User initiated: {callback.UserInitiated}");

        if (!callback.UserInitiated && _isRunning)
        {
            Console.WriteLine("Unexpected disconnection occurred.");
        }

        _isRunning = false;
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            Console.Error.WriteLine($"Failed to log on to Steam: {callback.Result}");

            if (callback.ExtendedResult != EResult.OK)
            {
                Console.Error.WriteLine($"Extended result: {callback.ExtendedResult}");
            }

            _isRunning = false;
            _loginCompletionSource.TrySetResult(false);
            return;
        }

        Console.WriteLine($"Successfully logged on to Steam!");
        Console.WriteLine($"SteamID: {callback.ClientSteamID}");

        _loginCompletionSource.TrySetResult(true);
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
    {
        Console.WriteLine($"Logged off from Steam: {callback.Result}");
    }

    private static void DrawQrCode(QrAuthSession qrAuthSession)
    {
        Console.WriteLine($"Challenge URL: {qrAuthSession.ChallengeURL}");
        Console.WriteLine();

        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(qrAuthSession.ChallengeURL, QRCodeGenerator.ECCLevel.L);
            using var qrCode = new AsciiQRCode(qrCodeData);
            var qrCodeAsAscii = qrCode.GetGraphic(1, drawQuietZones: false);

            Console.WriteLine("Use the Steam Mobile App to sign in via QR code:");
            Console.WriteLine(qrCodeAsAscii);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to generate QR code: {ex.Message}");
            Console.WriteLine($"Please visit: {qrAuthSession.ChallengeURL}");
        }
    }
}