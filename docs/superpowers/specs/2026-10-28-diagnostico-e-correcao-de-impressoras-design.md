# Diagnóstico e correção assistida de impressoras: desenho

Data: 2026-10-28 · Estado: rascunho para aprovação · Porte: projeto (várias fases)

## 1. Intenção e porquê

O AutomationPlatform já reconhece se o Windows enxerga uma impressora física (`printing.device-recognition`, 40 testes, somente leitura). Isso é um diagnóstico isolado. O objetivo agora é o ciclo completo para o **usuário final**:

> o usuário diz o que está acontecendo → o programa investiga com evidências → aponta a causa provável → propõe uma correção → pede confirmação → executa → **verifica se resolveu** → se não resolveu, gera relatório para o suporte.

O usuário final roda o programa sem privilégio de administrador. Quando uma correção exige administrador, o programa chama a elevação do Windows (UAC), onde o administrador da máquina aprova. O programa precisa ser simples, rápido e fácil para quem está com o problema.

Meta desta etapa: **o primeiro problema, "Impressora não está imprimindo", sendo diagnosticado e resolvido de ponta a ponta**, sobre uma arquitetura que já comporta o catálogo completo de problemas de impressora (seção 5).

## 2. Objetivos, não-objetivos, antiobjetivos

**Objetivos**
- Duas portas de entrada: **Diagnóstico completo** (roda todas as verificações) e **Problemas comuns** (cenários por sintoma, como "não está imprimindo").
- Cenários que combinam várias verificações e apontam a causa mais provável a partir das evidências.
- Correções reais, pequenas, nomeadas e verificáveis.
- Interação conduzida: o programa pergunta, pede confirmação e pede ao usuário que faça coisas físicas (ligar, conectar cabo) quando necessário.
- Toda alteração no computador confirmada pelo usuário, registrada e seguida de verificação.

**Não-objetivos (nesta etapa)**
- Integração com o USE (continua bloqueada, ver `docs/USE-INTEGRATION.md`).
- Outras categorias além de impressão.
- Instalar, remover ou atualizar drivers automaticamente.
- Serviço do Windows residente (caminho C da seção 4).
- Impressão de página de teste feita pelo programa.

**Antiobjetivos (falha mesmo que "funcione")**
- Declarar "resolvido" sem verificação posterior.
- Executar qualquer coisa fora da lista fixa de ações.
- Pedir administrador sem o usuário ter confirmado antes, ou rodar a interface inteira elevada.
- Perder trabalho do usuário sem avisar: limpar fila apaga trabalhos pendentes e isso precisa estar explícito na confirmação.
- Esconder falha: se o usuário recusar a elevação ou a senha falhar, isso aparece com clareza e **nada é alterado**.

## 3. Restrições (herdadas e mantidas)

- Dependência nova só com autorização; hoje só `System.Management` (apenas em `Platform.Windows`).
- Sem IA externa, sem transmissão de dados a servidores, sem PowerShell como motor, sem comando arbitrário.
- Não coletar nomes de documentos da fila nem conteúdo. A confirmação de "limpar fila" mostra **quantidade** de trabalhos, nunca nomes.
- Ausência de evidência não é evidência de ausência; sem porcentagens inventadas (`Confirmed`/`Probable`).
- Um commit por unidade lógica; push só no gatilho "Parar para ir embora".

## 4. Abordagens consideradas

| | O que é | Prós | Contras |
|---|---|---|---|
| A | Programa inteiro elevado | Simples | UAC a cada abertura; leitura e interface com poder total; contradiz a segurança |
| **B (escolhida)** | Programa como usuário comum + executor elevado chamado só quando preciso | Elevação só no momento da correção, depois da confirmação; lista fixa auditável | Dois executáveis; comunicação entre eles |
| C | Serviço do Windows instalado uma vez | Sem UAC a cada correção | Exige instalador, atualização e superfície de ataque maior; depende da definição de implantação do USE |

Escolha do usuário: **B** ("rodar e chamar o administrador apenas quando necessário; se parar por causa da senha, mostrar de forma simples"). A lista fixa de ações de B é a mesma que um serviço futuro (C) usaria, então migrar depois não refaz o desenho.

## 5. Catálogo de problemas de impressora

