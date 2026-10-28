# Plano de implementação: do diagnóstico à correção assistida

Base: `docs/superpowers/specs/2026-10-28-diagnostico-e-correcao-de-impressoras-design.md` (aprovado). Este documento diz **o que construímos, como e quais conceitos** em cada etapa. Os detalhes finos de código são fechados no momento de cada passo.

## Como trabalhamos em TODO passo

1. **Aula curta** do conceito novo (modo professor); você diz "continue".
2. **Teste primeiro:** escrevemos o teste que descreve o comportamento; ele falha.
3. **Implementação mínima** até o teste passar.
4. **Fake → Windows real:** só depois de passar com fakes validamos no Windows de verdade.
5. **Fechamento:** `dotnet build` sem avisos, `dotnet test` verde, docs atualizadas, **um commit** por unidade lógica.

Regra permanente: nenhuma dependência nova sem a sua autorização. Onde um passo poderia exigir uma, aviso abaixo.

---

## Fase A. Verificações atômicas (somente leitura)

**Objetivo:** pequenas leituras reutilizáveis, cada uma respondendo uma pergunta ("o spooler está rodando?").

**Conceitos:** interface `ICheck`; resultado tipado; composição; WMI (`Win32_Service`, `Win32_PrintJob`, `Win32_Printer`).

| Passo | O que fazer | Como |
|---|---|---|
| A1 | Contrato `ICheck` e `CheckResult` | Interface com id e `RunAsync`; resultado carrega fatos tipados + evidências + fonte + limitações, no padrão do que já existe |
| A2 | Ampliar a fonte de dados | Nova(s) interface(s) de provedor ao lado de `IPrintDeviceProvider`, mantendo a regra: devolve `SourceResult`, não lança por falha esperada |
| A3 | Verificação **Spooler** | Estado do serviço `Spooler` e dependências (RPCSS, http) via `Win32_Service` |
| A4 | Verificação **Fila** | Quantidade e estado dos trabalhos via `Win32_PrintJob`, **selecionando só estado/contagem, nunca o nome do documento** |
| A5 | Verificação **Offline/Pausada** | `WorkOffline`, `PrinterStatus`, `DetectedErrorState` via `Win32_Printer` |
| A6 | Verificação **Impressora padrão** | Impressora marcada como padrão + a opção "Windows gerencia a padrão" (chave de registro do usuário, só leitura) |
| A7 | Verificação **Porta** | Só lê a configuração da porta (tipo, endereço); sem teste de rede nesta fase |
| A8 | Verificação **Erro de dispositivo** | Reaproveita o que `printing.device-recognition` já faz; o id antigo continua funcionando |
| A9 | Execução real | Rodar cada verificação na sua máquina e registrar no doc o que o WMI devolve |

**Pronto quando:** cada verificação tem testes com fake e um registro do valor real obtido.

**Nota de segurança:** `Win32_PrintJob` contém o nome do documento; a consulta pede apenas colunas de estado.

---

## Fase B. Cenários e causa provável

**Objetivo:** o programa deixa de listar fatos e passa a **concluir**.

**Conceitos:** regras de decisão; causa × consequência; prioridade entre causas; `Confidence` (Confirmed/Probable).

| Passo | O que fazer | Como |
|---|---|---|
| B1 | Contrato `IScenario` | Recebe as verificações, roda, cruza e devolve um `DiagnosticOutcome` compatível com o Engine, para o cenário aparecer como diagnóstico comum |
| B2 | Cenário **"Não está imprimindo"** (somente leitura) | Ordem das regras: spooler parado → impressora pausada/offline → erro de dispositivo/driver → trabalho preso → nada encontrado (`Inconclusive`) |
| B3 | Indicar **ação candidata** | O achado já nomeia qual ação resolveria (ainda sem executar) |
| B4 | **Diagnóstico completo** | Roda todas as verificações e resume; é o futuro botão "Diagnóstico completo" |
| B5 | Testes de combinação | Uma causa; duas causas juntas; nenhuma evidência; fonte indisponível |

