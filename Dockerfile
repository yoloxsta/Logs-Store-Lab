# Build stage
FROM --platform=$BUILDPLATFORM golang:1.21-alpine AS builder

WORKDIR /app

# Copy go mod files
COPY go.mod ./

# Copy source code first for go mod tidy
COPY main.go ./

# Download dependencies (go.sum will be generated)
RUN go mod tidy && go mod download

# Build the application for ARM64
ARG TARGETPLATFORM
ARG BUILDPLATFORM
RUN CGO_ENABLED=0 GOOS=linux GOARCH=arm64 go build -a -installsuffix cgo -o lab-logs-collector .

# Runtime stage
FROM alpine:latest

RUN apk --no-cache add ca-certificates

WORKDIR /root/

# Copy binary from builder
COPY --from=builder /app/lab-logs-collector .

# Create PV mount directory
RUN mkdir -p /pv-logs

# Set environment variables
ENV POD_NAMESPACE=default
ENV POD_NAME=lab-logs-collector

# Run the binary
CMD ["./lab-logs-collector"]