Níveis de ação: **N0** só leitura · **N1** usuário comum, reversível · **N2** administrador, reversível · **N3** administrador, com perda possível (exige aviso explícito). "Orientação" = o programa explica o que fazer, sem alterar nada.

| # | Problema | Evidências / diagnóstico | Correção | Interação |
|---|---|---|---|---|
| 1 | **Não está imprimindo** (cenário-mãe) | Combina os itens 2, 3, 4, 5, 6, 9 e 10 | A da causa encontrada | Pergunta qual impressora; narra; propõe correção; verifica |
| 2 | Serviço de spooler parado ou travado | Estado do serviço `Spooler` e de dependências (RPCSS, http) | Iniciar/reiniciar spooler (N2) | Confirmar; UAC; verificar serviço em execução |
| 3 | Fila com trabalho preso (estado de erro/exclusão) | Quantidade e estado dos trabalhos, sem nomes | Cancelar trabalhos presos (N1/N2); se o spooler estiver travado, parar, esvaziar `spool\PRINTERS` e iniciar (N3) | Mostrar quantidade; avisar que serão perdidos; confirmar |
| 4 | Impressora **offline** (`WorkOffline`) | Propriedade `WorkOffline` e status | Colocar online (N2). Se USB/rede inacessível, ver 9 e 11 | Confirmar; verificar |
| 5 | Impressora **pausada** | Status da fila | Retomar (N1) | Confirmar; verificar |
| 6 | **Impressora padrão** ausente ou errada | Padrão do usuário vs. impressoras físicas existentes | Definir padrão (N1) | Perguntar qual; confirmar |
| 7 | Driver ausente/com erro | `ConfigManagerErrorCode` (28 etc.), driver da fila | **Orientação** nesta etapa (instalar driver fica fora) | Explicar, dar passos |
| 8 | Porta incorreta ou impressora de rede inacessível | Porta configurada (TCP/IP, WSD, USB); teste de alcance **só ao endereço da própria porta** | Orientação; recriar porta fica para depois | Explicar; pedir ao usuário que confira rede/cabo |
| 9 | Dispositivo USB não reconhecido/erro PnP | Já existe: `printing.device-recognition` | Orientação (cabo, porta USB, ligar) | Pedir ação física e **re-verificar** ao responder |
| 10 | Erro do próprio equipamento (papel, atolamento, toner, tampa) | `DetectedErrorState`, `PrinterStatus` | Orientação | Dizer o que olhar na impressora; re-verificar |
| 11 | Impressora compartilhada/servidor de impressão inacessível | Caminho UNC, alcance do servidor | Orientação | Explicar; acionar suporte com relatório |
| 12 | Pouco espaço em disco para o spool | Espaço livre no volume da pasta de spool | Orientação (liberar espaço) | Explicar |
| 13 | Política bloqueando conexão/instalação (erros de Point and Print) | Códigos de erro conhecidos nos eventos do spooler | Orientação | Gerar relatório para o suporte/administrador |

Primeiro corte do catálogo (suficiente para a meta): itens 2, 3, 4, 5 e 6 com correção; 9 e 10 como orientação com re-verificação; 1 como cenário que os combina. Os demais entram na fase de expansão.

## 6. Arquitetura

Camadas existentes ficam. Entram quatro conceitos novos:

1. **Verificação (`ICheck`)**: unidade pequena e somente leitura que devolve evidências (spooler, fila, offline, pausada, padrão, porta, erro de dispositivo). O `printing.device-recognition` atual vira uma verificação.
2. **Cenário (`IScenario`)**: compõe verificações, cruza evidências e produz achados com **causa provável** e **ações candidatas**. Exemplo: "spooler parado" explica "fila travada" e é a causa; a fila vira consequência.
3. **Ação (`IRemediationAction`)**: nome fixo, nível (N1/N2/N3), parâmetros validados, **pré-condição**, texto de confirmação, execução e **verificação posterior**. Cada execução gera registro de auditoria.
4. **Interação (`IUserInteraction`)**: o motor pergunta e pede confirmação por essa interface. Terminal e WPF a implementam; testes usam uma versão falsa.

**Pipeline de uma correção:** diagnosticar → causa provável → propor ação → confirmar com o usuário → (se N2/N3) chamar executor elevado → executar → verificar → relatar. Se a verificação falhar, o relatório diz que **não** resolveu e por quê.

