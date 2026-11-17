param(
    [ValidateSet('Debug', 'Release')]
    [string]$BuildConfiguration = 'Debug',
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$sorterWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sorterBuildDirectory = Join-Path $sorterWorkspace "LargeFileSort.Sorter/bin/$BuildConfiguration/net10.0"
$sorterSourceConfiguration = Join-Path $sorterWorkspace 'LargeFileSort.Sorter/appsettings.json'
$sorterSourceConfigurationHash = (Get-FileHash -LiteralPath $sorterSourceConfiguration).Hash
$sorterArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $sorterWorkspace 'artifacts'))
$sorterValidationRoot = Join-Path $sorterArtifactsRoot ('sorter-cli-' + [guid]::NewGuid().ToString('N'))
$sorterBinaryDirectory = Join-Path $sorterValidationRoot 'application'
$sorterWorkingDirectory = Join-Path $sorterValidationRoot 'working directory'
$sorterTempDirectory = Join-Path $sorterWorkingDirectory 'temporary data'
$sorterConfigurationPath = Join-Path $sorterBinaryDirectory 'appsettings.json'
$sorterInputPath = Join-Path $sorterWorkingDirectory 'input sample.txt'
$sorterOutputPath = Join-Path $sorterWorkingDirectory 'output sample.txt'
$sorterExistingOutput = Join-Path $sorterWorkingDirectory 'existing output.txt'
$sorterDefaultArguments = @('--input', 'input sample.txt', '--output', 'output sample.txt', '--temp-directory', 'temporary data')
$sorterCases = [System.Collections.Generic.List[string]]::new()
$sorterValidationSucceeded = $false

function New-SorterSettings {
    return @{
        MemoryBudgetBytes = 268435456
        MaxParallelism = 4
        MaxMergeSources = 16
        ExtraDiskSpace = 1
        ReaderBufferBytes = 65536
        WriterBufferCharacters = 65536
        Logging = @{ LogLevel = @{ Default = 'Information' } }
    }
}

function Write-SorterSettings([hashtable]$Settings) {
    $Settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sorterConfigurationPath -Encoding utf8
}

function Invoke-SorterCase {
    param(
        [string]$Name,
        [string[]]$CliArguments = $sorterDefaultArguments,
        [string]$ExpectedError = 'Sorter is not implemented yet.',
        [switch]$Help,
        [string]$AdditionalError
    )

    $configurationExistsBefore = Test-Path -LiteralPath $sorterConfigurationPath
    $configurationHashBefore = if ($configurationExistsBefore) { (Get-FileHash -LiteralPath $sorterConfigurationPath).Hash }
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.ArgumentList.Add((Join-Path $sorterBinaryDirectory 'LargeFileSort.Sorter.dll'))
    foreach ($argument in $CliArguments) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.WorkingDirectory = $sorterWorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw "$Name timed out."
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    finally { $process.Dispose() }

    if ($Help) {
        if ($exitCode -ne 0 -or -not $stdout.Contains('--input') -or $stderr.Length -ne 0) {
            throw "$Name failed: exit=$exitCode; stdout=$stdout; stderr=$stderr"
        }
    }
    elseif ($exitCode -eq 0 -or -not $stderr.Contains($ExpectedError)) {
        throw "$Name failed: exit=$exitCode; stdout=$stdout; stderr=$stderr"
    }
    if (-not $Help -and $ExpectedError -eq 'Sorter is not implemented yet.' -and $stdout.Length -ne 0) {
        throw "$Name wrote application output to stdout."
    }
    if ($AdditionalError -and -not $stderr.Contains($AdditionalError)) {
        throw "$Name is missing expected diagnostic: $AdditionalError"
    }
    if ($stderr.Contains([char]27)) { throw "$Name emitted console color escape sequences." }
    if ((Test-Path -LiteralPath $sorterConfigurationPath) -ne $configurationExistsBefore) {
        throw "$Name changed configuration existence."
    }
    if ($configurationExistsBefore -and (Get-FileHash -LiteralPath $sorterConfigurationPath).Hash -ne $configurationHashBefore) {
        throw "$Name modified configuration."
    }
    if ((Get-FileHash -LiteralPath $sorterInputPath).Hash -ne $sorterInputHash) { throw "$Name changed input." }
    if ((Get-FileHash -LiteralPath $sorterExistingOutput).Hash -ne $sorterExistingOutputHash) { throw "$Name changed existing output." }
    if (Test-Path -LiteralPath $sorterOutputPath) { throw "$Name created an output file." }
    if (@(Get-ChildItem -LiteralPath $sorterTempDirectory -Force).Count -ne 0) { throw "$Name created temporary data." }
    if (Test-Path -LiteralPath (Join-Path $sorterWorkingDirectory 'missing output parent')) { throw "$Name created an output parent." }
    if (Test-Path -LiteralPath (Join-Path $sorterWorkingDirectory 'missing temporary directory')) { throw "$Name created a temporary parent." }
    $sorterCases.Add($Name)
}

