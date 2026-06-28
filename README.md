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

---

## PVC Access Modes and Binding

### Access Modes

| Access Mode | Description | Storage Support | Use Case |
|-------------|-------------|-----------------|----------|
| **ReadWriteOnce (RWO)** | Single node can read/write | EBS, Local | Single pod, database |
| **ReadWriteMany (RWX)** | Multiple nodes can read/write | EFS, NFS | Multiple pods, shared data |
| **ReadOnlyMany (ROX)** | Multiple nodes can read only | EFS, NFS | Config files |

### Volume Binding Modes

| Mode | Behavior | When to Use |
|------|----------|-------------|
| **Immediate** | PV created immediately when PVC is created | EFS, when you know the AZ in advance |
| **WaitForFirstConsumer** | PV created only when first pod uses the PVC | EBS, to ensure PV is in same AZ as pod |

### Your Current Setup (EBS - RWO)

```yaml
# pvc.yaml - ReadWriteOnce (RWO)
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: lab-logs-pvc
spec:
  accessModes:
  - ReadWriteOnce    # Single node only
  storageClassName: ebs-sc
  resources:
    requests:
      storage: 2Gi
```

**Behavior:**
```
┌─────────────────────────────────────────────────────────┐
│         ReadWriteOnce (RWO) - EBS Volume                │
├─────────────────────────────────────────────────────────┤
│  Node 1: ✅ Can mount and read/write                    │
│  Node 2: ❌ CANNOT mount (volume already attached)      │
│  Node 3: ❌ CANNOT mount (volume already attached)      │
│                                                         │
│  If pod moves to Node 2:                               │
│    → EBS detaches from Node 1                          │
│    → EBS attaches to Node 2                            │
└─────────────────────────────────────────────────────────┘
```

---

## Solution: Mount EBS/EFS on EC2 Jump Host

### Problem with EBS (RWO)

EBS volumes can only be attached to **ONE instance at a time**. You cannot:
- ❌ Mount EBS on EC2 jump host while pod is using it
- ❌ Access data from multiple locations simultaneously

### Solution: Use EFS (RWX)

EFS allows **multiple mounts simultaneously**. You can:
- ✅ Mount on EC2 jump host
- ✅ Mount on EKS pods
- ✅ Access from both at the same time

### EFS Setup for EC2 + EKS Access

#### Step 1: Create EFS File System

```bash
# Create EFS
aws efs create-file-system \
  --performance-mode generalPurpose \
  --throughput-mode bursting \
  --region ap-south-1 \
  --tags Key=Name,Value=lab-logs-efs

# Note the File System ID: fs-xxxxxxxxx
```

#### Step 2: Create Mount Targets

```bash
# Create mount target in each AZ
aws efs create-mount-target \
  --file-system-id fs-xxxxxxxxx \
  --subnet-id subnet-xxxxxxxxx \
  --security-groups sg-xxxxxxxxx \
  --region ap-south-1

# Repeat for each AZ where your EKS nodes are
```

#### Step 3: Install EFS CSI Driver on EKS

```bash
# Install EFS CSI Driver
kubectl apply -k github.com/kubernetes-sigs/aws-efs-csi-driver/deploy/kubernetes/overlays/stable

# Verify installation
kubectl get pods -n kube-system -l app=efs-csi-controller
```

#### Step 4: Create EFS StorageClass

```yaml
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: efs-sc
provisioner: efs.csi.aws.com
volumeBindingMode: Immediate
```

```bash
kubectl apply -f - <<EOF
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: efs-sc
provisioner: efs.csi.aws.com
volumeBindingMode: Immediate
EOF
```

#### Step 5: Create PV (Manual)

```yaml
apiVersion: v1
kind: PersistentVolume
metadata:
  name: lab-logs-efs-pv
spec:
  capacity:
    storage: 5Gi
  volumeMode: Filesystem
  accessModes:
    - ReadWriteMany
  persistentVolumeReclaimPolicy: Retain
  storageClassName: efs-sc
  csi:
    driver: efs.csi.aws.com
    volumeHandle: fs-xxxxxxxxx  # Replace with your EFS ID
```

