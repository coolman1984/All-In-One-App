function Get-BundledFfmpeg([string]$Root) {
    $source = Join-Path $Root 'tools\bundle\ffmpeg.exe'
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        $command = Get-Command ffmpeg.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $command) { throw 'Place a standalone Windows x64 ffmpeg.exe in tools\bundle or add it to PATH before building.' }
        $source = $command.Source
    }
    $shim = [IO.Path]::ChangeExtension($source, '.shim')
    if (Test-Path -LiteralPath $shim -PathType Leaf) {
        $match = Select-String -LiteralPath $shim -Pattern '^\s*path\s*=\s*"?([^"\r\n]+)"?\s*$' | Select-Object -First 1
        if (-not $match) { throw "Cannot resolve the FFmpeg shim: $shim" }
        $source = $match.Matches[0].Groups[1].Value.Trim()
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "FFmpeg file is missing: $source" }

    $stream = [IO.File]::OpenRead($source)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 1MB -or $reader.ReadUInt16() -ne 0x5a4d) { throw "FFmpeg must be a standalone Windows executable: $source" }
        $stream.Position = 0x3c
        $stream.Position = $reader.ReadUInt32()
        if ($stream.Position -gt ($stream.Length - 6) -or $reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw "FFmpeg must be a Windows x64 executable: $source"
        }
    } finally { $reader.Dispose() }

    $cache = Join-Path $Root 'build-cache'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $archive = Join-Path $cache "ffmpeg-v1-$hash.exe.gz"
    if (Test-Path -LiteralPath $archive -PathType Leaf) { return $archive }

    $stage = Join-Path $cache ('ffmpeg-stage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        $probe = Join-Path $stage 'ffmpeg.exe'
        Copy-Item -LiteralPath $source -Destination $probe
        if ((Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash -ne $hash) { throw 'FFmpeg changed during the build. Run the build again.' }
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $probe
        $info.WorkingDirectory = $stage
        $info.Arguments = '-hide_banner -encoders'
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $info.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory
        $process = [Diagnostics.Process]::Start($info)
        try {
            $output = $process.StandardOutput.ReadToEndAsync()
            $errors = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit(); throw 'FFmpeg verification timed out.' }
            if ($process.ExitCode -ne 0 -or $output.Result -notmatch '\blibx264\b' -or $output.Result -notmatch '\baac\b') {
                throw ('Use standalone FFmpeg with H.264 and AAC encoders. ' + $errors.Result)
            }
        } finally { $process.Dispose() }

        Write-Host 'Packing standalone FFmpeg into the executable...'
        $temp = Join-Path $stage 'ffmpeg.exe.gz'
        $inputFile = [IO.File]::OpenRead($probe)
        try {
            $outputFile = [IO.File]::Create($temp)
            try {
                $gzip = New-Object IO.Compression.GZipStream($outputFile, [IO.Compression.CompressionLevel]::Optimal)
                try { $inputFile.CopyTo($gzip) } finally { $gzip.Dispose() }
            } finally { $outputFile.Dispose() }
        } finally { $inputFile.Dispose() }
        Move-Item -LiteralPath $temp -Destination $archive -Force
        return $archive
    } finally { Remove-Item -LiteralPath $stage -Recurse -Force }
}
