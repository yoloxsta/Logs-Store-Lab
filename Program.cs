using k8s;
using k8s.Models;

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
        var collector = new LogCollector(namespaceName);
        
        await collector.RunAsync();
    }
}

class LogCollector
{
    private readonly Kubernetes _k8sClient;
    private readonly string _namespace;
    private readonly TimeSpan _logInterval = TimeSpan.FromMinutes(Program.LogIntervalMinutes);

    public LogCollector(string namespaceName)
    {
        _namespace = namespaceName;
        var config = KubernetesClientConfiguration.InClusterConfig();
        _k8sClient = new Kubernetes(config);
        
        Console.WriteLine($"Namespace: {_namespace}");
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
            var pods = await _k8sClient.ListNamespacedPodAsync(_namespace);
            Console.WriteLine($"Found {pods.Items.Count} pods");

            foreach (var pod in pods.Items)
            {
                foreach (var container in pod.Spec.Containers)
                {
                    try
                    {
                        var logs = await GetPodLogsAsync(pod.Metadata.Name, container.Name);
                        var logFile = Path.Combine(logDir, $"{pod.Metadata.Name}_{container.Name}.log");
                        
                        await File.WriteAllTextAsync(logFile, logs);
                        Console.WriteLine($"Saved logs for pod {pod.Metadata.Name} container {container.Name} ({logs.Length} bytes)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting logs for pod {pod.Metadata.Name} container {container.Name}: {ex.Message}");
                    }
                }
            }

            // Write metadata
            var metadata = $"Log collection completed at {DateTime.UtcNow:O}\nCluster: UATMuzic\nRegion: {Program.Region}\nPods processed: {pods.Items.Count}\n";
            await File.WriteAllTextAsync(Path.Combine(logDir, "_metadata.txt"), metadata);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting logs: {ex.Message}");
        }
    }

    private async Task<string> GetPodLogsAsync(string podName, string containerName)
    {
        var stream = await _k8sClient.ReadNamespacedPodLogAsync(
            podName,
            _namespace,
            container: containerName
        );

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