```bash
kubectl apply -f - <<EOF
apiVersion: v1
kind: PersistentVolume
metadata:
  name: lab-logs-efs-pv
spec:
  capacity:
    storage: 5Gi
  volumeMode: Filesystem
  accessModes:
    - ReadWriteMany
  persistentVolumeReclaimPolicy: Retain
  storageClassName: efs-sc
  csi:
    driver: efs.csi.aws.com
    volumeHandle: fs-xxxxxxxxx
EOF
```

#### Step 6: Create PVC

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: lab-logs-efs-pvc
  namespace: music-uat
spec:
  accessModes:
    - ReadWriteMany
  storageClassName: efs-sc
  resources:
    requests:
      storage: 5Gi
```

```bash
kubectl apply -f - <<EOF
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: lab-logs-efs-pvc
  namespace: music-uat
spec:
  accessModes:
    - ReadWriteMany
  storageClassName: efs-sc
  resources:
    requests:
      storage: 5Gi
EOF
```

#### Step 7: Update Deployment to Use EFS PVC

```yaml
# Update deployment.yaml volumes section
volumes:
- name: pv-logs
  persistentVolumeClaim:
    claimName: lab-logs-efs-pvc  # Use EFS PVC instead
```

#### Step 8: Mount EFS on EC2 Jump Host

```bash
# SSH to EC2 jump host
ssh -i your-key.pem ec2-user@<JUMP-HOST-IP>

# Install EFS utils
sudo yum install -y amazon-efs-utils

# Create mount point
sudo mkdir -p /mnt/lab-logs

# Mount EFS
sudo mount -t efs fs-xxxxxxxxx:/ /mnt/lab-logs

# View logs
ls -la /mnt/lab-logs
cat /mnt/lab-logs/2026-06-16_00-30-20/lab-logs_2026-06-16_00-30-20.log

# Auto-mount on boot (add to /etc/fstab)
echo "fs-xxxxxxxxx:/ /mnt/lab-logs efs defaults,_netdev 0 0" | sudo tee -a /etc/fstab
```

### EFS Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    EFS Shared Storage                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐         ┌─────────────────────────┐   │
│  │ EC2 Jump Host   │         │   EKS Cluster           │   │
│  │                 │         │                         │   │
│  │ /mnt/lab-logs   │◄───────►│   Pod 1: /pv-logs       │   │
│  │                 │   EFS   │   Pod 2: /pv-logs       │   │
│  │ ✅ Read/Write   │         │   Pod 3: /pv-logs       │   │
│  │                 │         │                         │   │
│  └─────────────────┘         │   ✅ All can access     │   │
│                              │   simultaneously!       │   │
│                              └─────────────────────────┘   │
│                                                             │
│  All see the SAME data at the SAME time!                    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### EBS vs EFS Comparison

| Feature | EBS | EFS |
|---------|-----|-----|
| **Access Mode** | RWO (ReadWriteOnce) | RWX (ReadWriteMany) |
| **Multi-attach** | ❌ No | ✅ Yes |
| **EC2 + EKS access** | ❌ Not simultaneously | ✅ Simultaneously |
| **AZ** | Single AZ | Multi-AZ |
| **Cost** | Lower | Higher |
| **Performance** | Higher IOPS | Lower IOPS |
| **Use case** | Single pod | Multiple pods + EC2 |

### When to Use Each

| Use Case | Storage Type | Access Mode |
|----------|--------------|-------------|
| Single pod deployment | EBS | RWO |
| Database (single replica) | EBS | RWO |
| EC2 + EKS need same data | **EFS** | **RWX** |
| Multiple pods need same data | EFS | RWX |
| Shared configuration | EFS | RWX |

---

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
