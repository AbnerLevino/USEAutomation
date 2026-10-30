<#
.SYNOPSIS
  Roteiro MANUAL da fase E: executor elevado e aviso do administrador (UAC) de verdade.

.DESCRIPTION
  Parte 1 é automática e NÃO precisa de administrador: confere que o executor real recusa pedidos inválidos
  e que, sem elevação, não mexe no serviço de impressão.

  Parte 2 PRECISA de alguém que saiba a senha de um administrador (o aviso do Windows pede as credenciais,
  este programa nunca as vê). Ela PARA o serviço de impressão do computador, deixa você testar a janela,
  e no fim GARANTE que o serviço volta a funcionar (try/finally), pedindo outro aviso do Windows se preciso.
  Enquanto o roteiro roda, este computador fica sem imprimir. Não rode com impressão em andamento.

  Passo A: janela SEM a opção de desenvolvimento. Deve recusar sozinha, sem aviso do Windows, porque a pasta
           de desenvolvimento pode ser alterada por usuário comum.
  Passo B: janela COM AUTOMAXION_DEV_ELEVATED=1. Recuse o aviso uma vez (deve aparecer "tentar de novo"),
           tente de novo e aprove com as credenciais de administrador.

.EXAMPLE
  powershell -File tools\Test-Elevacao.ps1
  powershell -File tools\Test-Elevacao.ps1 -SoParte1
#>
[CmdletBinding()]
param([switch]$SoParte1)

$repo = Split-Path -Parent $PSScriptRoot
$executor = Join-Path $repo 'Host.Elevated\bin\Debug\net10.0-windows\AutomationPlatform.Host.Elevated.exe'
$janela = Join-Path $repo 'Host.Desktop\bin\Debug\net10.0-windows\AutomationPlatform.Host.Desktop.exe'

function Titulo($t) { Write-Host "`n################ $t" -ForegroundColor Cyan }
function Estado-Spooler { (Get-Service Spooler).Status }

foreach ($arquivo in @($executor, $janela)) {
    if (-not (Test-Path $arquivo)) { Write-Host "Não achei '$arquivo'. Rode 'dotnet build' antes." -ForegroundColor Red; exit 1 }
}

# ------------------------------------------------------------------ Parte 1
Titulo 'PARTE 1: o executor real recusa o que é inválido e não age sem elevação (sem administrador)'
$antes = Estado-Spooler
Write-Host "Serviço de impressão antes: $antes"
$casos = @(
    @{ Nome = 'sem argumentos';                       Args = @();                                                                 Esperado = 20 },
    @{ Nome = 'ação fora da lista (system.format)';   Args = @('--action', 'system.format');                                       Esperado = 20 },
    @{ Nome = 'injeção de comando na ação';           Args = @('--action', 'printing.spooler.restart & calc');                    Esperado = 20 },
    @{ Nome = 'parâmetro que a ação não aceita';      Args = @('--action', 'printing.spooler.restart', '--printer', 'X');         Esperado = 20 },
    @{ Nome = 'flag desconhecida';                    Args = @('--shell', 'cmd');                                                 Esperado = 20 },
    @{ Nome = 'ação válida, mas SEM elevação';        Args = @('--action', 'printing.spooler.restart');                           Esperado = 22 }
)
$falhas = 0
foreach ($caso in $casos) {
    & $executor @($caso.Args) | Out-Null
    $ok = $LASTEXITCODE -eq $caso.Esperado
    if (-not $ok) { $falhas++ }
    '{0,-40} saída={1,2} esperado={2,2}  {3}' -f $caso.Nome, $LASTEXITCODE, $caso.Esperado, $(if ($ok) { 'OK' } else { 'FALHOU' })
}
$depois = Estado-Spooler
Write-Host "Serviço de impressão depois: $depois"
if ($depois -ne $antes) { Write-Host 'FALHOU: o serviço mudou de estado sem elevação!' -ForegroundColor Red; $falhas++ }
if ($falhas -eq 0) { Write-Host 'PARTE 1: tudo certo.' -ForegroundColor Green } else { Write-Host "PARTE 1: $falhas falha(s)." -ForegroundColor Red }
if ($SoParte1) { exit $falhas }

# ------------------------------------------------------------------ Parte 2
Titulo 'PARTE 2: aviso do administrador de verdade (precisa de credenciais de administrador)'
Write-Host 'Isto PARA o serviço de impressão deste computador e o religa no fim.'
$resposta = Read-Host 'Você tem as credenciais de um administrador e quer continuar? (s/N)'
if ($resposta -ne 's') { Write-Host 'Parte 2 não executada.'; exit $falhas }

try {
    Write-Host "`nVai aparecer o aviso do Windows para PARAR o serviço de impressão (é só a preparação do teste)."
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', 'Stop-Service Spooler -Force'
    Write-Host "Serviço de impressão agora: $(Estado-Spooler)  (precisa estar Stopped para o teste)"
    if ((Estado-Spooler) -eq 'Running') { Write-Host 'O serviço não parou (aviso recusado?). Encerrando.'; exit 1 }

    Titulo 'Passo A: janela SEM a opção de desenvolvimento'
    Write-Host 'Na janela: clique em "Impressora não imprime" e confirme "Sim, pode fazer".'
    Write-Host 'ESPERADO: mensagem dizendo que o executor está num lugar que um usuário comum consegue alterar,'
    Write-Host '          e NENHUM aviso do Windows. Depois feche a janela.'
    Remove-Item Env:AUTOMAXION_DEV_ELEVATED -ErrorAction SilentlyContinue
    Start-Process $janela -Wait
    Write-Host "Serviço de impressão agora: $(Estado-Spooler)  (deve continuar Stopped)"

    Titulo 'Passo B: janela COM AUTOMAXION_DEV_ELEVATED=1'
    Write-Host 'Na janela: clique em "Impressora não imprime" e confirme "Sim, pode fazer".'
    Write-Host '1) No aviso do Windows, RECUSE (Não/Cancelar).  ESPERADO: "Para continuar é preciso a aprovação de um'
    Write-Host '   administrador. Nada foi alterado." e a pergunta "Quer tentar de novo?".'
    Write-Host '2) Responda que quer tentar de novo e APROVE com as credenciais de administrador.'
    Write-Host '   ESPERADO: "Pronto! O serviço de impressão voltou a funcionar."'
    Write-Host 'Depois conclua o atendimento e feche a janela.'
    $env:AUTOMAXION_DEV_ELEVATED = '1'
    Start-Process $janela -Wait
    Remove-Item Env:AUTOMAXION_DEV_ELEVATED -ErrorAction SilentlyContinue
    $final = Estado-Spooler
    if ($final -eq 'Running') { Write-Host "Serviço de impressão agora: $final  -> a automação reiniciou o serviço." -ForegroundColor Green }
    else { Write-Host "Serviço de impressão agora: $final  -> NÃO foi reiniciado pela automação." -ForegroundColor Yellow }
}
finally {
    if ((Estado-Spooler) -ne 'Running') {
        Titulo 'LIMPEZA: religando o serviço de impressão'
        Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', 'Start-Service Spooler'
        Write-Host "Serviço de impressão: $(Estado-Spooler)"
    }
}
