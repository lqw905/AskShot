using System.Diagnostics;
using System.Net.Http;

namespace ScreenMind.Client.Services;

/// <summary>
/// Manages the Python inference service as a child process.
/// Starts, monitors, and auto-restarts the Python FastAPI service.
/// </summary>
public class PythonServiceManager : IDisposable
{
    private Process? _process;
    private readonly string _pythonExe;
    private readonly string _serviceDir;
    private const int Port = 8900;
    private const int MaxRestartAttempts = 3;
    private int _restartCount;
    private bool _disposed;

    public PythonServiceManager(string pythonExe, string serviceDir)
    {
        _pythonExe = pythonExe;
        _serviceDir = serviceDir;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (await IsHealthy())
        {
            Console.WriteLine("[ScreenMind] Python service already running.");
            return;
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _pythonExe,
                Arguments = "main.py",
                WorkingDirectory = _serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Exited += OnProcessExited;
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"[Python] {e.Data}");
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Console.WriteLine($"[Python:err] {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Console.WriteLine($"[ScreenMind] Started Python service (PID: {_process.Id})");

        // Wait for health check
        await WaitForReady(TimeSpan.FromSeconds(30), ct);
        Console.WriteLine("[ScreenMind] Python service is healthy.");
    }

    private async Task<bool> IsHealthy()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"http://127.0.0.1:{Port}/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForReady(TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException();
            if (await IsHealthy()) return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("Python service failed to start within timeout.");
    }

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        _restartCount++;
        Console.WriteLine($"[ScreenMind] Python process exited (attempt {_restartCount}/{MaxRestartAttempts})");

        if (_restartCount <= MaxRestartAttempts)
        {
            // Exponential backoff: 2s, 4s, 8s
            var delay = TimeSpan.FromSeconds(Math.Pow(2, _restartCount));
            Console.WriteLine($"[ScreenMind] Restarting in {delay.TotalSeconds}s...");
            await Task.Delay(delay);
            await StartAsync();
        }
        else
        {
            Console.WriteLine("[ScreenMind] Max restart attempts reached. Service stopped.");
        }
    }

    public void Stop()
    {
        _disposed = true;
        if (_process is { HasExited: false })
        {
            _process.Exited -= OnProcessExited;
            _process.Kill();
            _process.Dispose();
            Console.WriteLine("[ScreenMind] Python service stopped.");
        }
    }

    public void Dispose() => Stop();
}
