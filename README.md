# Lab Logs Collector

Collects pod logs every 15 minutes and stores on Persistent Volume.

## Details
- **EKS**: UATMuzic (Mumbai)
- **Namespace**: music-uat
- **Storage**: ebs-sc (10Gi)
- **Interval**: 15 minutes

## Deploy

```bash
# Update kubeconfig
aws eks update-kubeconfig --name UATMuzic --region ap-south-1

# Build and push ARM64 image (EKS nodes are ARM)
AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
aws ecr get-login-password --region ap-south-1 | docker login --username AWS --password-stdin ${AWS_ACCOUNT_ID}.dkr.ecr.ap-south-1.amazonaws.com
docker buildx build --platform linux/arm64 -t ${AWS_ACCOUNT_ID}.dkr.ecr.ap-south-1.amazonaws.com/hello-world:pvlab --push .

# Update deployment.yaml with your account ID
sed -i "s|<AWS_ACCOUNT_ID>|${AWS_ACCOUNT_ID}|g" k8s/deployment.yaml

# Deploy
kubectl apply -f k8s/pvc.yaml
kubectl apply -f k8s/deployment.yaml
```

## Verify

```bash
kubectl logs -f -n music-uat -l app=lab-logs-collector
kubectl exec -n music-uat deploy/lab-logs-collector -- ls -la /pv-logs
```

## GitHub Actions

Add secrets to your repository:
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`

Push to main branch to trigger deployment.
