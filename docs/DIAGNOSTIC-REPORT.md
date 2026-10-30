# Formato do relatório

JSON em camelCase, enums como texto, versão do esquema em `schemaVersion` (atual: **1.2**). A versão muda quando o formato muda de forma incompatível; a 1.1 acrescentou `actions` (correções tentadas, com status, confirmação e verificação posterior) e a 1.2 acrescentou `closing` (como o fluxo guiado terminou e em que se baseia: resposta do usuário ou leitura objetiva), então relatórios anteriores continuam válidos.

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
| Algum dispositivo (qualquer tipo) com `ConfigManagerErrorCode` ≠ 0 | `ProblemConfirmed` (`Confirmed`, severidade High) |
| Dispositivo **USB** sem erro e **nenhuma fila física/de rede** instalada (fila consultada com sucesso) | `ProblemProbable` (`Probable`, Medium) |
| Dispositivo **USB** sem erro e fila física presente, ou fila não verificável | `NoProblemDetected` |
| Só itens virtuais/de sistema, filas de rede sem dispositivo, ou nenhum dispositivo | `Inconclusive` ("nenhuma impressora física detectada") |
| Fonte de dispositivos indisponível / sem permissão | `Inconclusive`, com a fonte registrada em `sources` |
| Exceção durante a coleta | `executionStatus` `Failed`/`PermissionDenied`, `overallStatus` `NotEvaluated` |

### Virtual x física

Todas as filas e dispositivos são **listados** em `evidence`, rotulados. Itens virtuais não são removidos: informam o que o Windows tem, mas **não contam como prova de impressora real**.

- **Fila virtual:** a *porta* é `PORTPROMPT:`, `FILE:`, `NUL:`, `SHRFAX:` ou `XPSPort:` (a porta é usada porque o nome muda com o idioma do Windows). Porta desconhecida **não** é presumida virtual.
- **Dispositivo USB:** o `DeviceID` começa com `USB\` ou `USBPRINT\`.
- Uma fila de rede (porta `IP_...`) sozinha **não** gera `NoProblemDetected`: a conectividade de rede não é verificada.

Códigos de erro com descrição própria: 10, 22, 24, 28, 43, 45; os demais recebem uma mensagem genérica com o número.

## Privacidade

O relatório não inclui nomes de documentos da fila, conteúdo de arquivos, credenciais nem dados pessoais. Inclui nome e identificador do dispositivo (que podem conter número de série USB).
