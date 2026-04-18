$ErrorActionPreference = 'Stop'
# Script lives in BMSBT/scripts → repo root is two levels up
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appPath = Join-Path $repoRoot 'BMSBT\appsettings.json'
if (-not (Test-Path $appPath)) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $appPath = Join-Path $repoRoot 'appsettings.json'
}

$json = Get-Content $appPath -Raw
if ($json -notmatch '"DefaultConnection"\s*:\s*"([^"]+)"') {
    throw "DefaultConnection not found in $appPath"
}
$connStr = $Matches[1]
$masked = $connStr -replace 'Password=[^;]+', 'Password=***'
$dbMatch = if ($connStr -match 'Database=([^;]+)') { $Matches[1] } else { 'unknown' }

Add-Type -AssemblyName System.Data

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()
$sql = @"
SELECT c.TABLE_SCHEMA, c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS c
INNER JOIN INFORMATION_SCHEMA.TABLES t ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
WHERE t.TABLE_TYPE = 'BASE TABLE'
ORDER BY c.TABLE_SCHEMA, c.TABLE_NAME, c.ORDINAL_POSITION
"@
$cmd = $conn.CreateCommand()
$cmd.CommandText = $sql
$reader = $cmd.ExecuteReader()
$rows = New-Object System.Collections.Generic.List[object]
while ($reader.Read()) {
    $charLen = if ($reader.IsDBNull(4)) { $null } else { $reader.GetInt32(4) }
    $numPrec = if ($reader.IsDBNull(5)) { $null } else { [int]$reader.GetValue(5) }
    $numScale = if ($reader.IsDBNull(6)) { $null } else { $reader.GetInt32(6) }
    $rows.Add([pscustomobject]@{
        TABLE_SCHEMA = $reader.GetString(0)
        TABLE_NAME   = $reader.GetString(1)
        COLUMN_NAME  = $reader.GetString(2)
        DATA_TYPE    = $reader.GetString(3)
        CHARACTER_MAXIMUM_LENGTH = $charLen
        NUMERIC_PRECISION = $numPrec
        NUMERIC_SCALE = $numScale
    })
}
$reader.Close()
$conn.Close()

function Format-ColType($r) {
    $dt = $r.DATA_TYPE
    if ($null -ne $r.CHARACTER_MAXIMUM_LENGTH) {
        $l = [int]$r.CHARACTER_MAXIMUM_LENGTH
        if ($l -lt 0) { return "$dt max" }
        $stringTypes = 'nvarchar','nchar','varchar','char','binary','varbinary'
        if ($stringTypes -contains $dt) { return "$dt $l" }
    }
    if ($dt -eq 'decimal' -or $dt -eq 'numeric') {
        if ($null -ne $r.NUMERIC_PRECISION -and $null -ne $r.NUMERIC_SCALE) {
            return "$dt($($r.NUMERIC_PRECISION),$($r.NUMERIC_SCALE))"
        }
        return $dt
    }
    return $dt
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("Database: $dbMatch")
[void]$sb.AppendLine("Connection: $masked")
[void]$sb.AppendLine('(From appsettings: DefaultConnection)')
[void]$sb.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('---')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Tables and columns (from INFORMATION_SCHEMA.COLUMNS):')
[void]$sb.AppendLine('')

$currentKey = $null
foreach ($r in $rows) {
    $label = if ($r.TABLE_SCHEMA -eq 'dbo') { $r.TABLE_NAME } else { "$($r.TABLE_SCHEMA).$($r.TABLE_NAME)" }
    $key = "$($r.TABLE_SCHEMA).$($r.TABLE_NAME)"
    if ($key -ne $currentKey) {
        if ($null -ne $currentKey) { [void]$sb.AppendLine('') }
        [void]$sb.AppendLine("Table: $label")
        $currentKey = $key
    }
    $typ = Format-ColType $r
    [void]$sb.AppendLine("  - $($r.COLUMN_NAME) ($typ)")
}

$outPath = Join-Path $repoRoot 'BMSBT\db.txt'
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$tableCount = ($rows | Group-Object { $_.TABLE_SCHEMA + '.' + $_.TABLE_NAME }).Count
Write-Host "Wrote $($rows.Count) columns across $tableCount tables to $outPath"
