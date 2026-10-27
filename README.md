# AutomationPlatform

Motor modular de automação de **diagnóstico e resolução assistida de problemas de TI**. O objetivo é detectar um problema, investigar causas com evidências reais, classificar o resultado, sugerir uma correção segura e, se nada resolver, entregar ao suporte um relatório técnico estruturado. O relatório é uma *saída*; o valor está nas automações reutilizáveis.

A primeira área é **diagnóstico de impressoras no Windows**. Objetivo futuro: oferecer essas automações dentro do ERP USE (Data System), **somente por mecanismo oficialmente suportado** (ver [docs/USE-INTEGRATION.md](docs/USE-INTEGRATION.md)).

## Estado atual (MVP)

- Diagnóstico `printing.device-recognition`: pergunta ao Windows (WMI, somente leitura) quais dispositivos de impressão ele reconhece e se algum reporta erro.
- Relatório JSON versionado (`schemaVersion` 1.0).
- CLI com narração em português e saída `--json` pura.
- Separa filas/dispositivos virtuais (PDF, XPS, fax) dos físicos ou de rede; os virtuais continuam listados, mas não provam que há impressora real.
- 40 testes automatizados (xUnit), sem depender de impressora física.
- **Somente leitura.** Nenhuma correção automática. Nenhuma integração com o USE.

## Pré-requisitos

- Windows 10/11
- [.NET SDK 10](https://dotnet.microsoft.com/download) (testado com 10.0.403)
- Acesso ao NuGet na primeira restauração (único pacote externo de runtime: `System.Management`)

## Comandos

```powershell
dotnet build                      # compila a solução
dotnet test                       # executa os testes

dotnet run --project Host.Cli -- list
dotnet run --project Host.Cli -- run printing.device-recognition          # texto narrado
dotnet run --project Host.Cli -- run printing.device-recognition --json   # só o JSON no stdout
```

### Códigos de saída

| Código | Significado |
|---|---|
| 0 | Rodou e não detectou problema nas verificações feitas |
| 1 | Rodou e encontrou problema (confirmado ou provável) |
| 2 | Rodou, mas o resultado é inconclusivo |
| 3 | A execução falhou (erro, permissão, tempo limite, cancelamento, id inexistente) |
| 64 | Comando inválido |

Com `--json`, o stdout contém **apenas** o JSON; a narração é suprimida. Sem `--json`, a narração vai para o stderr e o relatório para o stdout.

## Estrutura

| Projeto | Papel |
|---|---|
| `Contracts` | Modelos do relatório, `IDiagnostic`, enums, serializador JSON |
| `Engine` | Registra e executa diagnósticos; timeout, cancelamento e tratamento de falhas |
| `Diagnostics.Printing` | Regras do diagnóstico de impressoras (sem depender do Windows) |
| `Platform.Windows` | Acesso real ao Windows (WMI) |
| `Host.Cli` | Interface de terminal |
| `Tests` | Testes com provedores falsos |

Detalhes em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Outros documentos: [relatório](docs/DIAGNOSTIC-REPORT.md), [segurança](docs/SECURITY.md), [roadmap](docs/ROADMAP.md).

## Limitações atuais

- Só consulta dispositivos Plug and Play de impressão e filas instaladas. Spooler, portas, rede e drivers não são verificados.
- Ausência de erro **não prova** que a impressão funciona.
- Os cenários de erro (código 28 etc.) foram validados apenas com dados simulados; a execução real nesta fase encontrou somente impressoras virtuais (resultado `Inconclusive`: nenhuma impressora física detectada).
- Não há interface desktop (WPF) ainda.
