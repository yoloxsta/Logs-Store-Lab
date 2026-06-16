# Lab Logs Collector

.NET application that stores logs on Persistent Volume every 15 minutes.

## Details

| Component | Value |
|-----------|-------|
| **Language** | .NET 8.0 |
| **EKS Cluster** | UATMuzic |
| **Region** | ap-south-1 (Mumbai) |
| **Namespace** | music-uat |
| **Storage Class** | ebs-sc |
| **PVC Size** | 2 GiB |
| **Interval** | 15 minutes |
| **Architecture** | ARM64 |
| **RBAC Required** | No |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    EKS Cluster: UATMuzic                    │
│                    Region: ap-south-1 (Mumbai)              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │           Namespace: music-uat                        │  │
│  │                                                        │  │
│  │  ┌─────────────────────────────────────────────────┐ │  │
│  │  │  Pod: lab-logs-collector                         │ │  │
│  │  │                                                  │ │  │
│  │  │  Container (.NET 8.0)                            │ │  │
│  │  │  • No RBAC needed                               │ │  │
│  │  │  • Writes logs every 15 minutes                  │ │  │
│  │  │  • Stores to /pv-logs                            │ │  │
│  │  │           │                                      │ │  │
│  │  │           │ mount                                │ │  │
│  │  │           ▼                                      │ │  │
│  │  │  /pv-logs/                                       │ │  │
│  │  │  ├── 2026-06-15_06-42-08/                       │ │  │
│  │  │  ├── 2026-06-15_06-45-07/                       │ │  │
│  │  │  └── ...                                         │ │  │
│  │  └─────────────────────────────────────────────────┘ │  │
│  └──────────────────────────────────────────────────────┘  │
│                           │                                 │
│                           │ bound                           │
│                           ▼                                 │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  PVC: lab-logs-pvc (2 GiB)                           │  │
│  └──────────────────────────────────────────────────────┘  │
│                           │                                 │
│                           │ dynamic provisioning            │
│                           ▼                                 │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  EBS Volume: gp3 (2 GiB, 3000 IOPS)                  │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Deploy

```bash
# Update kubeconfig
aws eks update-kubeconfig --name UATMuzic --region ap-south-1

# Build and push ARM64 image
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
aws ecr get-login-password --region ap-south-1 | docker login --username AWS --password-stdin ${AWS_ACCOUNT_ID}.dkr.ecr.ap-south-1.amazonaws.com
docker buildx build --platform linux/arm64 -t ${AWS_ACCOUNT_ID}.dkr.ecr.ap-south-1.amazonaws.com/hello-world:pvlab --push .

# Update deployment.yaml with your account ID
sed -i "s|<AWS_ACCOUNT_ID>|${AWS_ACCOUNT_ID}|g" k8s/deployment.yaml

# Deploy
kubectl apply -f k8s/pvc.yaml
kubectl apply -f k8s/deployment.yaml
```

## Test, Check, and Confirm

### 1. Check Pod Status

```bash
kubectl get pods -n music-uat -l app=lab-logs-collector
```

Expected output:
```
NAME                                  READY   STATUS    RESTARTS   AGE
lab-logs-collector-xxxxxxxxx-xxxxx   1/1     Running   0          5m
```

### 2. Check PVC Status

```bash
kubectl get pvc -n music-uat
```

Expected output:
```
NAME           STATUS   VOLUME                                     CAPACITY   STORAGECLASS   AGE
lab-logs-pvc   Bound    pvc-a9e5dab1-835f-4784-b7a2-d5201f1ecfe4   2Gi        ebs-sc         5m
```

### 3. Check Application Logs

```bash
kubectl logs -n music-uat -l app=lab-logs-collector
```

Expected output:
```
=== Lab Logs Collector Starting ===
Version: 1.0.0
Tag: pvlab
Namespace: music-uat
Pod Name: lab-logs-collector-xxxxxxxxx-xxxxx
PV Mount Path: /pv-logs
Log Interval: 00:15:00
Starting log collector - collecting logs every 15 minutes
Collecting logs to: /pv-logs/2026-06-16_00-30-20
Saved log file: /pv-logs/2026-06-16_00-30-20/lab-logs_2026-06-16_00-30-20.log (183 bytes)
Metadata file written
```

### 4. Check PV Mount

```bash
kubectl exec -n music-uat deploy/lab-logs-collector -- ls -la /pv-logs
```

Expected output:
```
total 60
drwxr-xr-x   13 root     root          4096 Jun 15 11:14 .
drwxr-xr-x    1 root     root            62 Jun 15 11:14 ..
drwxr-xr-x    2 root     root          4096 Jun 15 06:42 2026-06-15_06-42-08
drwxr-xr-x    2 root     root          4096 Jun 15 06:45 2026-06-15_06-45-07
...
drwx------    2 root     root         16384 Jun 15 06:42 lost+found
```

### 5. Check Storage Usage

