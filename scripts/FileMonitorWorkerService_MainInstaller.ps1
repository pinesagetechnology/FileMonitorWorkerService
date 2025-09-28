# FileMonitorWorkerService - Main Installation Script
# This is the main script that orchestrates the entire installation process

param(
    [string]$InstallPath = "",
    [string]$DataPath = "",
    [string]$SourcePath = "",
    [switch]$Linux,
    [switch]$SkipDotNet,
    [switch]$SkipValidation,
    [switch]$Verbose,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
FileMonitorWorkerService - Main Installation Script

USAGE:
    Windows: .\FileMonitorWorkerService_MainInstaller.ps1 [options]
    Linux:   ./FileMonitorWorkerService_MainInstaller.sh [options]

OPTIONS:
    -InstallPath <path>    Installation directory 
                          Default: Windows: C:\FileMonitor, Linux: /opt/filemonitor
    -DataPath <path>       Data directory
                          Default: Windows: C:\FileMonitorData, Linux: /var/filemonitor
    -SourcePath <path>     Path to published application files
                          If not specified, will prompt for manual deployment
    -SkipDotNet           Skip .NET 8 installation check
    -SkipValidation       Skip post-installation validation
    -Verbose              Enable verbose output
    -Linux                Generate Linux version of this script
    -Help                 Show this help message

EXAMPLES:
    # Basic installation
    .\FileMonitorWorkerService_MainInstaller.ps1
    
    # Custom paths
    .\FileMonitorWorkerService_MainInstaller.ps1 -InstallPath "D:\Apps\FileMonitor" -DataPath "D:\Data\FileMonitor"
    
    # With application source
    .\FileMonitorWorkerService_MainInstaller.ps1 -SourcePath ".\publish"
    
    # Linux installation
    sudo ./FileMonitorWorkerService_MainInstaller.sh --install-path /opt/myapp --data-path /var/myapp

PREREQUISITES:
    Windows: PowerShell 5.1+, Administrator privileges
    Linux:   Bash, root/sudo privileges

"@
    exit 0
}

if ($Linux) {
    # Generate Linux version
    Write-Host @'
#!/usr/bin/env bash
# FileMonitorWorkerService - Main Installation Script for Linux

set -e

# Default values
INSTALL_PATH="/opt/filemonitor"
DATA_PATH="/var/filemonitor"
SOURCE_PATH=""
SKIP_DOTNET=false
SKIP_VALIDATION=false
VERBOSE=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --install-path)
            INSTALL_PATH="$2"
            shift 2
            ;;
        --data-path)
            DATA_PATH="$2"
            shift 2
            ;;
        --source-path)
            SOURCE_PATH="$2"
            shift 2
            ;;
        --skip-dotnet)
            SKIP_DOTNET=true
            shift
            ;;
        --skip-validation)
            SKIP_VALIDATION=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            echo "FileMonitorWorkerService - Main Installation Script for Linux"
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --install-path PATH    Installation directory (default: /opt/filemonitor)"
            echo "  --data-path PATH       Data directory (default: /var/filemonitor)"
            echo "  --source-path PATH     Path to published application files"
            echo "  --skip-dotnet         Skip .NET installation"
            echo "  --skip-validation     Skip post-installation validation"
            echo "  --verbose             Verbose output"
            echo "  -h, --help            Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

log_info() { echo -e "${GREEN}✓${NC} $1"; }
log_warn() { echo -e "${YELLOW}⚠${NC} $1"; }
log_error() { echo -e "${RED}✗${NC} $1"; }
log_step() { echo -e "${BLUE}==>${NC} $1"; }

echo -e "${GREEN}=== FileMonitorWorkerService Linux Installation ===${NC}"
echo "This script will install FileMonitorWorkerService on your Linux system"
echo ""
echo "Configuration:"
echo "  Install Path: $INSTALL_PATH"
echo "  Data Path: $DATA_PATH"
echo "  Source Path: ${SOURCE_PATH:-'Not specified (manual deployment)'}"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    log_error "This script must be run as root (use sudo)"
    exit 1
fi

# Step 1: Run prerequisites installation
log_step "Step 1: Installing prerequisites and setting up environment"
if [ "$SKIP_DOTNET" = true ]; then
    bash FileMonitorWorkerService_Linux_Installation.sh --install-path "$INSTALL_PATH" --data-path "$DATA_PATH" --skip-dotnet
else
    bash FileMonitorWorkerService_Linux_Installation.sh --install-path "$INSTALL_PATH" --data-path "$DATA_PATH"
fi

if [ $? -ne 0 ]; then
    log_error "Prerequisites installation failed"
    exit 1
fi

log_info "Prerequisites installation completed"

