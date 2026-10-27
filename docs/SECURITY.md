# Segurança (versão 1)

## Garantias desta versão

- **Somente leitura:** as únicas operações no Windows são consultas WMI `SELECT` (`Win32_PnPEntity`, `Win32_Printer`).
- Não reinicia serviços, não limpa filas, não instala ou remove drivers, não altera impressora padrão, portas, registro ou rede.
- Não exige administrador. Se faltar permissão, a fonte é marcada `PermissionDenied` e o resultado vira `Inconclusive`; o programa não trava.
- Não executa comandos arbitrários: a única entrada é o id de um diagnóstico já registrado (ids desconhecidos geram relatório de falha).
- Não envia dados pela rede e não chama serviços externos. A "narração" é gerada localmente por regras fixas.
- Não coleta nomes de documentos da fila, conteúdo de arquivos, credenciais ou senhas.

## Dados que aparecem no relatório

Nome e identificador de dispositivos de impressão e nomes de filas instaladas. O identificador pode conter o número de série de um dispositivo USB. Trate o JSON como informação técnica interna ao compartilhá-lo.

## Limitações

- O relatório não é assinado nem criptografado.
- Se o WMI estiver comprometido ou adulterado, os dados refletirão isso.
- A confiabilidade do resultado depende do que o Windows reporta; ver `limitations` em cada relatório.

## Antes de qualquer fase de correção

Será necessário projetar: ações explicitamente autorizadas, validação de parâmetros, confirmação do usuário, auditoria, verificação posterior e possibilidade de recuperação. Nada disso existe hoje, e nenhuma correção automática está implementada.
