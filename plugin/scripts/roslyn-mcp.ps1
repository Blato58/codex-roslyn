param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $ToolArgs
)

$ErrorActionPreference = "Stop"

function Write-LauncherStatus {
    param([string] $Message)

    [Console]::Error.WriteLine("[dotnet-semantic-tools] $Message")
}

function Get-RoslynMcpCommand {
    $command = Get-Command dotnet-roslyn-mcp -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $userProfile = [Environment]::GetFolderPath("UserProfile")
    if ([string]::IsNullOrWhiteSpace($userProfile)) {
        return $null
    }

    $toolNames = @("dotnet-roslyn-mcp.exe", "dotnet-roslyn-mcp")
    foreach ($toolName in $toolNames) {
        $candidate = Join-Path $userProfile ".dotnet/tools/$toolName"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

if ($ToolArgs.Count -eq 0) {
    $ToolArgs = @("serve", "--stdio")
}

$toolPath = Get-RoslynMcpCommand
if ($null -eq $toolPath) {
    Write-LauncherStatus "dotnet-roslyn-mcp was not found; installing the global .NET tool."

    $installOutput = & dotnet tool install -g Codex.Roslyn.Mcp.Tool 2>&1
    $installExitCode = $LASTEXITCODE
    foreach ($line in $installOutput) {
        [Console]::Error.WriteLine($line)
    }

    if ($installExitCode -ne 0) {
        exit $installExitCode
    }

    $toolPath = Get-RoslynMcpCommand
    if ($null -eq $toolPath) {
        Write-LauncherStatus "dotnet-roslyn-mcp installed, but the command could not be resolved."
        exit 127
    }
}

& $toolPath @ToolArgs
exit $LASTEXITCODE
