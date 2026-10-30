# Roadmap

A ordem pode mudar conforme os resultados técnicos e as respostas sobre o USE.

| # | Etapa | Estado |
|---|---|---|
| 1 | Diagnóstico inicial de impressoras (`printing.device-recognition`) | **Feito** (MVP) |
| 2 | Mais verificações de impressão: serviço de spooler (leitura), portas, impressora offline, driver | Pendente |
| 3 | Mensagens de orientação mais precisas, a partir de casos reais de suporte | Pendente |
| 4 | Correções assistidas e verificadas (autorização, confirmação, auditoria, verificação posterior, recuperação — ver [SECURITY.md](SECURITY.md)) | Pendente |
| 5 | Interface desktop (WPF/MVVM) consumindo Contracts e Engine, sem duplicar regras | Pendente |
| 6 | Integração oficialmente suportada com o USE | Bloqueada: depende das respostas em [USE-INTEGRATION.md](USE-INTEGRATION.md) |
| 7 | Outras categorias de automação (rede, serviços, etc.) | Pendente |

## Plano em andamento: diagnóstico e correção assistida

Desenho aprovado em [superpowers/specs/2026-10-28-diagnostico-e-correcao-de-impressoras-design.md](superpowers/specs/2026-10-28-diagnostico-e-correcao-de-impressoras-design.md).

| Fase | Entrega | Estado |
|---|---|---|
| A | Verificações atômicas somente leitura: spooler, fila, estado (offline/pausada/erro), padrão, porta, erro de dispositivo | **Feita** (141 testes; validada no Windows real) |
| B | Cenários e causa provável ("Não está imprimindo"), diagnóstico completo | **Feita** (181 testes; `run printing.not-printing` e `run printing.full`) |
| C | Contrato de ação, pipeline e relatório 1.1 | **Feita** (241 testes; `IRemediationAction`, `RemediationPipeline`, auditoria em memória; nenhuma ação real ainda) |
| D | Camada de interação (`IUserInteraction`) e fluxo guiado "Impressora não imprime" **na janela** (o CLI interativo foi dispensado; o CLI atual segue só para diagnóstico e `--json`) | **Feita** (308 testes; eventos estruturados, fluxo com escolha de impressora, orientação com reverificação e fechamento humano, relatório 1.2; validada na janela real com uma fila pausada de teste). Sem correção real cadastrada ainda: o "Sim, pode fazer" só aparece com a fase F |
| E | Executor elevado com UAC | **Feita, falta a validação manual da aprovação** (468 testes; `Host.Elevated` com lista fixa, local protegido, recusa e "tentar de novo"; ação reiniciar o serviço de impressão). O aviso real do UAC só um administrador valida: `tools/Test-Elevacao.ps1` |
| F | Primeiro problema ponta a ponta | Pendente |
| G | Interface WPF | **Em andamento**: botão único "Impressora não imprime", quadro fixo de impressoras e etapas, perguntas como botões, página de teste e tela de avaliação do atendimento (nota, comentário, encerramento). O relatório fica só em memória (`IReportSink`): falta definir o destino. Faltam ícones fiéis ao Painel de Controle e o aviso do UAC |

Verificações disponíveis (`run <id>`): `printing.spooler`, `printing.queue`, `printing.printer-state`, `printing.default-printer`, `printing.port`, `printing.device-error`, além de `printing.device-recognition`.

## Dívidas conhecidas

- Validar os cenários de erro (ex.: código 28) com um dispositivo real; hoje só estão cobertos por dados simulados.
- Decidir se a lista de códigos de erro descritos (10, 22, 24, 28, 43, 45) deve crescer com base em casos reais.
