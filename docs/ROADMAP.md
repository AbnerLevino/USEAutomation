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

## Dívidas conhecidas

- Validar os cenários de erro (ex.: código 28) com um dispositivo real; hoje só estão cobertos por dados simulados.
- Decidir se a lista de códigos de erro descritos (10, 22, 24, 28, 43, 45) deve crescer com base em casos reais.