**Regra central:** "trabalhos numa fila pausada" são consequência da pausa, não "trabalho preso". Nada de porcentagens inventadas.

**Pronto quando:** cada causa e cada combinação do catálogo tem teste, e o CLI roda o cenário só para diagnosticar.

---

## Fase C. Contrato de ação e pipeline

**Objetivo:** definir **como** uma correção é descrita, confirmada, verificada e registrada, antes de existir qualquer correção real.

**Conceitos:** padrão *Command*; pré-condição; **idempotência** (rodar duas vezes não piora); níveis N1/N2/N3; auditoria; esquema versionado aditivo.

| Passo | O que fazer | Como |
|---|---|---|
| C1 | `IRemediationAction` | Id fixo, nível, texto de confirmação, `CanRun` (pré-condição), `ExecuteAsync`, `VerifyAsync` |
| C2 | `RemediationPipeline` | Sequência: pré-condição → (confirmação) → execução → verificação → resultado. Falha de verificação = "**não resolvido**" |
| C3 | Registro de auditoria | O que foi pedido, confirmado, executado e verificado, com horário; sem nomes de documentos |
| C4 | Relatório **1.1** | Campos novos *aditivos* (ações, verificação, auditoria); relatórios 1.0 continuam válidos |
| C5 | Novo estado `ElevationDenied` | Para o caso em que o administrador recusa ou a senha falha |
| C6 | Testes com ação falsa | Passa; falha na execução; executa mas não resolve; usuário recusa |

**Pronto quando:** uma ação falsa percorre o pipeline inteiro nos testes, incluindo o caminho "não resolveu".

---

## Fase D. Camada de interação

**Objetivo:** o motor conversa com o usuário por uma interface, sem saber se é terminal ou tela.

**Conceitos:** interface de interação; máquina de estados; cancelamento seguro; modo não interativo.

| Passo | O que fazer | Como |
|---|---|---|
| D1 | `IUserInteraction` | Operações: avisar, **confirmar**, **escolher** da lista, **pedir ação física** ("ligue a impressora e responda") |
| D2 | `AssistedSession` | Orquestra: diagnostica → mostra causa → propõe ação → confirma → executa → verifica → pergunta "voltou a imprimir?" |
| D3 | `ConsoleInteraction` | Implementação no terminal, em português simples, uma pergunta por vez |
| D4 | Comandos do CLI | Menu "Diagnóstico completo / Problemas comuns"; `--json` e saída sem terminal **só diagnosticam** |
| D5 | `FakeInteraction` e testes | Cada fluxo, cada recusa, cancelamento em qualquer ponto |

**Pronto quando:** o fluxo completo roda com ação falsa, no terminal e nos testes, e cancelar nunca deixa o sistema alterado.

---

## Fase E. Executor elevado

**Objetivo:** executar com poder de administrador **somente** o necessário, depois da confirmação.

**Conceitos:** UAC; níveis de integridade; **menor privilégio**; lista branca de ações; "não confiar no chamador".

| Passo | O que fazer | Como |
|---|---|---|
| E1 | Projeto `Host.Elevated` | Executável pequeno; só aceita ids da lista fixa e parâmetros validados |
| E2 | Pré-condição compartilhada | Biblioteca pequena usada pelos dois lados; o executor **revalida** sozinho |
| E3 | Lançamento com UAC | O programa inicia o executor com o verbo de elevação do Windows; a senha é digitada no aviso do Windows, **nunca passa pelo nosso código** |
| E4 | Recusa e falha | Cancelar o UAC, senha errada ou administrador ausente viram `ElevationDenied` com mensagem simples e **nada alterado** |
| E5 | Verificação do local | Só aciona o executor se ele estiver na mesma pasta e ela for gravável apenas por administrador; modo desenvolvimento explícito, com aviso |
| E6 | Resultado de volta | Resposta estruturada (sucesso/falha/recusa) para o pipeline verificar e auditar |
| E7 | Testes | Lógica e lista branca com testes automáticos; fluxo real do UAC com roteiro manual (aprovar, recusar, senha errada) |

