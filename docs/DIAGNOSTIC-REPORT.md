# Formato do relatório

JSON em camelCase, enums como texto, versão do esquema em `schemaVersion` (atual: **1.0**). A versão muda quando o formato muda de forma incompatível.

## Campos

| Campo | Descrição |
|---|---|
| `schemaVersion` | Versão do formato |
| `diagnosticId` | Ex.: `printing.device-recognition` |
| `executionStatus` | A execução conseguiu rodar? |
| `overallStatus` | O que as evidências concluem? |
| `startedAtUtc`, `completedAtUtc` | ISO 8601 |
| `summary` | Resumo em linguagem natural |
| `findings` | Problemas encontrados (`id`, `title`, `description`, `severity`, `confidence`, `evidenceIds`) |
| `evidence` | Fatos observados (`id`, `source`, `description`) |
| `sources` | Fontes consultadas e o resultado de cada uma |
| `limitations` | O que não foi verificado ou não pode ser afirmado |
| `recommendations` | Próximos passos sugeridos (só texto; nada é executado) |
| `errorMessage` | Detalhe técnico quando `executionStatus` ≠ `Completed` |

## `executionStatus`

`Completed`, `Failed`, `PermissionDenied`, `TimedOut`, `Cancelled`.

## `overallStatus`

| Valor | Significado |
|---|---|
| `ProblemConfirmed` | Evidência direta de problema (ex.: o Windows reporta código de erro) |
| `ProblemProbable` | Indícios, sem prova direta |
| `NoProblemDetected` | As verificações feitas não encontraram problema. **Não prova que ele não existe.** |
| `Inconclusive` | Evidência insuficiente para concluir |
| `NotEvaluated` | A execução falhou; nada foi concluído |

## `confidence` (sem porcentagem)

- `Confirmed`: provado por evidência direta.
- `Probable`: sugerido por evidência indireta.

Não existe pontuação numérica: um número só seria honesto com uma base estatística que não temos.

## Regras do diagnóstico `printing.device-recognition`

| Situação observada | `overallStatus` |
|---|---|
| Algum dispositivo com `ConfigManagerErrorCode` ≠ 0 | `ProblemConfirmed` (`Confirmed`, severidade High) |
| Dispositivo sem erro e **nenhuma fila** instalada (fila consultada com sucesso) | `ProblemProbable` (`Probable`, Medium) |
| Dispositivo sem erro e fila presente, ou fila não verificável | `NoProblemDetected` |
| Nenhum dispositivo reconhecido | `Inconclusive` (impressoras de rede/virtuais não aparecem como dispositivo local) |
| Fonte de dispositivos indisponível / sem permissão | `Inconclusive`, com a fonte registrada em `sources` |
| Exceção durante a coleta | `executionStatus` `Failed`/`PermissionDenied`, `overallStatus` `NotEvaluated` |

Códigos de erro com descrição própria: 10, 22, 24, 28, 43, 45; os demais recebem uma mensagem genérica com o número.

## Privacidade

O relatório não inclui nomes de documentos da fila, conteúdo de arquivos, credenciais nem dados pessoais. Inclui nome e identificador do dispositivo (que podem conter número de série USB).
