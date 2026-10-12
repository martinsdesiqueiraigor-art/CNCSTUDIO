# Gera um programa G-code grande (padrao: 10 MB) para testar desempenho do CncStudio.
# Repete o corpo do samples/EXEMPLO.nc ate atingir o tamanho pedido.
#
# Uso (PowerShell, na pasta do projeto):
#   .\tools\gerar-arquivo-grande.ps1
#   .\tools\gerar-arquivo-grande.ps1 -TamanhoMB 20 -Saida C:\temp\TESTE.nc
#
# Se o Windows bloquear scripts, rode antes (vale so para esta janela):
#   Set-ExecutionPolicy -Scope Process Bypass

param(
    [int]$TamanhoMB = 10,
    [string]$Saida = "GRANDE.nc"
)

$raiz = Split-Path -Parent $PSScriptRoot
$exemplo = Join-Path $raiz "samples/EXEMPLO.nc"

# Corpo do programa sem cabecalho (%, O....) e sem o M30 final
$corpo = Get-Content $exemplo | Where-Object { $_ -notmatch '^%' -and $_ -notmatch '^O\d' -and $_ -notmatch 'M30' }

$texto = [System.Text.StringBuilder]::new()
[void]$texto.AppendLine('%')
[void]$texto.AppendLine('O9999 (ARQUIVO GRANDE DE TESTE - GERADO AUTOMATICAMENTE)')

$alvo = $TamanhoMB * 1MB
while ($texto.Length -lt $alvo) {
    foreach ($linha in $corpo) { [void]$texto.AppendLine($linha) }
}

[void]$texto.AppendLine('M30')
[void]$texto.AppendLine('%')

if (-not [System.IO.Path]::IsPathRooted($Saida)) {
    $Saida = Join-Path (Get-Location).Path $Saida
}

[System.IO.File]::WriteAllText($Saida, $texto.ToString())
$mb = [math]::Round((Get-Item $Saida).Length / 1MB, 1)
Write-Host "Gerado: $Saida ($mb MB)"
