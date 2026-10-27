$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$destination = Join-Path $sourceRoot 'dist'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$sourceNames = @('TrayCore.cs', 'BridgeClient.cs', 'TrayApp.cs', 'SelfTests.cs', 'AiClient.cs', 'AiSettings.cs', 'ReviewForm.cs', 'Design.cs', 'InboxForm.cs', 'ApiSettingsForm.cs', 'AiTests.cs', 'FormulaCatalog.cs', 'DownloadMonitor.cs', 'FormulaTests.cs', 'TopicTests.cs', 'MaterialThemes.cs', 'DualClassification.cs', 'DualTests.cs', 'PromptRouting.cs', 'PromptTests.cs', 'MaterialHistoryForm.cs', 'AssemblyInfo.cs')
$sourceFiles = $sourceNames | ForEach-Object { Join-Path $sourceRoot $_ }
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/codepage:65001', '/optimize+',
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $sourceRoot 'App.ico')),
    ('/out:' + (Join-Path $destination 'PaperInbox.exe')),
    ('/resource:' + (Join-Path $sourceRoot 'formula-catalog.json') + ',PaperInbox.formula-catalog.json'),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll', '/r:System.Security.dll', '/r:System.Web.Extensions.dll', '/r:System.Net.Http.dll') + $sourceFiles
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = Join-Path $destination 'PaperInbox-Zotero-Bridge-0.5.0.xpi'
if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package }
$archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'zotero-bridge') -File | ForEach-Object {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $_.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Copy-Item -LiteralPath (Join-Path $sourceRoot '使用说明.txt') -Destination $destination
Write-Output ('Built files: ' + $destination)