```bash
kubectl exec -n music-uat deploy/lab-logs-collector -- df -h /pv-logs
```

Expected output:
```
Filesystem                Size      Used Available Use% Mounted on
/dev/nvme2n1              1.9G      1.8M      1.9G   0% /pv-logs
```

### 6. View Collected Logs

```bash
# List all log directories
kubectl exec -n music-uat deploy/lab-logs-collector -- ls /pv-logs

# View a specific log file
kubectl exec -n music-uat deploy/lab-logs-collector -- cat /pv-logs/2026-06-16_00-30-20/lab-logs_2026-06-16_00-30-20.log

# View metadata
kubectl exec -n music-uat deploy/lab-logs-collector -- cat /pv-logs/2026-06-16_00-30-20/_metadata.txt
```

### 7. Monitor Real-time Logs

```bash
kubectl logs -f -n music-uat -l app=lab-logs-collector
```

### 8. Check EBS Volume in AWS Console

1. Go to AWS Console → EC2 → Volumes
2. Find volume with name: `UATMuzic-dynamic-pvc-a9e5dab1-835f-4784-b7a2-d5201f1ecfe4`
3. Verify:
   - Size: 2 GiB
   - Type: gp3
   - Status: In-use
   - Attached to EC2 node

### 9. Interactive Shell (Advanced Testing)

```bash
kubectl exec -it -n music-uat deploy/lab-logs-collector -- sh

# Inside container:
ls -la /pv-logs                    # List all logs
df -h /pv-logs                     # Check disk usage
du -sh /pv-logs                    # Check total size
find /pv-logs -type f | wc -l      # Count files
cat /pv-logs/2026-06-16_00-30-20/lab-logs_2026-06-16_00-30-20.log
```

## Visual Connection Flow

```
┌──────────────┐
│  Developer   │
│  (You)       │
└──────┬───────┘
       │ git push
       ▼
┌──────────────┐
│   GitHub     │
│  Repository  │
└──────┬───────┘
       │ trigger
       ▼
┌──────────────────────────────────────┐
│       GitHub Actions Workflow         │
│                                      │
│  1. Checkout code                    │
│  2. Configure AWS credentials        │
│  3. Login to ECR                     │
│  4. Build ARM64 Docker image         │
│  5. Push to ECR (hello-world:pvlab)  │
└──────┬───────────────────────────────┘
       │ image ready
       ▼
┌──────────────────────────────────────┐
│     Amazon ECR                       │
│  hello-world:pvlab (ARM64)           │
└──────┬───────────────────────────────┘
       │ kubectl apply
       ▼
┌──────────────────────────────────────┐
│     Amazon EKS Cluster (UATMuzic)    │
│                                      │
│  ┌────────────────────────────────┐ │
│  │  Namespace: music-uat          │ │
│  │                                │ │
│  │  Pod: lab-logs-collector       │ │
│  │  ├── Container (.NET 8.0)      │ │
│  │  └── Volume: /pv-logs          │ │
│  └────────────────────────────────┘ │
│                │                     │
│                ▼                     │
│  ┌────────────────────────────────┐ │
│  │  PVC: lab-logs-pvc (2 GiB)     │ │
│  └────────────────────────────────┘ │
└──────────────┬───────────────────────┘
               │ dynamic provisioning
               ▼
┌──────────────────────────────────────┐
│          Amazon EBS Volume           │
│  • gp3, 2 GiB, 3000 IOPS             │
│  • Persistent storage                │
│  • Stores log files                  │
└──────────────────────────────────────┘
```

## Delete Data

### Delete all logs (keep PV)

```bash
kubectl exec -n music-uat deploy/lab-logs-collector -- sh -c "rm -rf /pv-logs/2026-*"
```

### Delete specific date

```bash
kubectl exec -n music-uat deploy/lab-logs-collector -- rm -rf /pv-logs/2026-06-15_06-42-08
```

### Delete entire deployment

```bash
kubectl delete -f k8s/deployment.yaml
kubectl delete -f k8s/pvc.yaml
```

## GitHub Actions

Add secrets to your repository:
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`

Push to main branch to trigger automatic build.

## Storage Capacity

| Metric | Value |
|--------|-------|
| **Total Capacity** | 2 GiB |
| **Current Usage** | ~1.8 MB |
| **Available** | ~1.9 GB |
| **Per Day** | ~3.3 MB |
| **Per Month** | ~100 MB |
| **Estimated Time to Fill** | ~20 months |

## Files

```
lab-logs-collector/
├── LabLogsCollector.csproj      # .NET project
├── Program.cs                   # Main application
├── Dockerfile                   # Docker image (ARM64)
├── README.md                    # This file
├── k8s/
│   ├── pvc.yaml                 # Persistent Volume Claim
│   └── deployment.yaml          # Deployment config
└── .github/
    └── workflows/
        └── deploy.yaml          # GitHub Actions CI/CD
```
