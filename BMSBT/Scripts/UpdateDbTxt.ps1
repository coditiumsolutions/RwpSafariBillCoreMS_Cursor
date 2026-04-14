# Update db.txt with current database schema from connection string (appsettings.json)
$appsettingsPath = Join-Path (Join-Path $PSScriptRoot "..") "appsettings.json"
$json = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
$connStr = $json.ConnectionStrings.DefaultConnection
# Mask password for display in db.txt
$connStrMasked = $connStr -replace 'Password=[^;]+', 'Password=***'
$dbName = if ($connStr -match 'Database=([^;]+)') { $matches[1] } else { 'BMSSafariRwp' }

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$conn.Open()

# Get table types (TABLE vs VIEW)
$cmdTypes = $conn.CreateCommand()
$cmdTypes.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_CATALOG = '$dbName' AND TABLE_TYPE IN ('BASE TABLE','VIEW') ORDER BY TABLE_TYPE, TABLE_NAME"
$types = @{}
$rd = $cmdTypes.ExecuteReader()
while ($rd.Read()) {
    $key = $rd["TABLE_SCHEMA"].ToString() + "." + $rd["TABLE_NAME"].ToString()
    $types[$key] = $rd["TABLE_TYPE"].ToString()
}
$rd.Close()

# Get columns
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_CATALOG = '$dbName'
ORDER BY TABLE_NAME, ORDINAL_POSITION
"@
$reader = $cmd.ExecuteReader()

$byTable = @{}
while ($reader.Read()) {
    $schema = $reader["TABLE_SCHEMA"].ToString()
    $tname = $reader["TABLE_NAME"].ToString()
    $key = $schema + "." + $tname
    $col = $reader["COLUMN_NAME"].ToString()
    $dtype = $reader["DATA_TYPE"].ToString()
    $maxLen = $reader["CHARACTER_MAXIMUM_LENGTH"]
    $numPrec = $reader["NUMERIC_PRECISION"]
    $typeStr = $dtype
    if ($maxLen -is [int] -and $maxLen -gt 0) { $typeStr += " " + $maxLen }
    elseif ($maxLen -eq -1) { $typeStr += " max" }
    elseif ($numPrec -is [int] -and $numPrec -gt 0 -and $dtype -match 'decimal|numeric') { $typeStr += " " + $numPrec }
    if (-not $byTable[$key]) { $byTable[$key] = @() }
    $byTable[$key] += @{ Name = $col; Type = $typeStr }
}
$reader.Close()
$conn.Close()

# Build output
$lines = @()
$lines += "Database: $dbName"
$lines += "Connection: $connStrMasked"
$lines += "(From appsettings: DefaultConnection)"
$lines += ("Generated: {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
$lines += ""
$lines += "---"
$lines += ""
$lines += "Tables and columns (from INFORMATION_SCHEMA.COLUMNS):"
$lines += ""

$sortedKeys = $byTable.Keys | Sort-Object
foreach ($key in $sortedKeys) {
    $parts = $key.Split('.')
    $tname = $parts[-1]
    $ttype = $types[$key]
    $prefix = if ($ttype -eq 'VIEW') { "View" } else { "Table" }
    $lines += "$prefix`: $tname"
    foreach ($col in $byTable[$key]) {
        $lines += "  - $($col.Name) ($($col.Type))"
    }
    $lines += ""
}

$lines += "---"
$lines += ("Total tables/views: {0}" -f $byTable.Count)

$outPath = Join-Path (Join-Path $PSScriptRoot "..") "db.txt"
$lines | Set-Content -Path $outPath -Encoding UTF8
Write-Host "Updated $outPath with $($byTable.Count) tables/views."