**Decisão a fechar no passo:** forma de enviar o pedido e receber a resposta (argumentos + arquivo temporário seguro, ou canal nomeado).

**Pronto quando:** uma ação inofensiva real passa pelo UAC e volta verificada, e a recusa é tratada com clareza.

---

## Fase F. Primeiro problema ponta a ponta

**Objetivo:** "Impressora não está imprimindo" diagnosticado e **resolvido**.

**Conceitos:** idempotência real; verificação independente; validação sem hardware.

| Passo | Ação | Mecanismo previsto |
|---|---|---|
| F1 | Iniciar/reiniciar o spooler (N2) | Chamada de método do serviço pelo WMI (`Win32_Service`) |
| F2 | Retomar impressora pausada (N1) | Método do `Win32_Printer` |
| F3 | Colocar impressora online (N2) | Propriedade `WorkOffline` do `Win32_Printer` |
| F4 | Cancelar trabalhos presos (N1; sobe para N2 se falhar) | `Win32_PrintJob`; uma só confirmação que já avisa sobre o administrador |
| F5 | Limpar a pasta de spool (N3) | Para o spooler, esvazia a pasta, inicia; **só se o spooler estiver travado**, com aviso da perda de trabalhos |
| F6 | Definir impressora padrão (N1) | Método do `Win32_Printer`; se o Windows gerencia a padrão, confirmação separada para desligar |
| F7 | Ligar tudo ao cenário | Cada causa do cenário aciona sua ação |
| F8 | Validação **sem hardware** | Pausar a fila virtual "Microsoft Print to PDF" → diagnosticar → retomar → verificar. Parar o spooler → diagnosticar → iniciar → verificar |
| F9 | Fechamento humano | "Voltou a imprimir?" e registro de como foi decidido |

**Aviso de dependência:** reiniciar serviços pelo WMI usa o que já temos (`System.Management`). Se for melhor usar a biblioteca própria de serviços do .NET (`ServiceController`), ela é um **pacote novo** e eu peço a sua autorização antes.

**Pronto quando:** os dois cenários do F8 passam de ponta a ponta na sua máquina; o caso "trabalho preso" fica coberto por testes com dados falsos até haver impressora real.

---

## Fase G. Interface desktop (WPF)

**Objetivo:** a mesma lógica, com botões.

**Conceitos:** WPF; **MVVM** (separar tela, lógica de tela e dados); implementar `IUserInteraction` com diálogos.

| Passo | O que fazer |
|---|---|
| G1 | Projeto `Host.Desktop` consumindo `Contracts`, `Engine` e as sessões, **sem duplicar regra** |
| G2 | Tela inicial: **Diagnóstico completo** e **Problemas comuns** |
| G3 | Tela de conversa: progresso narrado, confirmações, pedidos de ação física |
| G4 | Mensagens de recusa do administrador (simples e claras) |
| G5 | Os cenários da fase F funcionando pela tela |

WPF vem no SDK (sem pacote novo).

---

## Depois (fora deste plano)

- **H. Expansão do catálogo:** driver, rede (com teste de alcance), servidor de impressão, disco, políticas, mais casos reais do suporte.
- **I. Integração com o USE:** bloqueada até a equipe responder `docs/USE-INTEGRATION.md`.

## Riscos acompanhados em todas as fases
- Ação elevada num executável substituível → instalação em pasta protegida + lista branca (E5).
- WMI negado ou diferente por versão do Windows → sempre `Inconclusive`, nunca "sem problema".
- Perda de trabalhos ao limpar fila → sempre N3 com aviso.
- Dados falsos não substituem hardware → dívida registrada até termos impressora real.

## Ordem e dependências
A → B → C → D → E → F → G. C e D podem ser desenhadas em paralelo, mas F exige C, D e E prontas.
