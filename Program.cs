using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using System.Reflection;
using SteamIconRestorer;


// Define options (flags)
Option<string?> usernameOption = new("--username")
{
    Description = "Steam username (required for credentials auth)",
    Aliases = { "-u", "--user" },
};

Option<string?> passwordOption = new("--password")
{
    Description = "Steam password (required for credentials auth)",
    Aliases = { "-p", "--pass" },
};

Option<bool> useQrCodeOption = new("--use-qr-code")
{
    Description = "Use QR Code to authenticate instead of username/password (recommended)",
    Aliases = { "-q", "--qr" },
};

Option<string?> steamInstallPathOption = new("--steam-install-path")
{
    Description = "Path to Steam installation directory (auto-detected if not specified)",
    Aliases = { "-s", "--steam-path" },
};

Option<bool> interactiveOption = new("--interactive")
{
    Description = "Run in interactive mode with prompts",
    Aliases = { "-i" },
    Hidden = false,
};

Option<bool> verboseOption = new("--verbose")
{
    Description = "Enable verbose output (prints stack traces on errors)",
    Aliases = { "-v" },
};

RootCommand command = new("Steam Icon Restorer - Restore game icons on Steam")
{
    usernameOption,
    passwordOption,
    useQrCodeOption,
    steamInstallPathOption,
    interactiveOption,
    verboseOption
};

command.SetAction(async parseResult =>
{
    PrintHeader();

    var interactive = parseResult.GetValue(interactiveOption);
    var username = parseResult.GetValue(usernameOption);
    var password = parseResult.GetValue(passwordOption);
    var useQrCode = parseResult.GetValue(useQrCodeOption);
    var steamPath = parseResult.GetValue(steamInstallPathOption);
    var verbose = parseResult.GetValue(verboseOption);

    // Interactive mode
    if (interactive || (args.Length == 0))
    {
        var code = await RunInteractiveModeAsync(verbose);
        if (code != 0) Environment.Exit(code);
        return;
    }

    // Auto-detect Steam path if not provided
    if (string.IsNullOrWhiteSpace(steamPath))
    {
        Console.WriteLine("Steam installation path not specified. Attempting auto-detection...");
        steamPath = TryDetectSteamPath();

        if (steamPath != null)
        {
            Console.WriteLine($"Found Steam at: {steamPath}");
        }
        else
        {
            await Console.Error.WriteLineAsync("Error: Could not auto-detect Steam installation path.");
            await Console.Error.WriteLineAsync("Please specify it using --steam-install-path flag.");
            Environment.Exit(1);
            return;
        }
    }

    // Validate Steam path
    if (!Directory.Exists(steamPath))
    {
        await Console.Error.WriteLineAsync($"Error: Steam installation path does not exist: {steamPath}");
        Environment.Exit(1);
        return;
    }

    // Validate input
    if (!useQrCode && (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
    {
        await Console.Error.WriteLineAsync(
            "Error: Username and password are required when not using QR Code authentication.");
        await Console.Error.WriteLineAsync(
            "Use --use-qr-code flag for QR authentication, or provide --username and --password.");
        Environment.Exit(1);
        return;
    }

    var exitCode = await ExecuteAsync(username, password, useQrCode, steamPath!, verbose);
    if (exitCode != 0) Environment.Exit(exitCode);
});

return await command.Parse(args).InvokeAsync();


void PrintHeader()
{
    var ver = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0";
    Console.WriteLine();
    Console.WriteLine("========================================");
    Console.WriteLine($"     Steam Icon Restorer v{ver}");
    Console.WriteLine("========================================");
    Console.WriteLine();
}

async Task<int> RunInteractiveModeAsync(bool verbose = false)
{
    Console.WriteLine("Running in interactive mode...");
    Console.WriteLine();

    // Detect Steam path
    string? steamPath = TryDetectSteamPath();

    if (steamPath != null)
    {
        Console.WriteLine($"Detected Steam installation at: {steamPath}");
        Console.Write("Use this path? (Y/n): ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (response == "n" || response == "no")
        {
            Console.Write("Enter Steam installation path: ");
            steamPath = Console.ReadLine()?.Trim();
        }
    }
    else
    {
        Console.WriteLine("Could not auto-detect Steam installation.");
        Console.Write("Enter Steam installation path: ");
        steamPath = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
    {
        await Console.Error.WriteLineAsync("Error: Invalid Steam installation path.");
        return 1;
    }

    // Choose authentication method
    Console.WriteLine();
    Console.WriteLine("Authentication Methods:");
    Console.WriteLine("  1. QR Code");
    Console.WriteLine("  2. Username & Password");
    Console.Write("Choose method (1 or 2): ");

    var authChoice = Console.ReadLine()?.Trim();
    bool useQrCode = authChoice != "2";

    string? username = null;
    string? password = null;

    if (!useQrCode)
    {
        Console.Write("Username: ");
        username = Console.ReadLine()?.Trim();

        Console.Write("Password: ");
        password = ReadPassword();
        Console.WriteLine();
    }

    Console.WriteLine();
    Console.WriteLine("Starting icon restoration process...");
    Console.WriteLine();

    return await ExecuteAsync(username, password, useQrCode, steamPath!, verbose);
}

async Task<int> ExecuteAsync(string? username, string? password, bool useQrCode, string steamPath, bool verbose)
{
    try
    {
        var authenticator = new SteamAuthenticator(username, password, useQrCode, steamPath);
        await authenticator.RunAsync();
        return 0;
    }
    catch (Exception ex)
    {
        if (verbose)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
        }
        else
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
        }
        return 1;
    }
}

static string? TryDetectSteamPath()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Common Windows locations
        string[] possiblePaths =
        [
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        ];

        // Check registry
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var registryPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(registryPath) && Directory.Exists(registryPath))
            {
                return registryPath;
            }
        }
        catch { /* Ignore registry errors */ }

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        string[] possiblePaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam"),
            "/usr/share/steam",
            "/usr/local/share/steam",
        ];

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        string[] possiblePaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam"),
        ];

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
    }

    return null;
}

static string ReadPassword()
{
    var password = "";
    ConsoleKeyInfo key;

    do
    {
        key = Console.ReadKey(true);

        if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
        {
            password += key.KeyChar;
            Console.Write("*");
        }
        else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password[0..^1];
            Console.Write("\b \b");
        }
    }
    while (key.Key != ConsoleKey.Enter);

    return password;
}