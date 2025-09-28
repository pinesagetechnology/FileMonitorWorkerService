# FileMonitorWorkerService - Windows Installation Script
# This script installs prerequisites and sets up the FileMonitorWorkerService on Windows

param(
    [string]$InstallPath = "C:\FileMonitor",
    [string]$DataPath = "C:\FileMonitorData",
    [switch]$SkipDotNet,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

# Set verbose preference
if ($Verbose) {
    $VerbosePreference = "Continue"
}

Write-Host "=== FileMonitorWorkerService Windows Installation ===" -ForegroundColor Green
Write-Host "Install Path: $InstallPath" -ForegroundColor Yellow
Write-Host "Data Path: $DataPath" -ForegroundColor Yellow

# Function to check if running as administrator
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Function to check .NET 8 installation
function Test-DotNet8 {
    try {
        $dotnetInfo = & dotnet --list-runtimes 2>$null
        if ($dotnetInfo -like "*Microsoft.AspNetCore.App 8.*") {
            Write-Host "✓ .NET 8 ASP.NET Core Runtime found" -ForegroundColor Green
            return $true
        }
        return $false
    }
    catch {
        return $false
    }
}

# Function to install .NET 8
function Install-DotNet8 {
    Write-Host "Installing .NET 8 Runtime..." -ForegroundColor Yellow
    
    $downloadUrl = "https://download.microsoft.com/download/8/4/8/848f28ae-78ab-4661-8ebe-765312c38565/dotnet-hosting-8.0.0-win.exe"
    $installerPath = "$env:TEMP\dotnet-hosting-8.0.0-win.exe"
    
    try {
        Write-Host "Downloading .NET 8 Hosting Bundle..."
        Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath -UseBasicParsing
        
        Write-Host "Installing .NET 8..."
        Start-Process -FilePath $installerPath -ArgumentList "/quiet" -Wait
        
        Remove-Item $installerPath -Force
        
        # Verify installation
        if (Test-DotNet8) {
            Write-Host "✓ .NET 8 installed successfully" -ForegroundColor Green
        } else {
            throw "Failed to verify .NET 8 installation"
        }
    }
    catch {
        Write-Error "Failed to install .NET 8: $_"
        exit 1
    }
}

# Function to create directories
function New-DirectoryStructure {
    param([string]$BasePath, [string]$DataBasePath)
    
    Write-Host "Creating directory structure..." -ForegroundColor Yellow
    
    $directories = @(
        $BasePath,
        "$BasePath\logs",
        "$DataBasePath",
        "$DataBasePath\database",
        "$DataBasePath\logs",
        "$DataBasePath\config",
        "$DataBasePath\temp"
    )
    
    foreach ($dir in $directories) {
        if (!(Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            Write-Verbose "Created directory: $dir"
        }
    }
    
    Write-Host "✓ Directory structure created" -ForegroundColor Green
}

# Function to update configuration files
function Update-ConfigurationFiles {
    param([string]$AppPath, [string]$DataBasePath)
    
    Write-Host "Updating configuration files..." -ForegroundColor Yellow
    
    $configFiles = @(
        "$AppPath\appsettings.json",
        "$AppPath\appsettings.Development.json"
    )
    
    $dbPath = "$DataBasePath\database\filemonitor.db".Replace('\', '\\')
    
    foreach ($configFile in $configFiles) {
        if (Test-Path $configFile) {
            Write-Verbose "Updating $configFile"
            
            $content = Get-Content $configFile -Raw
            
            # Update database path
            $content = $content -replace '"Data Source=.*?"', "`"Data Source=$dbPath`""
            
            Set-Content -Path $configFile -Value $content -Encoding UTF8
            Write-Host "✓ Updated $configFile" -ForegroundColor Green
        }
    }
}

# Function to set up Windows service
function Install-WindowsService {
    param([string]$AppPath, [string]$ServiceName = "FileMonitorWorkerService")
    
    Write-Host "Setting up Windows Service..." -ForegroundColor Yellow
    
    # Check if service already exists
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "Service $ServiceName already exists. Stopping..." -ForegroundColor Yellow
        try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch {}
        if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
            try { Remove-Service -Name $ServiceName -ErrorAction SilentlyContinue } catch {}
        } else {
            sc.exe delete $ServiceName | Out-Null
        }
    }
    
    # Create service using dotnet host for the DLL
    $dotnetPath = (Get-Command dotnet).Source
    $dllPath = Join-Path $AppPath 'FileMonitorWorkerService.dll'
    $binaryPath = '"' + $dotnetPath + '" ' + '"' + $dllPath + '"'

    $serviceArgs = @{
        Name = $ServiceName
        BinaryPathName = $binaryPath
        DisplayName = "FileMonitorWorkerService"
        Description = "FileMonitorWorkerService - File monitoring and Azure upload service"
        StartupType = "Automatic"
    }
    
    New-Service @serviceArgs
    Write-Host "✓ Windows Service created: $ServiceName" -ForegroundColor Green
}

# Function to create startup script
function New-StartupScript {
    param([string]$AppPath)
    
    $startScript = @"
@echo off
cd /d "$AppPath"
echo Starting FileMonitorWorkerService...
dotnet FileMonitorWorkerService.dll
pause
"@
    
    Set-Content -Path "$AppPath\start.bat" -Value $startScript
    Write-Host "✓ Startup script created: $AppPath\start.bat" -ForegroundColor Green
}

# Function to set permissions
function Set-DirectoryPermissions {
    param([string]$Path)
    
    Write-Host "Setting directory permissions..." -ForegroundColor Yellow
    
    try {
        # Give IIS_IUSRS and NETWORK SERVICE full access to data directories
        $acl = Get-Acl $Path
        
        $accessRules = @(
            @{ Identity = "IIS_IUSRS"; Rights = "FullControl" },
            @{ Identity = "NETWORK SERVICE"; Rights = "FullControl" },
            @{ Identity = "Users"; Rights = "ReadAndExecute" }
        )
        
        foreach ($rule in $accessRules) {
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $rule.Identity, $rule.Rights, "ContainerInherit,ObjectInherit", "None", "Allow"
            )
            $acl.SetAccessRule($accessRule)
        }
        
        Set-Acl -Path $Path -AclObject $acl
        Write-Host "✓ Permissions set for $Path" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not set permissions for $Path`: $_"
    }
}

# Function to create deployment guide
function New-DeploymentGuide {
    param([string]$AppPath, [string]$DataPath)
    
    $guideContent = @"
FileMonitorWorkerService - Deployment Guide

1) Publish application on a build machine:
   dotnet publish -c Release -o publish

2) Copy published files to the target machine:
   Copy-Item publish\* `"$AppPath`" -Recurse -Force

3) Configure application:
   - Update database connection in appsettings.json
   - Database path is set to: $DataPath\database\filemonitor.db
   - Configure Azure Storage connection strings
   - Add data source configurations for folder monitoring

4) Manage Windows service:
   sc start FileMonitorWorkerService
   sc query FileMonitorWorkerService
   sc stop FileMonitorWorkerService

5) Monitor logs:
   - Application logs: $AppPath\logs\
   - Data logs: $DataPath\logs\

