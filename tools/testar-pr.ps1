# Testar PR: baixa o .exe de teste da Prévia de um PR e abre (FLUXO-01 PR 2).
# Só mexe na pasta %LOCALAPPDATA%\CasillasTeste. Nunca toca no repositório.
$ErrorActionPreference = 'Continue'  # os códigos de saída do gh são conferidos à mão, sem erro vermelho do PowerShell

# Nome do artefato e do executável: vêm de .github/workflows/previa.yml (job "windows") e do AssemblyName do App.
$PrefixoArtefato = 'CasillasCode-teste-'
$Executavel = 'CasillasCode.exe'
$Raiz = Join-Path $env:LOCALAPPDATA 'CasillasTeste'

function Falha([string]$msg) {
    Write-Host ''
    Write-Host "  $msg" -ForegroundColor Yellow
    Write-Host ''
    exit 1
}

# 1. gh instalado e com login
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Falha 'O "gh" (GitHub CLI) não está instalado. Instale em https://cli.github.com e rode de novo.'
}
$null = & gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Falha 'O "gh" não está com login. Rode "gh auth login" no terminal e tente de novo.'
}

# 2. Número do PR
$entrada = Read-Host 'Número do PR'
if ($entrada -notmatch '^\s*#?(\d+)\s*$') { Falha 'Digite só o número do PR, por exemplo 178.' }
$numero = [int]$Matches[1]

# 3. Título e commit
$json = & gh pr view $numero --json 'title,headRefName,headRefOid,state' 2>$null
if ($LASTEXITCODE -ne 0 -or -not $json) { Falha "O PR $numero não existe neste repositório (ou o gh não está dentro dele)." }
$pr = $json | ConvertFrom-Json
$curto = $pr.headRefOid.Substring(0, 7)
Write-Host ''
Write-Host "  PR $numero : $($pr.title)"
Write-Host "  Ramo      : $($pr.headRefName)"
Write-Host "  Commit    : $curto"
Write-Host ''

# 4. Última execução da Prévia deste ramo
$lista = & gh run list --workflow previa.yml --branch $pr.headRefName --limit 20 --json 'databaseId,status,conclusion,headSha' | ConvertFrom-Json
if (-not $lista -or $lista.Count -eq 0) { Falha 'Este ramo ainda não tem execução da Prévia. Dê um push ou rode a Prévia no GitHub.' }
$doCommit = @($lista | Where-Object { $_.headSha -eq $pr.headRefOid })
if ($doCommit.Count -eq 0) { Falha "A Prévia ainda não começou para o commit $curto. Espere um pouco e tente de novo." }
if ($doCommit | Where-Object { $_.status -ne 'completed' }) {
    if (-not ($doCommit | Where-Object { $_.conclusion -eq 'success' })) {
        Falha 'A Prévia deste commit ainda está rodando. Tente de novo em alguns minutos.'
    }
}
$ok = $doCommit | Where-Object { $_.conclusion -eq 'success' } | Select-Object -First 1
if (-not $ok) { Falha "A Prévia do commit $curto falhou. Veja os testes no PR. Sem executável para baixar." }

# 5. Baixar para uma pasta fora do repositório (substitui a anterior deste PR)
$destino = Join-Path $Raiz "pr-$numero"
$raizCompleta = [IO.Path]::GetFullPath($Raiz).TrimEnd('\') + '\'
$destinoCompleto = [IO.Path]::GetFullPath($destino)
if (-not $destinoCompleto.StartsWith($raizCompleta, [StringComparison]::OrdinalIgnoreCase) -or $destinoCompleto -eq $raizCompleta) {
    Falha 'Caminho de destino inesperado. Nada foi apagado.'
}
if (Test-Path -LiteralPath $destinoCompleto) { Remove-Item -LiteralPath $destinoCompleto -Recurse -Force }
New-Item -ItemType Directory -Path $destinoCompleto -Force | Out-Null

$nomeArtefato = $PrefixoArtefato + $pr.headRefName
Write-Host '  Baixando o executável de teste...'
$null = & gh run download $ok.databaseId --name $nomeArtefato --dir $destinoCompleto 2>&1
if ($LASTEXITCODE -ne 0) {
    Remove-Item -LiteralPath $destinoCompleto -Recurse -Force -ErrorAction SilentlyContinue
    Falha 'Não consegui baixar o artefato. Ele pode ter vencido (a Prévia guarda 14 dias). Dê um novo push no ramo para gerar outro.'
}

# 6. Abrir
$exe = Join-Path $destinoCompleto $Executavel
if (-not (Test-Path -LiteralPath $exe)) { Falha "Baixei, mas não achei $Executavel em $destinoCompleto." }
Write-Host "  Abrindo $Executavel (PR $numero, commit $curto)."
Start-Process -FilePath $exe -WorkingDirectory $destinoCompleto
