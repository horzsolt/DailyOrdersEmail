# Resolves OTEL_ENDPOINT on every container start so host NAT gateway changes
# do not require recreating the container. Set OTEL_ENDPOINT=auto (default) or
# omit it to auto-detect; set an explicit URL to override.

$otelPort = if ($env:OTEL_PORT) { $env:OTEL_PORT } else { "4318" }
$otelEndpoint = $env:OTEL_ENDPOINT

if ([string]::IsNullOrWhiteSpace($otelEndpoint) -or $otelEndpoint -eq "auto") {
    try {
        $gateway = (Get-NetRoute -DestinationPrefix "0.0.0.0/0" -ErrorAction Stop |
            Sort-Object RouteMetric |
            Select-Object -First 1).NextHop

        if (-not [string]::IsNullOrWhiteSpace($gateway)) {
            $env:OTEL_ENDPOINT = "http://${gateway}:${otelPort}"
            Write-Host "OTEL endpoint auto-detected: $($env:OTEL_ENDPOINT)"
        } else {
            throw "No default route found"
        }
    } catch {
        $env:OTEL_ENDPOINT = "http://localhost:${otelPort}"
        Write-Warning "OTEL gateway auto-detect failed ($($_.Exception.Message)); using $($env:OTEL_ENDPOINT)"
    }
} else {
    Write-Host "OTEL endpoint (explicit): $otelEndpoint"
}

dotnet DailyOrdersEmail.dll @args
exit $LASTEXITCODE
