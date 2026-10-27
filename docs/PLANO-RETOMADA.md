# AutomationPlatform — Prompt de retomada (cole no Claude Code do outro computador)

> Gerado em 2026-10-27 a partir do estado real do repositório (branch `main`, commit `29bbcd4`, 40 testes aprovados).

## 0. Como você deve se comportar

Atue como engenheiro de software sênior, arquiteto de sistemas Windows e **professor**. Eu sou estudante de Análise e Desenvolvimento de Sistemas e quero ENTENDER o que está sendo construído.

- Responda em português do Brasil, narrando em frases corridas, de forma natural.
- Explique decisões técnicas relevantes, sem pedir aprovação de detalhes triviais.
- **Faça commit de toda alteração**, com mensagem descritiva (um commit por unidade lógica). Termine as mensagens com a linha de coautoria que o Claude Code indicar.
- Não faça `push` por conta própria. Só envie ao GitHub quando eu disser **"Parar para ir embora"**.
- **Modo professor:** se eu pedir um conceito, ensine primeiro (o que é, por quê, exemplo curto) e espere eu dizer **continue** antes de implementar.
- Ao começar, NÃO implemente nada: verifique o estado (seção 3), resuma para mim onde paramos e pergunte por onde quero seguir.

## 1. Visão do produto

Não é um app de relatórios nem um programa só de impressoras. É um **motor modular de automação de diagnóstico e resolução assistida de problemas de TI**, com objetivo futuro de integrar suas capacidades ao ERP USE (Data System).

Ciclo: (1) detectar um problema, (2) investigar causas com evidências reais, (3) classificar e indicar a causa provável se houver evidência, (4) sugerir correção segura (no futuro, executar ação autorizada), (5) verificar se resolveu, (6) se não resolveu, gerar relatório estruturado para o suporte.

O relatório é uma SAÍDA, não o produto. A primeira área é diagnóstico de impressoras no Windows; a arquitetura deve permitir outras categorias sem reescrever o motor.

## 2. Regras inegociáveis

