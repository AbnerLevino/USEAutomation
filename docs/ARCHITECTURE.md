# Arquitetura

## Camadas e dependências

```
Host.Cli ──► Engine ──► Contracts ◄── Diagnostics.Printing
   │                                         ▲
   └──► Platform.Windows ────────────────────┘ (implementa IPrintDeviceProvider)
```

Princípio: **as regras de negócio não conhecem o Windows**. `Diagnostics.Printing` declara o que precisa (`IPrintDeviceProvider`) e `Platform.Windows` entrega. Isso permite testar com dados falsos e trocar a fonte de dados sem mexer nas regras.

| Projeto | Responsabilidade | Depende de |
|---|---|---|
| `Contracts` | `IDiagnostic`, `DiagnosticReport`, `Finding`, `Evidence`, enums, `ReportSerializer` | — |
| `Engine` | `DiagnosticRegistry` e `DiagnosticEngine` | Contracts |
| `Diagnostics.Printing` | `PrinterRecognitionDiagnostic`, `IPrintDeviceProvider` | Contracts |
| `Platform.Windows` | `WmiPrintDeviceProvider` (WMI, só `SELECT`) | Contracts, Diagnostics.Printing, `System.Management` |
| `Host.Cli` | `CliApp` (lógica), `ReportPrinter`, `Program` (composição) | todos os anteriores |
| `Tests` | Regras, Engine, serialização e CLI | todos |

`Platform.Windows`, `Host.Cli` e `Tests` usam `net10.0-windows` porque dependem de APIs exclusivas do Windows.

## Fluxo de execução

1. `Program` registra o diagnóstico com o provedor WMI e chama `CliApp.RunAsync`.
2. `CliApp` interpreta `list` / `run <id> [--json]`.
3. `DiagnosticEngine.RunAsync` localiza o diagnóstico, aplica timeout (padrão 30 s) e cancelamento, e **sempre devolve um `DiagnosticReport`**: exceções viram relatório com `ExecutionStatus` correspondente, nunca são engolidas.
4. O diagnóstico consulta o provedor, aplica as regras e avisa o andamento via `IProgress<DiagnosticProgress>`.
5. O CLI imprime a narração (stderr), o relatório (stdout) e devolve o código de saída.

## Decisões relevantes

- **Dois status separados:** `ExecutionStatus` (conseguiu rodar?) e `OverallStatus` (o que as evidências dizem?).
- **Provedores devolvem `SourceResult`** em vez de lançar exceção para falhas esperadas (sem permissão, WMI fora do ar), para que o relatório registre *qual fonte* falhou.
- **Narração por regras fixas:** frases escritas no código e preenchidas com dados reais. Nenhuma IA externa é chamada.
- **Sem abstrações antecipadas:** só existe a interface necessária para a primeira automação.