**Executor elevado (`Host.Elevated`)**: executável pequeno, iniciado com elevação **uma vez por ação aprovada** e que termina ao acabar. Só aceita identificadores de ação da lista embutida e parâmetros validados; não interpreta comando livre. Devolve um resultado estruturado (sucesso, falha ou recusa) que a camada principal usa para verificar e registrar.

**Elevação recusada ou falha:** se o usuário cancelar o UAC, o administrador não estiver presente ou a senha for rejeitada, o programa mostra uma mensagem simples ("Para continuar é preciso a aprovação de um administrador. Nada foi alterado."), oferece tentar de novo ou encerrar e inclui o fato no relatório. Novo estado de execução previsto: `ElevationDenied`.

**Relatório:** o schema evolui de forma **aditiva** (1.0 → 1.1): ações propostas/executadas, verificações posteriores, auditoria. Relatórios 1.0 continuam válidos.

**Modos de execução:** `--json` e execução sem terminal interativo **só diagnosticam**, nunca corrigem. Correção exige interação humana.

## 7. Modelo de interação

Duas portas de entrada, no terminal agora e na tela depois:

- **Diagnóstico completo:** roda todas as verificações, mostra o resumo, lista achados e oferece as correções aplicáveis.
- **Problemas comuns:** o usuário escolhe o sintoma ("Não está imprimindo"), escolhe a impressora se houver mais de uma, e o cenário conduz.

Tipos de interação do motor: **confirmar** (sim/não), **escolher** (uma opção de lista), **pedir ação física** ("ligue a impressora, depois responda") e **aguardar/re-verificar**. Sempre há opção de cancelar. A narração em português continua por regras fixas.

Princípios: uma pergunta por vez; linguagem de usuário, não de técnico; toda alteração é anunciada antes e confirmada depois; o resultado final diz com clareza **resolvido**, **não resolvido** ou **inconclusivo**, nunca "talvez".

## 8. Fases

Cada fase termina com build sem avisos, testes aprovados e um commit por unidade lógica. Marcadas as dependências.

| Fase | Entrega | Critério de pronto |
|---|---|---|
| **A. Verificações atômicas** | Contrato `ICheck`; verificações de spooler, fila, offline, pausada, padrão, porta e erro de dispositivo; provedor WMI estendido; `printing.device-recognition` adaptado | Cada verificação com testes de fake; execução real documenta o que o WMI devolve na máquina de desenvolvimento |
| **B. Cenários e causa provável** | `IScenario`; regras de cruzamento; cenário "Não está imprimindo" **somente leitura** (aponta causa e ação candidata, ainda sem executar) | Casos de teste para cada causa e para causas combinadas; `Inconclusive` quando não há evidência |
| **C. Contrato de ação e pipeline** | `IRemediationAction`, níveis, pré-condição, verificação posterior, auditoria; relatório 1.1 | Ações falsas passam por todo o pipeline em teste; falha de verificação vira "não resolvido" |
| **D. Camada de interação** | `IUserInteraction`; CLI interativo (perguntar, confirmar, pedir ação física, re-verificar); `--json` continua só diagnóstico | Fluxos completos testados com interação falsa; cancelar a qualquer momento é seguro |
| **E. Executor elevado** | `Host.Elevated`; lista fixa de ações; chamada com UAC; tratamento de recusa/falha; local de instalação protegido contra troca do executável | Testes com executor falso; teste manual real com UAC, incluindo recusa e senha incorreta |
| **F. Primeiro problema ponta a ponta** | Ações: iniciar/reiniciar spooler, cancelar/limpar fila, colocar online, retomar impressora, definir padrão; cenário 1 completo | Validação real **sem hardware**: criar um trabalho preso na fila virtual "Microsoft Print to PDF" (pausada), diagnosticar, corrigir, verificar. Depois repetir com impressora física quando houver |
| **G. Interface desktop (WPF)** | Tela com "Diagnóstico completo" e "Problemas comuns", mesma lógica por `IUserInteraction`, sem duplicar regras | Mesmos cenários da fase F operam pela tela |
| **H. Expansão do catálogo** | Itens 7, 8, 11, 12, 13 e demais; ampliação com casos reais de suporte | Cada item com diagnóstico, orientação/correção e testes |
| **I. Integração com o USE** | Bloqueada | Perguntas de `docs/USE-INTEGRATION.md` respondidas |

