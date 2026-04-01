$process = New-Object System.Diagnostics.Process
$process.StartInfo.FileName = "dotnet"
$process.StartInfo.Arguments = "run"
$process.StartInfo.UseShellExecute = $false
$process.StartInfo.RedirectStandardOutput = $true
$process.StartInfo.WorkingDirectory = (Get-Location).Path
$process.Start() | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()

while ($sw.ElapsedMilliseconds -lt 30000 -and !$process.HasExited) {
    $output = $process.StandardOutput.ReadLine()
    if ($null -ne $output) {
        $output
    }
}

$process.Kill()
Write-Host "=== Process killed after 10 seconds ==="
