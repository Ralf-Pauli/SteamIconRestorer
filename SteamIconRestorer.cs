using System.Diagnostics;

namespace SteamIconRestorer;

using SteamKit2;
using static SteamKit2.SteamApps;

public class SteamIconRestorer
{
    private readonly string _steamPath;
    private readonly string _libraryFoldersPath;
    private readonly List<string> _libraryFolders;

    private readonly SteamClient _steamClient;
    private readonly CallbackManager _callbackManager;

    private const string SteamIconUrlTemplate =
        "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{0}/{1}.ico";

    private record struct GameInfo(uint AppId, string Name, string ManifestPath);


    public SteamIconRestorer(string steamPath, SteamClient steamClient, CallbackManager callbackManager)
    {
        _steamPath = steamPath ?? throw new ArgumentNullException(nameof(steamPath));
        _steamClient = steamClient ?? throw new ArgumentNullException(nameof(steamClient));
        _callbackManager = callbackManager ?? throw new ArgumentNullException(nameof(callbackManager));

        _libraryFoldersPath = Path.Combine(_steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(_libraryFoldersPath))
        {
            throw new FileNotFoundException($"Library folders file not found at: {_libraryFoldersPath}");
        }

        _libraryFolders = GetLibraryFolders();
    }

    private List<string> GetLibraryFolders()
    {
        List<string> libraries = [];
        KeyValue keyValue = new();

        try
        {
            keyValue.ReadFileAsText(_libraryFoldersPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read library folders file: {ex.Message}", ex);
        }

        if (keyValue.Name != "libraryfolders")
        {
            throw new InvalidOperationException("Invalid library folders file format");
        }

        foreach (var libraryFolder in keyValue.Children)
        {
            var path = libraryFolder["path"].Value;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                libraries.Add(path);
            }
        }

        return libraries;
    }


    private List<GameInfo> GetInstalledGames(List<string> libraryFolders)
    {
        List<GameInfo> games = [];

        foreach (var library in libraryFolders)
        {
            string steamAppsPath = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamAppsPath))
                continue;

            // find all appmanifest_*.acf files
            string[] manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");

            foreach (string manifest in manifestFiles)
            {
                try
                {
                    var keyValue = new KeyValue();
                    keyValue.ReadFileAsText(manifest);

                    var appIdKeyValue = keyValue.Children.Find(child => child.Name == "appid");
                    var nameKeyValue = keyValue.Children.Find(child => child.Name == "name");

                    if (appIdKeyValue == null) continue;

                    if (uint.TryParse(appIdKeyValue.Value, out uint appId))
                    {
                        string gameName = nameKeyValue?.Value ?? $"Unknown Game ({appId})";
                        games.Add(new GameInfo(appId, gameName, manifest));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to read manifest file {manifest}: {ex.Message}");
                }
            }
        }

        return games;
    }


    private async Task<string?> GetClientIconAsync(uint appId)
    {
        TaskCompletionSource<string?> tcs = new();
        var steamApps = _steamClient.GetHandler<SteamApps>();

        if (steamApps == null)
        {
            Console.WriteLine("Error: SteamApps handler is not available");
            return null;
        }

        var request = new PICSRequest(appId);
        _ = steamApps.PICSGetProductInfo(request, null);

        // Subscribe to the response
        var subscription = _callbackManager.Subscribe<PICSProductInfoCallback>(callback =>
        {
            if (callback.Apps.TryGetValue(appId, out var appInfo))
            {
                var commonSection = appInfo.KeyValues.Children.Find(child => child.Name == "common");
                if (commonSection == null)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var clientIcon = commonSection["clienticon"].Value;
                tcs.TrySetResult(string.IsNullOrEmpty(clientIcon) ? null : clientIcon);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        });

        try
        {
            // Add a timeout to prevent infinite waiting
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await tcs.Task.WaitAsync(cts.Token);
            return result;
        }
        catch (TimeoutException)
        {
            Console.WriteLine($"Timeout waiting for app info for AppID {appId}");
            return null;
        }
        finally
        {
            // Unsubscribe to prevent memory leaks
            subscription.Dispose();
        }
    }

    private async Task<bool> DownloadIconAsync(uint appId, string clientIcon)
    {
        string iconUrl = string.Format(SteamIconUrlTemplate, appId, clientIcon);
        string iconPath = Path.Combine(_steamPath, "steam", "games", $"{clientIcon}.ico");

        try
        {
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(iconPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            byte[] fileBytes = await client.GetByteArrayAsync(iconUrl);
            await File.WriteAllBytesAsync(iconPath, fileBytes);

            return true;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"Failed to download icon for AppID {appId}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving icon for AppID {appId}: {ex.Message}");
            return false;
        }
    }

    public async Task RestoreIconsAsync()
    {
        Console.WriteLine("Discovering installed games...");
        var installedGames = GetInstalledGames(_libraryFolders);
        Console.WriteLine($"Found {installedGames.Count} installed games.");
        Console.WriteLine();
        
        int successCount = 0;
        int failureCount = 0;
        int current = 0;

        foreach (var game in installedGames)
        {
            current++;
            Console.Write($"[{current}/{installedGames.Count}] {game.Name} (AppID: {game.AppId})... ");
            
            var clientIcon = await GetClientIconAsync(game.AppId);
            if (clientIcon != null)
            {
                bool downloaded = await DownloadIconAsync(game.AppId, clientIcon);
                if (downloaded)
                {
                    Console.WriteLine("[OK]");
                    successCount++;
                }
                else
                {
                    Console.WriteLine("[FAILED - download error]");
                    failureCount++;
                }
            }
            else
            {
                Console.WriteLine("[SKIPPED - no icon available]");
                failureCount++;
            }
        }

        Console.WriteLine();
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine($"Icon restoration complete:");
        Console.WriteLine($"  Successful: {successCount}");
        Console.WriteLine($"  Failed/Skipped: {failureCount}");
        Console.WriteLine($"  Total: {installedGames.Count}");
        Console.WriteLine("=".PadRight(60, '='));

        if (successCount > 0 && OperatingSystem.IsWindows())
        {
            RestartExplorer();
        }
    }

    private static void RestartExplorer()
    {
        try
        {
            Console.WriteLine("Restarting Windows Explorer to refresh icons...");

            var explorerProcesses = Process.GetProcessesByName("explorer");
            if (explorerProcesses.Length > 0)
            {
                explorerProcesses[0].Kill();
                explorerProcesses[0].WaitForExit(5000);
            }

            Process.Start("explorer.exe");
            Console.WriteLine("Explorer restarted successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to restart Explorer: {ex.Message}");
            Console.WriteLine("You may need to restart Explorer manually for icons to refresh.");
        }
    }
}