6) Configuration:
   - Database: $DataPath\database\filemonitor.db
   - Logs: $DataPath\logs\
   - Configuration: $DataPath\config\
"@
    
    Set-Content -Path "$AppPath\DEPLOYMENT_GUIDE.txt" -Value $guideContent
    Write-Host "✓ Deployment guide created: $AppPath\DEPLOYMENT_GUIDE.txt" -ForegroundColor Green
}

# Main installation process
try {
    # Check administrator privileges
    if (!(Test-Administrator)) {
        Write-Error "This script must be run as Administrator. Please run PowerShell as Administrator and try again."
        exit 1
    }
    
    Write-Host "✓ Running as Administrator" -ForegroundColor Green
    
    # Check and install .NET 8
    if (!$SkipDotNet) {
        if (!(Test-DotNet8)) {
            Install-DotNet8
        } else {
            Write-Host "✓ .NET 8 is already installed" -ForegroundColor Green
        }
    }
    
    # Create directory structure
    New-DirectoryStructure -BasePath $InstallPath -DataBasePath $DataPath
    
    # Check if application files exist
    if (!(Test-Path "$InstallPath\FileMonitorWorkerService.dll")) {
        Write-Host "Application files not found. Please copy the published application to $InstallPath" -ForegroundColor Red
        Write-Host "You can publish the application using: dotnet publish -c Release -o `"$InstallPath`"" -ForegroundColor Yellow
        Write-Host ""
        New-DeploymentGuide -AppPath $InstallPath -DataPath $DataPath
        exit 0
    }
    
    # Update configuration files
    Update-ConfigurationFiles -AppPath $InstallPath -DataBasePath $DataPath
    
    # Set permissions
    Set-DirectoryPermissions -Path $DataPath
    Set-DirectoryPermissions -Path $InstallPath
    
    # Create startup script
    New-StartupScript -AppPath $InstallPath
    
    # Create deployment guide
    New-DeploymentGuide -AppPath $InstallPath -DataPath $DataPath
    
    # Install Windows service (optional)
    $installService = Read-Host "Do you want to install as Windows Service? (y/n) [default: y]"
    if ($installService -eq "" -or $installService -eq "y") {
        Install-WindowsService -AppPath $InstallPath
    }
    
    Write-Host ""
    Write-Host "=== Installation Complete ===" -ForegroundColor Green
    Write-Host "Application Path: $InstallPath" -ForegroundColor Yellow
    Write-Host "Database Path: $DataPath\database\filemonitor.db" -ForegroundColor Yellow
    Write-Host "Logs: $InstallPath\logs\" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "1. Configure Azure Storage connection string in appsettings.json"
    Write-Host "2. Add data source configurations for folder monitoring"
    Write-Host "3. Start the service or run manually using start.bat"
    Write-Host "4. Monitor logs in the logs directories"
    Write-Host ""
    Write-Host "For detailed instructions, see: $InstallPath\DEPLOYMENT_GUIDE.txt"
    
}
catch {
    Write-Error "Installation failed: $_"
    exit 1
}
