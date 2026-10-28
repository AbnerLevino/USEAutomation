<#
.SYNOPSIS
  Validação com impressoras FALSAS no Windows real. Cria filas de teste, provoca problemas,
  roda o programa, mostra o que o Windows disse e o que o programa respondeu, e LIMPA tudo.

.DESCRIPTION
  O programa (Host.Cli) continua somente leitura: quem altera o sistema aqui é este roteiro, que
  existe só para verificação independente (PowerShell não é o motor do produto).

  Cria:  fila 'TESTE Pausada' (porta LPT1:) e fila 'TESTE Rede Inalcancavel' (porta TCP/IP 10.255.255.1,
         IP que não existe). Usa o driver embutido 'Microsoft Print To PDF'. Não precisa de administrador.
  Mexe somente em filas cujo nome começa com 'TESTE ' e na porta de teste. Nunca em outras impressoras.
  Limpa sempre ao terminar (try/finally). A porta de teste pode não sair até reiniciar o spooler;
  isso é inofensivo e o roteiro a reaproveita na próxima execução.

.PARAMETER Cenarios
  Pausada, Offline, Trabalhos. Padrão: todos.

.EXAMPLE
  powershell -File tools\Test-FilasFalsas.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Pausada', 'Offline', 'Trabalhos')]
    [string[]]$Cenarios = @('Pausada', 'Offline', 'Trabalhos')
)

$repo = Split-Path -Parent $PSScriptRoot
$portaTeste = 'IP_10.255.255.1'
$filaPausada = 'TESTE Pausada'
$filaRede = 'TESTE Rede Inalcancavel'
$driver = 'Microsoft Print To PDF'

function Titulo($texto) { Write-Host "`n################ $texto" -ForegroundColor Cyan }

function Estado-Windows {
    Write-Host '--- O que o WINDOWS diz (verificação independente):' -ForegroundColor Yellow
    Get-CimInstance Win32_Printer |
        Select-Object Name, PortName, WorkOffline, ExtendedPrinterStatus, DetectedErrorState, Default |
        Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    $jobs = Get-CimInstance Win32_PrintJob | Select-Object Name, StatusMask
    if ($jobs) { $jobs | Format-Table -AutoSize | Out-String -Width 200 | Write-Host } else { Write-Host '(nenhum trabalho)' }
}

function Rodar-Programa($id) {
    Write-Host "`n>>> run $id" -ForegroundColor Green
    $antes = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet run --no-build --project (Join-Path $repo 'Host.Cli') -- run $id 2>&1 | ForEach-Object { "    $_" }
    "    [código de saída: $LASTEXITCODE]"
    $ErrorActionPreference = $antes
}

function Limpar {
    Titulo 'LIMPEZA'
    foreach ($nome in @($filaPausada, $filaRede)) {
        if (Get-Printer -Name $nome -ErrorAction SilentlyContinue) {
            Get-PrintJob -PrinterName $nome -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue
            Remove-Printer -Name $nome -ErrorAction SilentlyContinue
            Write-Host "removida a fila: $nome"
        }
    }
    Start-Sleep -Seconds 2
    if (Get-PrinterPort -Name $portaTeste -ErrorAction SilentlyContinue) {
        try { Remove-PrinterPort -Name $portaTeste -ErrorAction Stop; Write-Host "removida a porta: $portaTeste" }
        catch { Write-Host "AVISO: a porta de teste '$portaTeste' não saiu (o spooler a segura até reiniciar). É inofensiva e será reaproveitada." -ForegroundColor DarkYellow }
    }
    $padrao = Get-CimInstance Win32_Printer | Where-Object Default | Select-Object -ExpandProperty Name
    Write-Host "impressora padrão agora: $padrao"
    $restantes = @(Get-Printer | Where-Object Name -like 'TESTE *').Count
    Write-Host "filas de teste restantes: $restantes (esperado 0)"
}

# Segurança: recusa rodar se já existir uma fila 'TESTE *' que não seja nossa conhecida.
$estranhas = Get-Printer | Where-Object { $_.Name -like 'TESTE *' -and $_.Name -notin @($filaPausada, $filaRede) }
if ($estranhas) { throw "Existem filas 'TESTE *' desconhecidas: $($estranhas.Name -join ', '). Nada foi feito." }

Write-Host 'Compilando o programa (sem alterar nada no sistema)...'
& dotnet build (Join-Path $repo 'AutomationPlatform.sln') --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }

try {
    $ErrorActionPreference = 'Stop'
    Titulo 'PREPARAÇÃO: criando filas falsas'
    if (-not (Get-PrinterPort -Name $portaTeste -ErrorAction SilentlyContinue)) {
        Add-PrinterPort -Name $portaTeste -PrinterHostAddress '10.255.255.1'
    }
    Add-Printer -Name $filaPausada -DriverName $driver -PortName 'LPT1:'
    Add-Printer -Name $filaRede -DriverName $driver -PortName $portaTeste
    Estado-Windows
    Rodar-Programa 'printing.port'
    Rodar-Programa 'printing.default-printer'

    if ('Pausada' -in $Cenarios) {
        Titulo "CENÁRIO: fila pausada ($filaPausada)"
        $p = Get-CimInstance Win32_Printer -Filter "Name='$filaPausada'"
        $r = Invoke-CimMethod -InputObject $p -MethodName Pause
        Write-Host "Pause() retornou $($r.ReturnValue) (0 = sucesso)"
        Estado-Windows
        Rodar-Programa 'printing.printer-state'
        Rodar-Programa 'printing.not-printing'
    }

    if ('Trabalhos' -in $Cenarios) {
        Titulo 'CENÁRIO: trabalho dentro de fila pausada'
        'Texto de teste' | Out-Printer -Name $filaPausada
        Start-Sleep -Seconds 2
        Estado-Windows
        Rodar-Programa 'printing.queue'
        Rodar-Programa 'printing.not-printing'
    }

    if ('Offline' -in $Cenarios) {
        Titulo "CENÁRIO: fila de rede marcada offline ($filaRede)"
        $n = Get-CimInstance Win32_Printer -Filter "Name='$filaRede'"
        Set-CimInstance -InputObject $n -Property @{ WorkOffline = $true }
        Estado-Windows
        Rodar-Programa 'printing.printer-state'
        Rodar-Programa 'printing.full'
    }
}
finally {
    $ErrorActionPreference = 'Continue'
    Limpar
}
