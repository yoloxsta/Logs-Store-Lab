namespace LabLogsCollector;

class Program
{
    public const int LogIntervalMinutes = 15;
    public const string PvMountPath = "/pv-logs";
    public const string Region = "ap-south-1";

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Lab Logs Collector Starting ===");
        Console.WriteLine("Version: 1.0.0");
        Console.WriteLine("Tag: pvlab");

        var namespaceName = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? "music-uat";
        var podName = Environment.GetEnvironmentVariable("POD_NAME") ?? "lab-logs-collector";
        var collector = new LogCollector(namespaceName, podName);
        
        await collector.RunAsync();
    }
}

class LogCollector
{
    private readonly string _namespace;
    private readonly string _podName;
    private readonly TimeSpan _logInterval = TimeSpan.FromMinutes(Program.LogIntervalMinutes);

    public LogCollector(string namespaceName, string podName)
    {
        _namespace = namespaceName;
        _podName = podName;
        
        Console.WriteLine($"Namespace: {_namespace}");
        Console.WriteLine($"Pod Name: {_podName}");
        Console.WriteLine($"PV Mount Path: {Program.PvMountPath}");
        Console.WriteLine($"Log Interval: {_logInterval}");
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"Starting log collector - collecting logs every {_logInterval.TotalMinutes} minutes");

        // Run immediately on start
        await CollectLogsAsync();

        using var timer = new PeriodicTimer(_logInterval);
        
        while (await timer.WaitForNextTickAsync())
        {
            Console.WriteLine($"=== Starting scheduled log collection at {DateTime.UtcNow:O} ===");
            await CollectLogsAsync();
            Console.WriteLine($"=== Completed log collection at {DateTime.UtcNow:O} ===");
        }
    }

    private async Task CollectLogsAsync()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var logDir = Path.Combine(Program.PvMountPath, timestamp);

        try
        {
            Directory.CreateDirectory(logDir);
            Console.WriteLine($"Collecting logs to: {logDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating directory: {ex.Message}");
            return;
        }

        try
        {
            // Write sample log file
            var logContent = $"Log collected at {DateTime.UtcNow:O}\n" +
                            $"Namespace: {_namespace}\n" +
                            $"Pod: {_podName}\n" +
                            $"Cluster: UATMuzic\n" +
                            $"Region: {Program.Region}\n" +
                            $"Message: Lab logs stored successfully\n";
            
            var logFile = Path.Combine(logDir, $"lab-logs_{timestamp}.log");
            await File.WriteAllTextAsync(logFile, logContent);
            Console.WriteLine($"Saved log file: {logFile} ({logContent.Length} bytes)");

            // Write metadata
            var metadata = $"Log collection completed at {DateTime.UtcNow:O}\n" +
                          $"Cluster: UATMuzic\n" +
                          $"Region: {Program.Region}\n" +
                          $"Namespace: {_namespace}\n";
            await File.WriteAllTextAsync(Path.Combine(logDir, "_metadata.txt"), metadata);
            Console.WriteLine($"Metadata file written");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting logs: {ex.Message}");
        }
    }
}
