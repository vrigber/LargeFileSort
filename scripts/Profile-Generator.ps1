[CmdletBinding()]
param([switch]$ValidateOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$perfView = Join-Path $workspace 'tools\PerfView.exe'
$releaseDirectory = Join-Path $workspace 'LargeFileSort.Generator\bin\Release\net10.0'
$application = Join-Path $releaseDirectory 'LargeFileSort.Generator.exe'
$applicationDll = Join-Path $releaseDirectory 'LargeFileSort.Generator.dll'
$configurationPath = Join-Path $releaseDirectory 'appsettings.json'
$sourceConfigurationPath = Join-Path $workspace 'LargeFileSort.Generator\appsettings.json'
$dictionary = Join-Path $workspace 'data\phrases.txt'
$maximumBytes = [long]1000000000
$seed = 42

foreach ($path in @($perfView, $application, $applicationDll, $configurationPath, $sourceConfigurationPath, $dictionary))
{
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "Required file not found: $path. Build Release before profiling."
    }
}

$configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
if ($configuration.WriterBufferCharacters -ne 65536)
{
    throw 'This profiling run requires WriterBufferCharacters = 65536. Rebuild Release after changing the configuration.'
}
if ($configuration.MaxDictionaryBytes -lt (Get-Item -LiteralPath $dictionary).Length)
{
    throw 'The dictionary exceeds MaxDictionaryBytes.'
}
if ((Get-FileHash -LiteralPath $configurationPath).Hash -ne (Get-FileHash -LiteralPath $sourceConfigurationPath).Hash)
{
    throw 'Release configuration differs from the working configuration. Rebuild Release.'
}

