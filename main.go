package main

import (
	"bytes"
	"context"
	"fmt"
	"log"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	corev1 "k8s.io/api/core/v1"
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/client-go/kubernetes"
	"k8s.io/client-go/rest"
	"k8s.io/client-go/tools/clientcmd"
)

const (
	logInterval = 15 * time.Minute
	pvMountPath = "/pv-logs"
	region      = "ap-south-1" // Mumbai
)

type LogCollector struct {
	k8sClient *kubernetes.Clientset
	namespace string
}

func NewLogCollector() (*LogCollector, error) {
	// Initialize Kubernetes client
	k8sClient, err := initK8sClient()
	if err != nil {
		return nil, fmt.Errorf("failed to create k8s client: %w", err)
	}

	namespace := os.Getenv("POD_NAMESPACE")
	if namespace == "" {
		namespace = "default"
	}

	return &LogCollector{
		k8sClient: k8sClient,
		namespace: namespace,
	}, nil
}

func initK8sClient() (*kubernetes.Clientset, error) {
	config, err := rest.InClusterConfig()
	if err != nil {
		// Fallback to local kubeconfig for local development
		kubeconfig := filepath.Join(os.Getenv("HOME"), ".kube", "config")
		config, err = clientcmd.BuildConfigFromFlags("", kubeconfig)
		if err != nil {
			return nil, err
		}
	}

	return kubernetes.NewForConfig(config)
}

func (lc *LogCollector) CollectLogs(ctx context.Context) error {
	// Create logs directory on PV
	timestamp := time.Now().Format("2006-01-02_15-04-05")
	logDir := filepath.Join(pvMountPath, timestamp)

	if err := os.MkdirAll(logDir, 0755); err != nil {
		return fmt.Errorf("failed to create log directory: %w", err)
	}

	log.Printf("Collecting logs to: %s", logDir)

	// Get all pods in the namespace
	pods, err := lc.k8sClient.CoreV1().Pods(lc.namespace).List(ctx, metav1.ListOptions{})
	if err != nil {
		return fmt.Errorf("failed to list pods: %w", err)
	}

	for _, pod := range pods.Items {
		// Get logs for each container in the pod
		for _, container := range pod.Spec.Containers {
			logContent, err := lc.getPodLogs(ctx, pod.Name, container.Name)
			if err != nil {
				log.Printf("Error getting logs for pod %s container %s: %v", pod.Name, container.Name, err)
				continue
			}

			// Save logs to PV
			logFile := filepath.Join(logDir, fmt.Sprintf("%s_%s.log", pod.Name, container.Name))
			if err := os.WriteFile(logFile, []byte(logContent), 0644); err != nil {
				log.Printf("Error writing log file %s: %v", logFile, err)
				continue
			}

			log.Printf("Saved logs for pod %s container %s (%d bytes)", pod.Name, container.Name, len(logContent))
		}
	}

	// Write metadata file
	metadata := fmt.Sprintf("Log collection completed at %s\nCluster: UATMuzic\nRegion: %s\nPods processed: %d\n",
		time.Now().Format(time.RFC3339), region, len(pods.Items))
	metadataFile := filepath.Join(logDir, "_metadata.txt")
	if err := os.WriteFile(metadataFile, []byte(metadata), 0644); err != nil {
		log.Printf("Error writing metadata file: %v", err)
	}

	return nil
}

func (lc *LogCollector) getPodLogs(ctx context.Context, podName, containerName string) (string, error) {
	opts := &corev1.PodLogOptions{
		Container: containerName,
	}

	req := lc.k8sClient.CoreV1().Pods(lc.namespace).GetLogs(podName, opts)
	stream, err := req.Stream(ctx)
	if err != nil {
		return "", err
	}
	defer stream.Close()

	buf := new(bytes.Buffer)
	_, err = buf.ReadFrom(stream)
	if err != nil {
		return "", err
	}

	return buf.String(), nil
}

func (lc *LogCollector) Run(ctx context.Context) {
	ticker := time.NewTicker(logInterval)
	defer ticker.Stop()

	log.Printf("Starting log collector - collecting logs every %v", logInterval)
	log.Printf("PV Mount Path: %s", pvMountPath)
	log.Printf("Namespace: %s", lc.namespace)

	// Run immediately on start
	if err := lc.CollectLogs(ctx); err != nil {
		log.Printf("Error collecting logs: %v", err)
	}

	for {
		select {
		case <-ctx.Done():
			log.Println("Shutting down log collector")
			return
		case <-ticker.C:
			log.Printf("=== Starting scheduled log collection at %s ===", time.Now().Format(time.RFC3339))
			if err := lc.CollectLogs(ctx); err != nil {
				log.Printf("Error collecting logs: %v", err)
			}
			log.Printf("=== Completed log collection at %s ===", time.Now().Format(time.RFC3339))
		}
	}
}

func main() {
	log.Println("=== Lab Logs Collector Starting ===")
	log.Printf("Version: 1.0.0")
	log.Printf("Tag: pvlab")

	collector, err := NewLogCollector()
	if err != nil {
		log.Fatalf("Failed to create log collector: %v", err)
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// Handle graceful shutdown
	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)

	go func() {
		<-sigChan
		log.Println("Received shutdown signal")
		cancel()
	}()

	collector.Run(ctx)
}
