# Segurança

## Diagnóstico: somente leitura

- Toda investigação só lê o computador (consultas WMI `SELECT` e leitura de estado de serviços).
- Não exige administrador. Se faltar permissão, a fonte é marcada `PermissionDenied` e o resultado vira `Inconclusive`; o programa não trava.
- Não executa comandos arbitrários: a única entrada é o id de um diagnóstico já registrado.
- Não coleta nomes de documentos da fila, conteúdo de arquivos, credenciais ou senhas.
- Não envia dados pela rede nem chama serviços externos. O relatório, hoje, fica só em memória (`IReportSink`); quando houver um destino definido, o texto da tela deve passar a dizê-lo.

## Correções: só com confirmação, uma ação nomeada por vez

- Nenhuma alteração acontece sem uma confirmação explícita do usuário, em linguagem simples, e sem a pré-condição ainda valer.
- Cada ação tem nível: **N1** (usuário comum, reversível), **N2** (exige administrador, reversível), **N3** (exige administrador e pode apagar dados do usuário, com aviso destacado).
- Depois de executar, o programa **lê o estado de novo** de forma independente. "Resolvido" só sai dessa leitura ou da resposta do usuário, e o relatório diz qual das duas foi usada.
- Toda tentativa, inclusive a recusada, vai para a auditoria (hoje em memória) e para o relatório (`actions`).
- Ações hoje: retomar impressora pausada (N1), imprimir página de teste (N1), reiniciar o serviço de impressão (N2, pelo executor elevado).

## Executor elevado (fase E)

O app roda como **usuário comum**. Só um programa separado e pequeno, `Host.Elevated`, ganha poder de administrador.

- **Quem aprova:** o Windows. O aviso do UAC é mostrado pelo sistema (verbo `runas`) e a senha do administrador é digitada nele; este programa **nunca vê nem guarda a senha**. Cada ação aprovada gera um aviso novo, e o executor termina ao acabar.
- **Lista fixa:** o executor só aceita ids de `ElevatedProtocol.AllowedActions`, com parâmetros validados (ids só com letras minúsculas, dígitos, ponto e hífen; nomes de impressora sem aspas, sem caracteres de controle). Não interpreta comando livre. O serviço mexido (`Spooler`) é fixo no código e nunca vem do pedido.
- **Não confia no chamador:** valida o pedido inteiro, confere por conta própria que está elevado e revalida a condição de agir antes de tocar em qualquer coisa. Pedido inválido tem sempre o mesmo código (20), elevado ou não.
- **Resultado só pelo código de saída** (0 feito, 10 falhou, 20 recusou, 21 condição não existe mais, 22 sem elevação). Não há arquivo temporário nem canal em que um usuário comum pudesse interferir: um processo elevado que escreve em caminho escolhido por usuário comum é um risco clássico.
- **Local protegido:** antes de lançar, o app confere que o executor existe e que **nem o arquivo, nem a pasta, nem as pastas acima** podem ser alterados por quem não é administrador (`ExecutableTrust`). Senão, recusa **sem mostrar o aviso do Windows**: aprovar o aviso para um executável que um usuário comum possa trocar entregaria poder de administrador a ele. Pastas acima só contam se permitirem apagar/renomear, mudar permissões ou tomar posse (criar itens em `C:\` todo usuário pode, e isso não troca uma pasta que já existe).
- **Modo de desenvolvimento:** `AUTOMAXION_DEV_ELEVATED=1` libera uma pasta comum (como `bin\Debug`), e o resultado da ação leva o aviso "executor em pasta sem proteção". Nunca use em máquinas dos usuários.
- **Aprovação recusada:** o app mostra "Para continuar é preciso a aprovação de um administrador. Nada foi alterado." e oferece tentar de novo sem repetir a primeira pergunta.

## Dados que aparecem no relatório

Nome e identificador de dispositivos de impressão e nomes de filas instaladas. O identificador pode conter o número de série de um dispositivo USB. O relatório inclui ainda as ações tentadas e, se o usuário avaliar, a nota e o **comentário livre** (até 500 letras), que pode conter dado pessoal. Trate o JSON como informação interna ao compartilhá-lo.

## Limitações conhecidas

- O relatório não é assinado nem criptografado.
- Se o WMI estiver comprometido ou adulterado, os dados refletirão isso.
- A confiabilidade do resultado depende do que o Windows reporta; ver `limitations` em cada relatório.
- O executor não é assinado digitalmente, e a distribuição (instalador em pasta protegida, assinatura) ainda não foi desenhada: depende de como será implantado.
- A aprovação real do UAC não é automatizável; ela é validada pelo roteiro manual `tools/Test-Elevacao.ps1`.