Ordem: A → B → C → D → E → F → G; H e I depois. Fase E pode ser desenhada em paralelo à D, mas a F exige as duas.

## 9. Verificação do que é "resolvido"

Verificação posterior por correção, sempre por leitura independente da execução:

- Spooler: serviço em estado em execução, dependências em execução.
- Fila: sem trabalhos em erro/exclusão na impressora afetada.
- Offline/pausada: propriedade e status refletem online/ativa.
- Padrão: impressora padrão do usuário é a escolhida.
- Fechamento humano: ao final, o programa pergunta se a impressão voltou a funcionar. Só a resposta do usuário (ou a verificação objetiva, se ele não puder testar) define o texto final, e o relatório registra qual dos dois foi usado.

## 10. Testes

- Fakes para provedor de dados, executor de ações, interação e relógio, no padrão atual.
- O executor elevado é testado em dois níveis: lógica de validação e lista fixa sem elevação; fluxo real de elevação manualmente documentado.
- Validação com impressora virtual (fila pausada com trabalho preso) cobre a fase F sem hardware. Cenários com USB real e códigos de erro reais seguem como dívida até haver equipamento.

## 11. Pontos que o desenho deixa para quem for construir

- Forma de comunicação entre programa e executor elevado (argumentos e arquivo temporário seguro, ou canal nomeado).
- Formato do registro de auditoria e onde ele fica no disco do usuário.
- Detalhes de texto das mensagens em português.
- Quais códigos de erro de Point and Print entram na fase H.

## 12. Decisões após a revisão do desenho

**Já respondidas (pela conversa ou pelo repositório)**
- *Quem aprova o UAC:* o usuário final é usuário comum; o administrador da máquina digita as credenciais no aviso do Windows. A elevação usa o mecanismo padrão do Windows (verbo `runas` do `ShellExecute`); o programa **nunca vê nem guarda a senha**.
- *Plataforma:* o repositório já fixa `net10.0` (núcleo) e `net10.0-windows` (Windows/CLI/testes). `Host.Elevated` segue `net10.0-windows`. Autocontido ou dependente do runtime fica para a implantação.

**Decisões minhas (marcadas para você conferir)**
1. *Executor elevado e confiança:* o programa só aciona o executor se ele estiver na mesma pasta e essa pasta for gravável apenas por administrador (ex.: `Program Files`). Em desenvolvimento, uma opção explícita libera pasta comum, com aviso. Assinatura digital e instalador ficam para a implantação (depende do USE).
2. *O executor não confia no chamador:* ele revalida a pré-condição por conta própria, com leituras mínimas, e aceita só identificadores da lista fixa com parâmetros validados. A lógica de pré-condição fica numa biblioteca pequena compartilhada, para não existir em dois lugares.
3. *Cancelar trabalhos:* é **uma** ação com **uma** confirmação. Ela tenta primeiro sem elevação (trabalhos do próprio usuário); só se falhar, pede elevação. O texto da confirmação já avisa que o administrador pode ser chamado.
4. *Porta:* a verificação de porta da fase A só **lê a configuração**. O teste de alcance na rede fica na fase H.
5. *Trabalho "preso":* estado de erro, exclusão ou bloqueio. Trabalhos parados numa fila **pausada ou offline** são consequência dessa causa, não "trabalho preso". Sem limite de tempo na primeira versão, para não inventar um número. Por isso a validação sem hardware da fase F cobre **pausada → retomar** e **spooler parado → iniciar**; "trabalho preso" é validado com dados falsos e com impressora real quando houver.
6. *Impressora padrão gerenciada pelo Windows:* se essa opção estiver ligada, o programa avisa e pede uma **confirmação separada** para desligá-la antes de definir o padrão. Sem isso, a verificação posterior seria falsa.

## 13. Riscos conhecidos

- Executar uma ação elevada num executável que o usuário comum possa substituir: mitigado instalando em local protegido e validando a lista de ações.
- WMI pode negar acesso ou devolver dados diferentes por versão do Windows: continua tratado como `Inconclusive`, nunca "sem problema".
- Limpar fila apaga trabalhos: sempre N3, com aviso explícito.
