# Install-Service.ps1
param(
    [string]$Name = "Daraban.Agent",
    [string]$BinPath = "$PWD/Daraban.Agent.Service.exe",
    [string]$DisplayName = "Daraban Agent",
    [string]$Description = "Cross-platform Daraban Agent written by Daraban"
)

# Check if already installed, if so, stop and remove
if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $Name -Force
    Start-Sleep -Seconds 2
    Write-Host "Removing existing service..."
    sc.exe delete $Name
    Start-Sleep -Seconds 2
}

# Install the new service (runs as LocalSystem by default)
Write-Host "Installing service..."
New-Service -Name $Name -BinaryPathName $BinPath -DisplayName $DisplayName -Description $Description -StartupType Automatic

Write-Host "Starting service..."
Start-Service -Name $Name

Write-Host "Service '$Name' installed and started successfully!"