Write-Host "PerfView: $((Get-Item -LiteralPath $perfView).VersionInfo.FileVersion)"
Write-Host "Release application: $application"
Write-Host "Dictionary: $dictionary"
Write-Host "Seed: $seed; maximum bytes: $maximumBytes; writer buffer: 65536 characters"
if ($ValidateOnly)
{
    Write-Host 'Preparation validated. Neither PerfView nor the generator was started.'
    return
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try
{
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
    {
        throw 'Open PowerShell as administrator and run this script again. ETW collection requires elevation.'
    }
}
finally
{
    $identity.Dispose()
}

$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $workspace ('output\profiling\' + $runId)
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$tracePath = Join-Path $runDirectory 'generator.etl'
$archivePath = $tracePath + '.zip'
$logPath = Join-Path $runDirectory 'perfview.log'
$outputPath = Join-Path $runDirectory 'generated.txt'
$metadataPath = Join-Path $runDirectory 'run.json'
$utf8 = [Text.UTF8Encoding]::new($false)
$sessionName = 'LargeFileSort-' + $runId
$arguments = @(
    '/NoGui', '/NoView', '/AcceptEULA', '/Zip:true',
    '/KernelEvents:Default', '/ClrEvents:Default', '/ClrEventLevel:Verbose',
    ('/SessionName:' + $sessionName),
    ('/DataFile:' + $tracePath), ('/LogFile:' + $logPath),
    'run', $application,
    '--dictionary', $dictionary, '--output', $outputPath,
    '--max-bytes', $maximumBytes.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--seed', $seed.ToString([Globalization.CultureInfo]::InvariantCulture)
)

$os = Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber
$cpu = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors)
$memory = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
$disk = $null
try
{
    $driveLetter = [IO.Path]::GetPathRoot($runDirectory).Substring(0, 1)
    $disk = @(Get-Partition -DriveLetter $driveLetter | Get-Disk |
        Select-Object FriendlyName, BusType, Size)
}
catch
{
    $disk = $_.Exception.Message
}

$metadata = [ordered]@{
    State = 'Prepared'
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    EndedUtc = $null
    Workspace = $workspace
    Application = $application
    ApplicationSHA256 = (Get-FileHash -LiteralPath $applicationDll).Hash
    PerfViewVersion = (Get-Item -LiteralPath $perfView).VersionInfo.FileVersion
    PerfViewSHA256 = (Get-FileHash -LiteralPath $perfView).Hash
    Dictionary = $dictionary
    DictionaryBytes = (Get-Item -LiteralPath $dictionary).Length
    DictionarySHA256 = (Get-FileHash -LiteralPath $dictionary).Hash
    Seed = $seed
    MaxOutputBytes = $maximumBytes
    Configuration = $configuration
    OS = $os
    CPU = $cpu
    TotalMemoryBytes = $memory
    OutputDisk = $disk
    InstalledRuntimes = @(& dotnet --list-runtimes)
    GitRevision = (& git -C $workspace rev-parse HEAD | Out-String).Trim()
    GitStatus = @(& git -C $workspace status --short)
    PerfViewArguments = $arguments
    SessionName = $sessionName
    PerfViewExitCode = $null
    ActualOutputBytes = $null
    Error = $null
}
[IO.File]::WriteAllText($metadataPath, ($metadata | ConvertTo-Json -Depth 8), $utf8)
Copy-Item -LiteralPath $configurationPath -Destination (Join-Path $runDirectory 'appsettings.snapshot.json')
$symbolsDirectory = Join-Path $runDirectory 'symbols'
New-Item -ItemType Directory -Path $symbolsDirectory | Out-Null
Copy-Item -LiteralPath $applicationDll -Destination $symbolsDirectory
$pdb = [IO.Path]::ChangeExtension($applicationDll, '.pdb')
if (Test-Path -LiteralPath $pdb)
{
    Copy-Item -LiteralPath $pdb -Destination $symbolsDirectory
}
Copy-Item -LiteralPath (Join-Path $releaseDirectory 'LargeFileSort.Generator.deps.json'),
    (Join-Path $releaseDirectory 'LargeFileSort.Generator.runtimeconfig.json') -Destination $symbolsDirectory

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $perfView
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.WorkingDirectory = $workspace
$startInfo.Arguments = ($arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo

Write-Host "Collecting one profiled run. Artifacts: $runDirectory"
try
{
    $metadata.State = 'Collecting'
    if (-not $process.Start()) { throw 'PerfView did not start.' }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $metadata.PerfViewExitCode = $process.ExitCode
    [IO.File]::WriteAllText((Join-Path $runDirectory 'collector.stdout.txt'), $stdout.GetAwaiter().GetResult(), $utf8)
    [IO.File]::WriteAllText((Join-Path $runDirectory 'collector.stderr.txt'), $stderr.GetAwaiter().GetResult(), $utf8)
    if ($process.ExitCode -ne 0) { throw "PerfView failed with exit code $($process.ExitCode). See $logPath" }
    if (-not (Test-Path -LiteralPath $archivePath)) { throw "Trace archive not found. See $logPath" }
    $log = [IO.File]::ReadAllText($logPath)
    if ($log -match 'Command exited with non-success|Command did not exit, killing')
    {
        throw "The generator did not finish successfully. See $logPath"
    }
    $reportedBytes = [regex]::Match($log, '(?m)^\s*Actual bytes:\s*([0-9]+)\s*$')
    if (-not $reportedBytes.Success -or -not (Test-Path -LiteralPath $outputPath))
    {
        throw "Generator success diagnostics or output file missing. See $logPath"
    }
    $metadata.ActualOutputBytes = (Get-Item -LiteralPath $outputPath).Length
    if ($metadata.ActualOutputBytes -le 0 -or $metadata.ActualOutputBytes -gt $maximumBytes -or
        $metadata.ActualOutputBytes -ne [long]$reportedBytes.Groups[1].Value)
    {
        throw 'Generated file size does not match the expected limit and diagnostics.'
    }
    if ($metadata.DictionarySHA256 -ne (Get-FileHash -LiteralPath $dictionary).Hash)
    {
        throw 'The dictionary changed during the profiling run.'
    }
    $metadata.State = 'Collected'
    Write-Host "Collection complete: $archivePath"
    Write-Host "Generator output and diagnostics: $logPath"
}
catch
{
    $metadata.State = 'Failed'
    $metadata.Error = $_.Exception.Message
    throw
}
finally
{
    $metadata.EndedUtc = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText($metadataPath, ($metadata | ConvertTo-Json -Depth 8), $utf8)
    $process.Dispose()
}