try {
    if (-not (Test-Path -LiteralPath (Join-Path $sorterBuildDirectory 'LargeFileSort.Sorter.dll'))) {
        throw 'Build LargeFileSort.Sorter before running this script.'
    }
    $null = New-Item -ItemType Directory -Path $sorterBinaryDirectory, $sorterTempDirectory -Force
    Get-ChildItem -LiteralPath $sorterBuildDirectory -Force | Copy-Item -Destination $sorterBinaryDirectory -Recurse
    "2. Banana`n1. apple`n" | Set-Content -LiteralPath $sorterInputPath -Encoding utf8 -NoNewline
    'Existing output must survive.' | Set-Content -LiteralPath $sorterExistingOutput -Encoding utf8
    '{ invalid working-directory configuration' | Set-Content -LiteralPath (Join-Path $sorterWorkingDirectory 'appsettings.json') -Encoding utf8
    $sorterInputHash = (Get-FileHash -LiteralPath $sorterInputPath).Hash
    $sorterExistingOutputHash = (Get-FileHash -LiteralPath $sorterExistingOutput).Hash

    Remove-Item -LiteralPath $sorterConfigurationPath
    Invoke-SorterCase -Name 'Help without configuration' -CliArguments @('--help') -Help
    Invoke-SorterCase -Name 'Missing configuration' -ExpectedError 'appsettings.json'
    '{ invalid JSON' | Set-Content -LiteralPath $sorterConfigurationPath -Encoding utf8
    Invoke-SorterCase -Name 'Help with malformed configuration' -CliArguments @('--help') -Help
    Invoke-SorterCase -Name 'Malformed configuration' -ExpectedError 'appsettings.json'

    Write-SorterSettings (New-SorterSettings)
    Invoke-SorterCase -Name 'Valid invocation and configuration base directory' -AdditionalError 'Configuration loaded.'
    Invoke-SorterCase -Name 'Absolute paths' -CliArguments @('--input', $sorterInputPath, '--output', $sorterOutputPath, '--temp-directory', $sorterTempDirectory)
    Invoke-SorterCase -Name 'Normalized relative paths' -CliArguments @('--input', './input sample.txt', '--output', 'temporary data/../output sample.txt', '--temp-directory', './temporary data')

    foreach ($option in @('--input', '--output', '--temp-directory')) {
        $optionIndex = [array]::IndexOf($sorterDefaultArguments, $option)
        $missingArguments = for ($index = 0; $index -lt $sorterDefaultArguments.Length; $index++) {
            if ($index -ne $optionIndex -and $index -ne $optionIndex + 1) { $sorterDefaultArguments[$index] }
        }
        Invoke-SorterCase -Name "Missing $option" -CliArguments $missingArguments -ExpectedError $option
        $emptyArguments = $sorterDefaultArguments.Clone()
        $emptyArguments[$optionIndex + 1] = ''
        Invoke-SorterCase -Name "Empty $option" -CliArguments $emptyArguments -ExpectedError 'must not be empty'
    }
    Invoke-SorterCase -Name 'No CLI overrides' -CliArguments ($sorterDefaultArguments + @('--max-parallelism', '2')) -ExpectedError '--max-parallelism'
    Invoke-SorterCase -Name 'Whitespace path' -CliArguments @('--input', ' ', '--output', 'output sample.txt', '--temp-directory', 'temporary data') -ExpectedError 'must not be empty'
    Invoke-SorterCase -Name 'Missing input' -CliArguments @('--input', 'missing.txt', '--output', 'output sample.txt', '--temp-directory', 'temporary data') -ExpectedError 'Input file does not exist'
    Invoke-SorterCase -Name 'Input is a directory' -CliArguments @('--input', 'temporary data', '--output', 'output sample.txt', '--temp-directory', 'temporary data') -ExpectedError 'Input file does not exist'
    Invoke-SorterCase -Name 'Same normalized paths' -CliArguments @('--input', 'input sample.txt', '--output', './input sample.txt', '--temp-directory', 'temporary data') -ExpectedError 'must be different'
    if ([System.OperatingSystem]::IsWindows()) {
        Invoke-SorterCase -Name 'Same paths ignoring Windows case' -CliArguments @('--input', 'input sample.txt', '--output', 'INPUT SAMPLE.TXT', '--temp-directory', 'temporary data') -ExpectedError 'must be different'
    }
    Invoke-SorterCase -Name 'Existing output file' -CliArguments @('--input', 'input sample.txt', '--output', 'existing output.txt', '--temp-directory', 'temporary data') -ExpectedError 'Output path already exists'
    Invoke-SorterCase -Name 'Existing output directory' -CliArguments @('--input', 'input sample.txt', '--output', 'temporary data', '--temp-directory', 'temporary data') -ExpectedError 'Output path already exists'
    Invoke-SorterCase -Name 'Missing output parent' -CliArguments @('--input', 'input sample.txt', '--output', 'missing output parent/result.txt', '--temp-directory', 'temporary data') -ExpectedError 'Output directory does not exist'
    Invoke-SorterCase -Name 'Missing temporary directory' -CliArguments @('--input', 'input sample.txt', '--output', 'output sample.txt', '--temp-directory', 'missing temporary directory') -ExpectedError 'Temporary directory does not exist'
    Invoke-SorterCase -Name 'Temporary path is a file' -CliArguments @('--input', 'input sample.txt', '--output', 'output sample.txt', '--temp-directory', 'input sample.txt') -ExpectedError 'Temporary directory does not exist'

    foreach ($name in @('MemoryBudgetBytes', 'MaxParallelism', 'MaxMergeSources', 'ExtraDiskSpace', 'ReaderBufferBytes', 'WriterBufferCharacters')) {
        $minimum = if ($name -eq 'MaxMergeSources') { 2 } else { 1 }
        $maximum = if ($name -eq 'MemoryBudgetBytes') { [long]::MaxValue } elseif ($name -eq 'ExtraDiskSpace') { 10 } else { [int]::MaxValue }
        $overflow = if ($name -eq 'MemoryBudgetBytes') { '9223372036854775808' } else { '2147483648' }
        $settings = New-SorterSettings
        $settings.Remove($name)
        Write-SorterSettings $settings
        Invoke-SorterCase -Name "$name missing" -ExpectedError $name
        foreach ($invalid in @(
            @{ Label = 'null'; Value = $null },
            @{ Label = 'empty'; Value = '' },
            @{ Label = 'text'; Value = 'abc' },
            @{ Label = 'fraction'; Value = 1.5 },
            @{ Label = 'boolean'; Value = $true },
            @{ Label = 'object'; Value = @{ Value = 1 } },
            @{ Label = 'array'; Value = @(1, 2) },
            @{ Label = 'below minimum'; Value = $minimum - 1 },
            @{ Label = 'negative'; Value = -1 },
            @{ Label = 'overflow'; Value = $overflow }
        )) {
            $settings = New-SorterSettings
            $settings[$name] = $invalid.Value
            Write-SorterSettings $settings
            Invoke-SorterCase -Name "$name $($invalid.Label)" -ExpectedError $name
        }
        foreach ($boundary in @(@{ Label = 'minimum'; Value = $minimum }, @{ Label = 'maximum'; Value = $maximum })) {
            $settings = New-SorterSettings
            $settings[$name] = $boundary.Value
            Write-SorterSettings $settings
            Invoke-SorterCase -Name "$name $($boundary.Label)"
        }
    }

    $settings = New-SorterSettings
    $settings.ExtraDiskSpace = 11
    Write-SorterSettings $settings
    Invoke-SorterCase -Name 'ExtraDiskSpace above maximum' -ExpectedError 'ExtraDiskSpace'
    foreach ($level in @('None', 'Debug')) {
        $settings = New-SorterSettings
        $settings.Logging.LogLevel.Default = $level
        Write-SorterSettings $settings
        $additional = if ($level -eq 'Debug') { $sorterInputPath } else { $null }
        Invoke-SorterCase -Name "Logging $level" -AdditionalError $additional
    }
    $settings = New-SorterSettings
    $settings.Logging.LogLevel.Default = 'InvalidLevel'
    Write-SorterSettings $settings
    Invoke-SorterCase -Name 'Invalid logging level' -ExpectedError 'InvalidLevel'

    if ((Get-FileHash -LiteralPath $sorterSourceConfiguration).Hash -ne $sorterSourceConfigurationHash) {
        throw 'Working source configuration was modified.'
    }
    $sorterValidationSucceeded = $true
    [pscustomobject]@{ Passed = $sorterCases.Count; BuildConfiguration = $BuildConfiguration; Cases = $sorterCases } | ConvertTo-Json -Depth 4
}
finally {
    if ($sorterValidationSucceeded -and -not $KeepArtifacts) {
        $resolvedValidationRoot = [System.IO.Path]::GetFullPath($sorterValidationRoot)
        $requiredPrefix = $sorterArtifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedValidationRoot.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected validation path: $resolvedValidationRoot"
        }
        Remove-Item -LiteralPath $resolvedValidationRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $sorterValidationRoot) {
        Write-Host "Validation files retained: $sorterValidationRoot"
    }
}
