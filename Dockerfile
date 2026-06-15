# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /app

# Copy project file and restore
COPY LabLogsCollector.csproj ./
RUN dotnet restore

# Copy source and build
COPY Program.cs ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine

WORKDIR /app

# Create PV mount directory
RUN mkdir -p /pv-logs

# Copy binary from builder
COPY --from=builder /app/out .

# Set environment variables
ENV POD_NAMESPACE=music-uat
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# Run the application
ENTRYPOINT ["dotnet", "LabLogsCollector.dll"]
