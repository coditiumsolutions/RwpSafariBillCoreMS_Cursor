$ErrorActionPreference = "Stop"
$rawPath = Join-Path (Split-Path $PSScriptRoot -Parent) "db_raw.txt"
$outPath = Join-Path (Split-Path $PSScriptRoot -Parent) "db.txt"

if (-not (Test-Path $rawPath)) {
    Write-Error "Missing db_raw.txt. Run ExportSchemaSqlcmd.bat first."
}

function Format-TypeDisplay {
    param($dataType, $charLen, $prec, $scale)
    $dt = [string]$dataType
    if ($charLen -ne "NULL" -and $charLen -ne "" -and $null -ne $charLen) {
        $n = [int]$charLen
        if ($n -lt 0) { return "$dt (max)" }
        return "$dt ($n)"
    }
    if ($prec -ne "NULL" -and $prec -ne "" -and $scale -ne "NULL" -and $scale -ne "") {
        return "$dt ($prec,$scale)"
    }
    if ($prec -ne "NULL" -and $prec -ne "") {
        return "$dt ($prec)"
    }
    return $dt
}

# db_raw.txt from sqlcmd -u is UTF-16 LE (matches sqlcmd -o Unicode output)
$lines = Get-Content -LiteralPath $rawPath -Encoding Unicode
$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine("Database: BMSSafariRwp")
$null = $sb.AppendLine("")
$null = $sb.AppendLine("Connection (from appsettings.json DefaultConnection, password redacted):")
$null = $sb.AppendLine("  Server=172.20.229.2; Database=BMSSafariRwp; User Id=admin; Password=***; MultipleActiveResultSets=True; TrustServerCertificate=True;")
$null = $sb.AppendLine("")
$null = $sb.AppendLine(("Generated: {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")))
$null = $sb.AppendLine("Source: INFORMATION_SCHEMA (user base tables only)")
$null = $sb.AppendLine("")
$null = $sb.AppendLine("---")
$null = $sb.AppendLine("")

$tableCount = 0
$currentKey = ""
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $p = $line -split '\|', 8
    if ($p.Count -lt 8) { continue }
    $schema = $p[0].Trim()
    $table = $p[1].Trim()
    $col = $p[2].Trim()
    $dtype = $p[3].Trim()
    $cmax = $p[4].Trim()
    $nprec = $p[5].Trim()
    $nscale = $p[6].Trim()
    $nullable = $p[7].Trim()
    $key = "$schema.$table"
    if ($key -ne $currentKey) {
        if ($currentKey -ne "") { $null = $sb.AppendLine("") }
        $currentKey = $key
        $tableCount++
        $null = $sb.AppendLine("Table: $table  (schema: $schema)")
    }
    $typeDisp = Format-TypeDisplay $dtype $cmax $nprec $nscale
    $null = $sb.AppendLine("  - $col  |  $typeDisp  |  nullable: $nullable")
}

$null = $sb.AppendLine("")
$null = $sb.AppendLine("---")
$null = $sb.AppendLine(("Total tables: {0}" -f $tableCount))

# StreamWriter ensures UTF-8 (no BOM); WriteAllText on Windows PowerShell 5.1 can emit UTF-16
$utf8 = New-Object System.Text.UTF8Encoding $false
$sw = New-Object System.IO.StreamWriter($outPath, $false, $utf8)
try {
    $sw.Write($sb.ToString())
} finally {
    $sw.Close()
}
Write-Host "Wrote $outPath (UTF-8)"
