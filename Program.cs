namespace LabLogsCollector;

class Program
{
    // ========================================
    // CONFIGURATION CONSTANTS
    // ========================================
    
    /// <summary>
    /// How often to write logs (in minutes)
    /// </summary>
    public const int LogIntervalMinutes = 15;
    
    /// <summary>
    /// WHERE LOGS ARE STORED
    /// This is the mount point inside the container
    /// Maps to the PVC in Kubernetes
    /// </summary>
    public const string PvMountPath = "/pv-logs";
    
    /// <summary>
    /// AWS region for metadata
    /// </summary>
    public const string Region = "ap-south-1";

    static async Task Main(string[] args)
    {
        // ========================================
        // APPLICATION STARTUP
        // ========================================
        
        Console.WriteLine("=== Lab Logs Collector Starting ===");
        Console.WriteLine("Version: 1.0.0");
        Console.WriteLine("Tag: pvlab");

        // Get environment variables from Kubernetes
        // These are automatically injected by Kubernetes
        var namespaceName = Environment.GetEnvironmentVariable("POD_NAMESPACE") ?? "music-uat";
        var podName = Environment.GetEnvironmentVariable("POD_NAME") ?? "lab-logs-collector";
        
        // Create collector and start
        var collector = new LogCollector(namespaceName, podName);
        await collector.RunAsync();
    }
}

class LogCollector
{
    // ========================================
    // PRIVATE FIELDS
    // ========================================
    
    private readonly string _namespace;          // Kubernetes namespace
    private readonly string _podName;            // Pod name
    private readonly TimeSpan _logInterval;      // 15 minutes

    // ========================================
    // CONSTRUCTOR
    // ========================================
    
    public LogCollector(string namespaceName, string podName)
    {
        _namespace = namespaceName;
        _podName = podName;
        _logInterval = TimeSpan.FromMinutes(Program.LogIntervalMinutes);
        
        // Log configuration for debugging
        Console.WriteLine($"[CONFIG] Namespace: {_namespace}");
        Console.WriteLine($"[CONFIG] Pod Name: {_podName}");
        Console.WriteLine($"[CONFIG] PV Mount Path: {Program.PvMountPath}");
        Console.WriteLine($"[CONFIG] Log Interval: {_logInterval.TotalMinutes} minutes");
    }

    // ========================================
    // MAIN RUN LOOP
    // ========================================
    
    public async Task RunAsync()
    {
        Console.WriteLine($"[START] Starting log collector - collecting logs every {_logInterval.TotalMinutes} minutes");

        // STEP 1: Run IMMEDIATELY on start (don't wait 15 minutes)
        Console.WriteLine("[STEP 1] Running initial log collection...");
        await CollectLogsAsync();

        // STEP 2: Create periodic timer (fires every 15 minutes)
        using var timer = new PeriodicTimer(_logInterval);
        
        // STEP 3: Infinite loop - runs forever until pod is terminated
        while (await timer.WaitForNextTickAsync())
        {
            Console.WriteLine($"[TIMER] === 15 minutes elapsed - starting scheduled collection at {DateTime.UtcNow:O} ===");
            await CollectLogsAsync();
            Console.WriteLine($"[TIMER] === Collection completed at {DateTime.UtcNow:O} ===");
        }
    }

    // ========================================
    // LOG COLLECTION METHOD
    // This is where the magic happens!
    // ========================================
    
    private async Task CollectLogsAsync()
    {
        // ========================================
        // STEP 1: CREATE TIMESTAMPED DIRECTORY
        // ========================================
        
        // Create a unique directory name based on current time
        // Example: 2026-06-16_00-30-20
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        
        // Combine paths: /pv-logs/2026-06-16_00-30-20
        // This creates the full path to store logs
        var logDir = Path.Combine(Program.PvMountPath, timestamp);

        try
        {
            // CREATE DIRECTORY ON PVC
            // This writes to the Persistent Volume!
            // If PVC is backed by EBS, this creates directory on EBS volume
            // If PVC is backed by EFS, this creates directory on EFS
            Directory.CreateDirectory(logDir);
            
            Console.WriteLine($"[DIR] Created directory: {logDir}");
        }
        catch (Exception ex)
        {
            // If this fails, PV might not be mounted or no permissions
            Console.WriteLine($"[ERROR] Failed to create directory: {ex.Message}");
            Console.WriteLine($"[ERROR] Check if PVC is properly mounted!");
            return;
        }

        try
        {
            // ========================================
            // STEP 2: PREPARE LOG CONTENT
            // ========================================
            
            // This is the data we want to store
            // In real scenario, you might collect actual logs from files, APIs, etc.
            var logContent = $"Log collected at {DateTime.UtcNow:O}\n" +
                            $"Namespace: {_namespace}\n" +
                            $"Pod: {_podName}\n" +
                            $"Cluster: UATMuzic\n" +
                            $"Region: {Program.Region}\n" +
                            $"Message: Lab logs stored successfully\n";
            
            // ========================================
            // STEP 3: WRITE LOG FILE TO PVC
            // ========================================
            
            // Create file path: /pv-logs/2026-06-16_00-30-20/lab-logs_2026-06-16_00-30-20.log
            var logFile = Path.Combine(logDir, $"lab-logs_{timestamp}.log");
            
            // WRITE TO PVC!
            // This is how data is stored on Persistent Volume
            // File.WriteAllTextAsync:
            //   1. Opens/creates file
            //   2. Writes content
            //   3. Closes file
            //   4. Data is now on EBS/EFS disk!
            await File.WriteAllTextAsync(logFile, logContent);
            
            Console.WriteLine($"[WRITE] Saved log file: {logFile}");
            Console.WriteLine($"[WRITE] File size: {logContent.Length} bytes");

            // ========================================
            // STEP 4: WRITE METADATA FILE
            // ========================================
            
            // Additional info about this collection
            var metadata = $"Log collection completed at {DateTime.UtcNow:O}\n" +
                          $"Cluster: UATMuzic\n" +
                          $"Region: {Program.Region}\n" +
                          $"Namespace: {_namespace}\n";
            
            var metadataFile = Path.Combine(logDir, "_metadata.txt");
            await File.WriteAllTextAsync(metadataFile, metadata);
            
            Console.WriteLine($"[META] Metadata file written: {metadataFile}");
            Console.WriteLine($"[SUCCESS] Log collection completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to write logs: {ex.Message}");
        }
    }
}
