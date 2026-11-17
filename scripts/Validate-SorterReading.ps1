param([switch]$KeepArtifacts)

$ErrorActionPreference = 'Stop'
$sorterWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sorterArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $sorterWorkspace 'artifacts'))
$sorterValidationRoot = Join-Path $sorterArtifactsRoot ('sorter-reading-' + [guid]::NewGuid().ToString('N'))
$sorterValidationSucceeded = $false

try {
    $null = New-Item -ItemType Directory -Path $sorterValidationRoot -Force
    $sorterSources = @(
        'LargeFileSort.Sorter/Abstractions/ILineReader.cs',
        'LargeFileSort.Sorter/Models/SortRecord.cs',
        'LargeFileSort.Sorter/Reading/Utf8LineReader.cs',
        'LargeFileSort.Sorter/Reading/RecordParser.cs',
        'LargeFileSort.Sorter/Comparison/SortRecordComparer.cs',
        'scripts/Validate-SorterReading.cs'
    )
    $sorterCompileItems = foreach ($sorterSource in $sorterSources) {
        $sorterSourcePath = [System.Security.SecurityElement]::Escape((Join-Path $sorterWorkspace $sorterSource))
        "    <Compile Include=`"$sorterSourcePath`" />"
    }
    $sorterProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <UseSharedCompilation>false</UseSharedCompilation>
  </PropertyGroup>
  <ItemGroup>
$($sorterCompileItems -join [Environment]::NewLine)
  </ItemGroup>
</Project>
"@
    $sorterProjectPath = Join-Path $sorterValidationRoot 'Validate.csproj'
    Set-Content -LiteralPath $sorterProjectPath -Value $sorterProject -Encoding utf8
    & dotnet run --project $sorterProjectPath --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw "Reading validation failed with exit code $LASTEXITCODE." }
    $sorterValidationSucceeded = $true
}
finally {
    if ($sorterValidationSucceeded -and -not $KeepArtifacts) {
        $sorterResolvedRoot = [System.IO.Path]::GetFullPath($sorterValidationRoot)
        $sorterRequiredPrefix = $sorterArtifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $sorterResolvedRoot.StartsWith($sorterRequiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected validation path: $sorterResolvedRoot"
        }
        Remove-Item -LiteralPath $sorterResolvedRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $sorterValidationRoot) {
        Write-Host "Validation files retained: $sorterValidationRoot"
    }
}