- **Somente leitura** nesta fase: não reiniciar serviços, não limpar filas, não instalar/remover drivers, não alterar impressora padrão, portas, registro ou rede.
- **Sem correções automáticas.** Não afirmar que algo foi resolvido sem verificação posterior.
- Não pedir elevação de administrador por padrão. Não executar comandos arbitrários.
- Não usar PowerShell como motor da aplicação (usar C# e APIs do Windows/.NET). PowerShell só para inspeção/verificação independente.
- Não transmitir dados a servidores externos; nenhuma IA externa é chamada (a "narração" é feita por regras fixas).
- Não coletar nomes de documentos da fila, conteúdo de arquivos, credenciais ou dados pessoais.
- **Não inventar** APIs, SDKs ou integração do USE; não acessar banco, arquivos ou processo do USE; só mecanismo oficialmente permitido.
- Dependência nova só com minha autorização. A única autorizada e já adicionada: `System.Management` (só em `Platform.Windows`).
- Não apagar arquivos nem executar comandos destrutivos. Não declarar testes aprovados sem executá-los.
- **Ausência de evidência não é evidência de ausência.** Não inventar porcentagens de confiança.
- Decisão reversível não especificada: escolha a mais simples e segura e documente. Decisão irreversível ou bloqueio: pare só nela e explique.

## 3. Estado atual (verificado em 2026-10-27)

Repositório: https://github.com/AbnerLevino/USEAutomation (branch `main`). .NET SDK 10.0.403, Git 2.56, VS Code. `dotnet build`: 0 avisos, 0 erros. `dotnet test`: **40 aprovados, 0 falhas**.

Commits (mais recente primeiro): `29bbcd4` virtuais x físicas + evidências no terminal · `2ffb613` docs · `34cb763` testes · `21ed000` CLI · `93d4832` diagnóstico + provedor WMI · `29fed15` contratos/Engine · `73e2e09` solução · `65868f9` repo inicial.

| Projeto | TFM | Estado |
|---|---|---|
| `Contracts` | net10.0 | PRONTO: `IDiagnostic`, `DiagnosticOutcome`, `DiagnosticProgress`, `DiagnosticReport` (schema "1.0"), `Finding`, `Evidence`, `DataSourceResult`, `Recommendation`, enums, `ReportSerializer` (camelCase, indentado, acentos legíveis) |
| `Engine` | net10.0 | PRONTO: `DiagnosticRegistry`, `DiagnosticEngine` (sempre devolve relatório; timeout 30 s, cancelamento, permissão, exceções, id inexistente) |
| `Diagnostics.Printing` | net10.0 | PRONTO: `PrinterRecognitionDiagnostic` (`printing.device-recognition`), `IPrintDeviceProvider`, `SourceResult<T>`, `PrinterClassifier`, `DeviceErrorCodes` |
| `Platform.Windows` | net10.0-windows | PRONTO: `WmiPrintDeviceProvider` (WMI `SELECT` em `Win32_PnPEntity` e `Win32_Printer`) |
| `Host.Cli` | net10.0-windows | PRONTO: `CliApp`, `ReportPrinter`, `Program` — comandos `list` e `run <id> [--json]` |
| `Tests` | net10.0-windows | 40 testes xUnit com provedor falso (regras, Engine, serialização, CLI) |
| `Host.Desktop` (WPF) | — | NÃO criado (adiado de propósito) |

Docs prontas em `README.md` e `docs/` (ARCHITECTURE, DIAGNOSTIC-REPORT, SECURITY, USE-INTEGRATION, ROADMAP).

### Regras do diagnóstico `printing.device-recognition`

1. Qualquer dispositivo com `ConfigManagerErrorCode` ≠ 0 → `ProblemConfirmed` (Confirmed, High).
2. Dispositivo **USB** (`DeviceID` começa com `USB\` ou `USBPRINT\`) sem erro e **nenhuma fila física/de rede** (fila consultada) → `ProblemProbable` (Probable, Medium).
3. Dispositivo USB sem erro, com fila física ou fila não verificável → `NoProblemDetected`.
4. Só itens virtuais/de sistema, fila de rede sem dispositivo, ou nenhum dispositivo → `Inconclusive` ("nenhuma impressora física detectada").
5. Fonte de dispositivos indisponível/sem permissão → `Inconclusive`; exceção na coleta → `Failed`/`PermissionDenied` + `NotEvaluated`.

Fila virtual = **porta** `PORTPROMPT:`, `FILE:`, `NUL:`, `SHRFAX:` ou `XPSPort:` (porta, não nome, pois o nome muda com o idioma). Porta desconhecida não é presumida virtual. Virtuais são listadas como evidência (rotuladas), mas não provam impressora real.

### Códigos de saída do CLI

0 = sem problema · 1 = problema (confirmado/provável) · 2 = inconclusivo · 3 = execução falhou (erro, permissão, timeout, cancelamento, id inexistente) · 64 = comando inválido. Com `--json`, o stdout tem só o JSON; a narração vai ao stderr (e é suprimida em `--json`).

### Resultado da execução real (máquina de desenvolvimento)

Sem impressora física. O WMI respondeu: fila `Microsoft Print to PDF` (porta `PORTPROMPT:`, virtual) e dispositivos `Microsoft Print to PDF` e `Fila de Impressão da Raiz` (`SWD\PRINTENUM\...`, código 0). Resultado: `Inconclusive`, saída 2.

### O que NÃO está provado

- A detecção **USB com hardware real**: só foi testada com dados falsos. Não se sabe se o `DeviceID` de toda impressora USB começa com `USB\`/`USBPRINT\`. Os cenários de erro reais (código 28, 45…) também nunca foram observados.

## 4. Conceitos já ensinados (modo professor)

1. **Camadas e inversão de dependência:** `Diagnostics.Printing` declara a interface do que precisa; `Platform.Windows` a implementa. Permite testar com fakes e trocar a fonte de dados.
2. **WMI/CIM:** banco de consultas do Windows (WQL, parecido com SQL). `Win32_PnPEntity.ConfigManagerErrorCode`: 0 ok, 10 não inicia, 22 desabilitado, 24 ausente/removido, 28 drivers não instalados, 43 parado por problemas, 45 não conectado. Somente leitura; `System.Management` é exclusivo do Windows.
3. **Ausência de evidência ≠ evidência de ausência:** impressora fora da lista não prova defeito (USB sem fila, rede, virtual, compartilhada); WMI negado → `Inconclusive`, nunca "sem problema".
4. **Dois status:** `ExecutionStatus` (rodou?) × `OverallStatus` (o que as evidências dizem?).
5. **`Confidence` sem porcentagem:** `Confirmed` (evidência direta) e `Probable` (indireta).
6. **`IProgress<T>`:** o diagnóstico avisa o andamento sem saber quem escuta; o CLI imprime a narração em português. A implementação é síncrona de propósito (`Progress<T>` embaralha a ordem).
7. **stdout x stderr e códigos de saída:** stdout = resultado, stderr = conversa; código de saída permite scripts decidirem sem ler texto.
8. **Testes com fakes:** `FakePrintDeviceProvider` controla o mundo sem impressora física.

## 5. Fases

| Fase | Estado |
|---|---|
| 1 Inspeção e plano | Feita |
| 2 Fundação (solução, contratos, Engine) | Feita |
| 3A Coleta WMI + regras | Feita |
| 3B CLI + narração | Feita |
| 4 Validação (40 testes + execução real) | Feita (falta hardware real) |
| 5 Documentação | Feita |
| Refinamento: virtuais × físicas | Feito |

**Próximos passos sugeridos (peça minha escolha antes de começar):**

1. **Validar com impressora real** (USB conectada, ou desconectada após instalada): rodar o CLI, comparar com `Get-CimInstance Win32_PnPEntity/Win32_Printer`, ajustar a regra USB e registrar um caso real.
2. Mais verificações de impressão (somente leitura): serviço de spooler, portas, impressora offline (`WorkOffline`), status do driver.
3. Mensagens de orientação mais precisas, a partir de casos reais.
4. Correções assistidas e verificadas — exige antes projetar ações autorizadas, validação de parâmetros, confirmação do usuário, auditoria, verificação posterior e recuperação. **Não implementar sem esse projeto aprovado.**
5. Interface desktop (WPF/MVVM) consumindo Contracts e Engine, sem duplicar regras.
6. Integração com o USE: **bloqueada** até a equipe responder às perguntas em `docs/USE-INTEGRATION.md`.
7. Outras categorias de automação.

## 6. Como retomar neste computador

```powershell
git clone https://github.com/AbnerLevino/USEAutomation.git AutomationPlatform
cd AutomationPlatform
dotnet --list-sdks      # precisa do .NET SDK 10
dotnet build
dotnet test             # esperado: 40 aprovados
dotnet run --project Host.Cli -- run printing.device-recognition
```

Pré-requisitos: Windows 10/11, .NET SDK 10, Git e acesso ao NuGet na primeira restauração. A extensão "C# Dev Kit" do VS Code é opcional (não instale sem eu autorizar).

## 7. Comece agora

Verifique o estado (seção 3) rodando `git status`, `git log --oneline` e `dotnet test`. Depois me diga, em poucas linhas, onde paramos, confirme se algo diverge deste documento e pergunte qual dos próximos passos (seção 5) eu quero seguir. Não altere nada antes disso.
