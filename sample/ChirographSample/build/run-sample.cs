// Runs the sample end to end, on Linux, macOS and Windows alike (mise run sample:run, or dotnet run build/run-sample.cs from
// sample/ChirographSample):
//
//   1. packs src/** into artifacts/nuget-sample (build/pack.proj) when the package is missing or older than its sources;
//   2. builds ChirographSample.slnx, which needs no server: the URL client is generated from its committed document cache;
//   3. starts Rockets.Api on http://localhost:5179 and waits until it answers;
//   4. runs each app with Clients__<ClientName>__BaseAddress pointing at it, and compares what it prints with expected/<App>.txt;
//   5. stops the API.
//
// It exits non-zero when a step fails, an app exits non-zero, or an app's output differs from its expected file.

using System.Diagnostics;
using System.Text;

const string ApiAddress = "http://localhost:5179/";
string[] clientNames = ["RocketsClient", "RocketsRefitClient", "RocketsUrlClient", "RocketsDovetailClient"];
string[] apps = ["Rockets.Console", "Rockets.Dovetail"];

var sample = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.Combine(Directory.GetCurrentDirectory(), "build"), ".."));
var repository = Path.GetFullPath(Path.Combine(sample, "..", ".."));
// the dotnet that mise installed (it exports DOTNET_ROOT), so a machine's other SDKs aren't picked up from PATH
var dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root
          && File.Exists(Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
    ? Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")
    : "dotnet";
var logs = Path.Combine(repository, "artifacts", "logs");
Directory.CreateDirectory(logs);

try
{
    if (PackIsStale())
    {
        Step("Packing src/** at 0.0.0-sample");
        Run(dotnet, ["msbuild", "build/pack.proj", $"-bl:{Path.Combine(logs, "sample-pack.binlog")}"], sample);
    }
    else
    {
        Step("The packed Chirograph is up to date");
    }

    Step("Building ChirographSample.slnx");
    Run(dotnet, ["build", "ChirographSample.slnx", $"-bl:{Path.Combine(logs, "sample-build.binlog")}"], sample);

    Step($"Starting Rockets.Api on {ApiAddress}");
    using var api = await StartApi();

    var failures = 0;
    foreach (var app in apps)
    {
        Step($"Running {app}");
        if (!File.Exists(Assembly(app)))
        {
            failures++;
            Console.Error.WriteLine($"FAIL {app} wasn't built: {Assembly(app)} doesn't exist.");
            continue;
        }

        var (exitCode, output, error) = Capture(dotnet, [Assembly(app)], Path.Combine(sample, app), ClientEnvironment());
        Console.Write(output);
        Console.Error.Write(error);
        var expectedPath = Path.Combine(sample, "expected", $"{app}.txt");
        var expected = File.Exists(expectedPath) ? Normalize(File.ReadAllText(expectedPath)) : "";
        if (exitCode != 0)
        {
            failures++;
            Console.Error.WriteLine($"FAIL {app} exited with {exitCode}.");
        }
        else if (Normalize(output) != expected)
        {
            failures++;
            Console.Error.WriteLine($"FAIL {app} printed something other than {Path.GetRelativePath(sample, expectedPath)}:");
            Console.Error.WriteLine(Diff(expected, Normalize(output)));
        }
        else
        {
            Console.WriteLine($"PASS {app} printed {Path.GetRelativePath(sample, expectedPath)}.");
        }
    }

    return failures == 0 ? 0 : 1;
}
catch (StepFailedException e)
{
    Console.Error.WriteLine($"FAIL {e.Message}");
    return 1;
}

// The pack is stale when its package is missing, or older than anything it is packed from.
bool PackIsStale()
{
    var package = Path.Combine(repository, "artifacts", "nuget-sample", "Chirograph.0.0.0-sample.nupkg");
    if (!File.Exists(package))
    {
        return true;
    }

    var packed = File.GetLastWriteTimeUtc(package);
    var sources = Directory
                 .EnumerateFiles(Path.Combine(repository, "src"), "*", SearchOption.AllDirectories)
                 .Where(z => !z.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj"))
                 .Append(Path.Combine(repository, "build", "Chirograph.PackLocal.targets"))
                 .Append(Path.Combine(sample, "build", "pack.proj"));
    return sources.Any(z => File.GetLastWriteTimeUtc(z) > packed);
}

// Every client reads its base address from Clients:<ClientName>:BaseAddress.
Dictionary<string, string> ClientEnvironment() => clientNames.ToDictionary(z => $"Clients__{z}__BaseAddress", _ => ApiAddress);

string Assembly(string project) => Path.Combine(sample, project, "bin", "Debug", "net10.0", $"{project}.dll");

async Task<ApiProcess> StartApi()
{
    var start = new ProcessStartInfo(dotnet, [Assembly("Rockets.Api"), "--urls", ApiAddress])
    {
        WorkingDirectory = Path.Combine(sample, "Rockets.Api"),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    var process = Process.Start(start) ?? throw new StepFailedException("Rockets.Api did not start.");
    var log = new StringBuilder();
    process.OutputDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    var api = new ApiProcess(process, log, Path.Combine(logs, "sample-api.log"));

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (DateTime.UtcNow < deadline)
    {
        if (process.HasExited)
        {
            api.Dispose();
            throw new StepFailedException($"Rockets.Api exited with {process.ExitCode} before it was ready; its output is in {api.LogPath}.");
        }

        try
        {
            using var response = await http.GetAsync(new Uri(new Uri(ApiAddress), "rockets"));
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Rockets.Api is ready at {ApiAddress}");
                return api;
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }

        await Task.Delay(250);
    }

    api.Dispose();
    throw new StepFailedException($"Rockets.Api wasn't ready at {ApiAddress} within 60 seconds; its output is in {api.LogPath}.");
}

static void Step(string message) => Console.WriteLine($"==> {message}");

static void Run(string fileName, string[] arguments, string workingDirectory)
{
    var start = new ProcessStartInfo(fileName, arguments) { WorkingDirectory = workingDirectory, UseShellExecute = false };
    using var process = Process.Start(start) ?? throw new StepFailedException($"{fileName} did not start.");
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new StepFailedException($"{Path.GetFileName(fileName)} {string.Join(' ', arguments)} exited with {process.ExitCode}.");
    }
}

static (int ExitCode, string Output, string Error) Capture(
    string fileName,
    string[] arguments,
    string workingDirectory,
    Dictionary<string, string> environment
)
{
    var start = new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var (key, value) in environment)
    {
        start.Environment[key] = value;
    }

    using var process = Process.Start(start) ?? throw new StepFailedException($"{fileName} did not start.");
    var error = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output, error.Result);
}

// Compared without regard to line endings or trailing blank lines, so a checkout with CRLF endings passes too.
static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

static string Diff(string expected, string actual)
{
    var expectedLines = expected.Split('\n');
    var actualLines = actual.Split('\n');
    var diff = new StringBuilder();
    for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
    {
        var e = i < expectedLines.Length ? expectedLines[i] : null;
        var a = i < actualLines.Length ? actualLines[i] : null;
        if (e == a)
        {
            diff.AppendLine($"  {e}");
            continue;
        }

        if (e is not null)
        {
            diff.AppendLine($"- {e}");
        }

        if (a is not null)
        {
            diff.AppendLine($"+ {a}");
        }
    }

    return diff.ToString();
}

// Rockets.Api, stopped (with its log written) when disposed.
sealed class ApiProcess(Process process, StringBuilder log, string logPath) : IDisposable
{
    public string LogPath => logPath;

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }

        lock (log)
        {
            File.WriteAllText(logPath, log.ToString());
        }

        process.Dispose();
        Console.WriteLine("Stopped Rockets.Api");
    }
}

sealed class StepFailedException(string message) : Exception(message);