# Step 2: Deploy application if source provided
if [ -n "$SOURCE_PATH" ]; then
    log_step "Step 2: Deploying application files"
    
    if [ ! -d "$SOURCE_PATH" ]; then
        log_error "Source path not found: $SOURCE_PATH"
        exit 1
    fi
    
    if [ ! -f "$SOURCE_PATH/FileMonitorWorkerService.dll" ]; then
        log_error "Application DLL not found in source path: $SOURCE_PATH"
        log_info "Please ensure you have published the application:"
        echo "  cd path/to/FileMonitorWorkerService"
        echo "  dotnet publish -c Release -o \"$SOURCE_PATH\""
        exit 1
    fi
    
    # Copy files
    cp -r "$SOURCE_PATH"/* "$INSTALL_PATH/"
    chown -R filemonitor:filemonitor "$INSTALL_PATH"
    chmod +x "$INSTALL_PATH"/*.dll
    
    log_info "Application files deployed"
else
    log_step "Step 2: Application deployment skipped"
    log_warn "No source path provided. You need to manually deploy the application:"
    echo "  1. Publish: dotnet publish -c Release -o publish"
    echo "  2. Copy: sudo cp -r publish/* \"$INSTALL_PATH/\""
    echo "  3. Set ownership: sudo chown -R filemonitor:filemonitor \"$INSTALL_PATH\""
fi

# Step 3: Validation
if [ "$SKIP_VALIDATION" = false ]; then
    log_step "Step 3: Validating installation"
    
    # Create validation script inline
    cat > /tmp/validate-config.sh << 'EOF'
#!/bin/bash
INSTALL_PATH="$1"
DATA_PATH="$2"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_success() { echo -e "${GREEN}✓${NC} $1"; }
log_error() { echo -e "${RED}✗${NC} $1"; }
log_warn() { echo -e "${YELLOW}⚠${NC} $1"; }

errors=0

# Check .NET
if command -v dotnet &> /dev/null && dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 8"; then
    log_success ".NET 8 is installed"
else
    log_error ".NET 8 not found"
    ((errors++))
fi

# Check directories
for dir in "$INSTALL_PATH" "$DATA_PATH" "$DATA_PATH/database" "$DATA_PATH/logs" "$DATA_PATH/config"; do
    if [ -d "$dir" ]; then
        log_success "Directory exists: $dir"
    else
        log_error "Directory missing: $dir"
        ((errors++))
    fi
done

# Check application
if [ -f "$INSTALL_PATH/FileMonitorWorkerService.dll" ]; then
    log_success "Application files found"
else
    log_warn "Application files not found (manual deployment needed)"
fi

# Check service
if systemctl is-enabled filemonitor &>/dev/null; then
    log_success "Service is configured"
else
    log_error "Service not configured"
    ((errors++))
fi

exit $errors
EOF

    chmod +x /tmp/validate-config.sh
    
    if /tmp/validate-config.sh "$INSTALL_PATH" "$DATA_PATH"; then
        log_info "Validation passed"
    else
        log_warn "Validation found issues - please review and fix"
    fi
    
    rm /tmp/validate-config.sh
else
    log_step "Step 3: Validation skipped"
fi

# Final instructions
echo ""
echo -e "${GREEN}=== Installation Summary ===${NC}"
echo "Install Path: $INSTALL_PATH"
echo "Data Path: $DATA_PATH"
echo "Service: filemonitor"
echo ""
echo -e "${BLUE}Next Steps:${NC}"

if [ -f "$INSTALL_PATH/FileMonitorWorkerService.dll" ]; then
    echo "1. Configure Azure Storage connection in: $INSTALL_PATH/appsettings.json"
    echo "2. Add data source configurations for folder monitoring"
    echo "3. Start the service: sudo systemctl start filemonitor"
    echo "4. Enable auto-start: sudo systemctl enable filemonitor"
    echo "5. Check status: sudo systemctl status filemonitor"
    echo "6. View logs: sudo journalctl -u filemonitor -f"
else
    echo "1. Deploy application files to: $INSTALL_PATH"
    echo "2. Configure Azure Storage connection"
    echo "3. Add data source configurations"
    echo "4. Start the service"
fi

echo ""
echo "For detailed instructions: $INSTALL_PATH/DEPLOYMENT_GUIDE.txt"
'@
    exit 0
}

# Windows PowerShell version starts here
$ErrorActionPreference = "Stop"

# Set default paths for Windows
if (!$InstallPath) { $InstallPath = "C:\FileMonitor" }
if (!$DataPath) { $DataPath = "C:\FileMonitorData" }

Write-Host "=== FileMonitorWorkerService Windows Installation ===" -ForegroundColor Green
Write-Host "This script will install FileMonitorWorkerService on your Windows system" -ForegroundColor White
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Install Path: $InstallPath"
Write-Host "  Data Path: $DataPath"
Write-Host "  Source Path: $(if ($SourcePath) { $SourcePath } else { 'Not specified (manual deployment)' })"
Write-Host ""

# Function to check if running as administrator
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Check administrator privileges
if (!(Test-Administrator)) {
    Write-Error "This script must be run as Administrator. Please run PowerShell as Administrator and try again."
    exit 1
}

Write-Host "✓ Running as Administrator" -ForegroundColor Green

try {
    # Step 1: Run prerequisites installation
    Write-Host ""
    Write-Host "==> Step 1: Installing prerequisites and setting up environment" -ForegroundColor Blue
    
    $installArgs = @("-InstallPath", $InstallPath, "-DataPath", $DataPath)
    if ($SkipDotNet) { $installArgs += "-SkipDotNet" }
    if ($Verbose) { $installArgs += "-Verbose" }
    
    & ".\FileMonitorWorkerService_Windows_Installation.ps1" @installArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Prerequisites installation failed"
    }
    
    Write-Host "✓ Prerequisites installation completed" -ForegroundColor Green

    # Step 2: Deploy application if source provided
    Write-Host ""
    Write-Host "==> Step 2: Deploying application files" -ForegroundColor Blue
    
    if ($SourcePath) {
        if (!(Test-Path $SourcePath)) {
            throw "Source path not found: $SourcePath"
        }
        
        if (!(Test-Path "$SourcePath\FileMonitorWorkerService.dll")) {
            Write-Error "Application DLL not found in source path: $SourcePath"
            Write-Host "Please ensure you have published the application:" -ForegroundColor Yellow
            Write-Host "  cd path\to\FileMonitorWorkerService"
            Write-Host "  dotnet publish -c Release -o `"$SourcePath`""
            exit 1
        }
        
        # Copy files
        Copy-Item "$SourcePath\*" $InstallPath -Recurse -Force
        Write-Host "✓ Application files deployed" -ForegroundColor Green
    } else {
        Write-Host "⚠ Application deployment skipped" -ForegroundColor Yellow
        Write-Host "No source path provided. You need to manually deploy the application:" -ForegroundColor Yellow
        Write-Host "  1. Publish: dotnet publish -c Release -o publish"
        Write-Host "  2. Copy: Copy-Item publish\* `"$InstallPath`" -Recurse -Force"
    }

    # Step 3: Validation
    if (!$SkipValidation) {
        Write-Host ""
        Write-Host "==> Step 3: Validating installation" -ForegroundColor Blue
        
        # Basic validation checks
        $validationErrors = 0
        
        # Check .NET
        try {
            $dotnetInfo = & dotnet --list-runtimes 2>$null
            if ($dotnetInfo -like "*Microsoft.AspNetCore.App 8.*") {
                Write-Host "✓ .NET 8 is installed" -ForegroundColor Green
            } else {
                Write-Host "✗ .NET 8 not found" -ForegroundColor Red
                $validationErrors++
            }
        } catch {
            Write-Host "✗ .NET 8 not found" -ForegroundColor Red
            $validationErrors++
        }
        
        # Check directories
        $requiredDirs = @($InstallPath, $DataPath, "$DataPath\database", "$DataPath\logs", "$DataPath\config")
        foreach ($dir in $requiredDirs) {
            if (Test-Path $dir) {
                Write-Host "✓ Directory exists: $dir" -ForegroundColor Green
            } else {
                Write-Host "✗ Directory missing: $dir" -ForegroundColor Red
                $validationErrors++
            }
        }
        
        # Check application
        if (Test-Path "$InstallPath\FileMonitorWorkerService.dll") {
            Write-Host "✓ Application files found" -ForegroundColor Green
        } else {
            Write-Host "⚠ Application files not found (manual deployment needed)" -ForegroundColor Yellow
        }
        
        if ($validationErrors -eq 0) {
            Write-Host "✓ Validation passed" -ForegroundColor Green
        } else {
            Write-Host "⚠ Validation found $validationErrors issues - please review and fix" -ForegroundColor Yellow
        }
    } else {
        Write-Host ""
        Write-Host "==> Step 3: Validation skipped" -ForegroundColor Blue
    }

    # Final instructions
    Write-Host ""
    Write-Host "=== Installation Summary ===" -ForegroundColor Green
    Write-Host "Install Path: $InstallPath" -ForegroundColor Yellow
    Write-Host "Data Path: $DataPath" -ForegroundColor Yellow
    Write-Host "Service: FileMonitorWorkerService" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    
    if (Test-Path "$InstallPath\FileMonitorWorkerService.dll") {
        Write-Host "1. Configure Azure Storage connection in: $InstallPath\appsettings.json"
        Write-Host "2. Add data source configurations for folder monitoring"
        Write-Host "3. Start the service: sc start FileMonitorWorkerService"
        Write-Host "4. Check status: sc query FileMonitorWorkerService"
        Write-Host "5. View logs in: $InstallPath\logs\"
        Write-Host "6. Monitor service logs in Event Viewer"
    } else {
        Write-Host "1. Deploy application files to: $InstallPath"
        Write-Host "2. Configure Azure Storage connection"
        Write-Host "3. Add data source configurations"
        Write-Host "4. Start the service or run manually"
    }

    Write-Host ""
    Write-Host "For detailed instructions, see: $InstallPath\DEPLOYMENT_GUIDE.txt"

} catch {
    Write-Host ""
    Write-Host "Installation failed: $_" -ForegroundColor Red
    Write-Host "Please check the error messages above and try again." -ForegroundColor Yellow
    exit 1
}
