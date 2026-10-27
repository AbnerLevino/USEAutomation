# Integração com o USE: perguntas em aberto

**Nada neste documento é fato sobre o USE.** São perguntas que precisam ser respondidas pela equipe responsável na Data System. Até lá, o núcleo permanece independente e o `Host.Cli` serve apenas como demonstração e ponto de integração experimental.

## Perguntas

1. Existe SDK, API, sistema de plugins ou outro mecanismo oficial de extensão?
2. O USE pode iniciar um componente externo com parâmetros?
3. Há uma forma autorizada de receber resultados JSON (saída padrão, arquivo, API local)?
4. Como devem funcionar autenticação, permissões, atualização e distribuição?
5. A automação deve executar localmente na máquina do cliente?
6. Quais são as restrições de implantação e suporte (versões do Windows, políticas de segurança, antivírus, ausência de administrador)?
7. O formato do relatório (`schemaVersion` 1.0) atende, ou o USE exige outro contrato?

## O que NÃO será feito sem autorização

- Alterar o USE, injetar código em seu processo ou contornar controles de segurança.
- Acessar o banco de dados ou arquivos internos do USE.
- Assumir qualquer interface não documentada oficialmente.

## O que já está pronto para uma integração futura

- Saída JSON pura (`--json`) com códigos de saída documentados.
- Contrato versionado em `Contracts`, independente da interface.
