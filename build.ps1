<#
.SYNOPSIS
    A script that provides simple entry points for bootstrapping, building and testing.
.DESCRIPTION
    A script to make it easy to bootstrap, build and run tests.
.EXAMPLE
    PS > .\build.ps1 -Bootstrap
    Check and install prerequisites for the build.
.EXAMPLE
    PS > .\build.ps1 -Configuration Release -Framework net461
    Build the main module with 'Release' configuration and targeting 'net461'.
.EXAMPLE
    PS > .\build.ps1
    Build the main module with the default configuration (Debug) and the default target framework (determined by the current session).
.EXAMPLE
    PS > .\build.ps1 -Test
    Run xUnit tests with the default configuration (Debug) and the default target framework (determined by the current session).
.PARAMETER Clean
    Clean the local repo, but keep untracked files.
.PARAMETER Bootstrap
    Check and install the build prerequisites.
.PARAMETER Test
    Run tests.
.PARAMETER Configuration
    The configuration setting for the build. The default value is 'Debug'.
.PARAMETER Framework
    The target framework for the build.
    When not specified, the target framework is determined by the current PowerShell session:
    - If the current session is PowerShell Core, then use 'netcoreapp2.1' as the default target framework.
    - If the current session is Windows PowerShell, then use 'net461' as the default target framework.
#>
[CmdletBinding()]
param(
    [switch]
    $Clean,

    [switch]
    $Bootstrap,

    [switch]
    $Test,

    [ValidateSet("Debug", "Release")]
    [string]
    $Configuration = "Debug",

    [ValidateSet("net461", "netcoreapp2.1")]
    [string]
    $Framework
)

# Clean step
if ($Clean) {
    try {
        Push-Location $PSScriptRoot
        git clean -fdX
        return
    } finally {
        Pop-Location
    }
}

Import-Module "$PSScriptRoot/tools/helper.psm1"

if ($Bootstrap) {
    Write-Log "Validate and install missing prerequisits for building ..."

    Install-Dotnet
    return
}

function Invoke-Build
{
    param (
        [string] $Configuration,
        [string] $Framework
    )

    $sourcePath = Join-Path $PSScriptRoot PSThreadJob
    Push-Location $sourcePath
    try {
        Write-Log "Building PSThreadJob binary..."
        dotnet publish --configuration $Configuration --framework $Framework --output bin

        Write-Log "Create Signed signing destination directory..."
        $signedPath  = Join-Path . "bin\$Configuration\Signed"
        if (! (Test-Path $signedPath))
        {
            $null = New-Item -Path $signedPath -ItemType Directory
        }

        Write-Log "Creating PSThreadJob signing source directory..."
        $destPath = Join-Path . "bin\$Configuration\PSThreadJob"
        if (! (Test-Path $destPath))
        {
            $null = New-Item -Path $destPath -ItemType Directory
        }

        Write-Log "Copying ThreadJob.psd1 file for signing to $destPath..."
        $psd1FilePath = Join-Path . ThreadJob.psd1
        Copy-Item -Path $psd1FilePath -Destination $destPath -Force

        Write-Log "Copying Microsoft.PowerShell.ThreadJob.dll file for signing to $destPath..."
        $binFilePath = Join-Path . "bin\$Configuration\$Framework\Microsoft.PowerShell.ThreadJob.dll"
        Copy-Item -Path $binFilePath -Destination $destPath -Force
    }
    finally {
        Pop-Location
    }
}

# Build/Test step
# $buildTask = if ($Test) { "RunTests" } else { "ZipRelease" }

$arguments = @{ Configuration = $Configuration }
if ($Framework) { $arguments.Add("Framework", $Framework) }
Invoke-Build @arguments
