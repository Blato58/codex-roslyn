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
    Write-LauncherStatus "dotnet-roslyn-mcp was not found."
    Write-LauncherStatus "Install the required global .NET tool, then retry:"
    Write-LauncherStatus "dotnet tool install -g Blato58.RoslynMcp"
    exit 127
}

& $toolPath @ToolArgs
exit $LASTEXITCODE
