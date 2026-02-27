$csvPath = "Avalonia/bin/Debug/net9.0/latency_stats.csv"

if (-not (Test-Path $csvPath)) {
    Write-Error "Arquivo CSV não encontrado em $csvPath"
    exit 1
}

$lines = Get-Content $csvPath | Select-Object -Skip 1 | Where-Object { $_ -match "\d" }
$values = @()

foreach ($line in $lines) {
    $parts = $line -split ","
    if ($parts.Count -ge 2) {
        # Se o CSV foi gerado com vírgula decimal sem aspas (Ex: Tick,0,1234), vai ter 3 partes
        # O Tick é a parte 0. O atraso é o resto.
        $latStr = $parts[1..($parts.Count-1)] -join "."
        
        # Removemos qualquer vírgula ou ponto extra que tenha sobrado e normalizamos para ponto
        $latStr = $latStr -replace ",", "."
        
        $val = $latStr -as [double]
        if ($val -ne $null) {
            $values += $val
        }
    }
}

$count = $values.Count
if ($count -eq 0) {
    Write-Warning "Nenhum dado numérico válido encontrado."
    exit
}

# --- Cálculos Estatísticos Manuais ---

# Média
$sum = 0
$min = [double]::MaxValue
$max = [double]::MinValue

foreach ($v in $values) {
    $sum += $v
    if ($v -lt $min) { $min = $v }
    if ($v -gt $max) { $max = $v }
}
$avg = $sum / $count

# Desvio Padrão
$sumSqDiff = 0
foreach ($v in $values) {
    $diff = $v - $avg
    $sumSqDiff += $diff * $diff
}
$stdDev = [Math]::Sqrt($sumSqDiff / $count)

# Ordenar para Percentis
$sorted = $values | Sort-Object

function Get-Percentile ($sortedData, $percentile) {
    $idx = [Math]::Floor($sortedData.Count * $percentile)
    if ($idx -ge $sortedData.Count) { $idx = $sortedData.Count - 1 }
    return $sortedData[$idx]
}

$p50 = Get-Percentile $sorted 0.50
$p95 = Get-Percentile $sorted 0.95
$p99 = Get-Percentile $sorted 0.99

# --- Relatório ---

Write-Host "--------------------------------------------------"
Write-Host "ANÁLISE DE LATÊNCIA (ms) - Base: $count amostras"
Write-Host "--------------------------------------------------"
Write-Host ("Mínimo:  {0:F4} ms" -f $min)
Write-Host ("Máximo:  {0:F4} ms" -f $max)
Write-Host ("Média:   {0:F4} ms" -f $avg)
Write-Host ("Jitter:  {0:F4} ms (StdDev)" -f $stdDev)
Write-Host "--------------------------------------------------"
Write-Host ("P50 (Mediana): {0:F4} ms" -f $p50)
Write-Host ("P95:           {0:F4} ms" -f $p95)
Write-Host ("P99:           {0:F4} ms" -f $p99)
Write-Host "--------------------------------------------------"
