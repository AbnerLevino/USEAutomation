# Aula: a estrutura do AutomationPlatform e o C# por trás

Esta aula explica o projeto como ele **existe hoje** (commit `8f092a5` em diante), com trechos do código real. Leia na ordem; cada parte usa a anterior.

---

## Parte 1. Os blocos básicos do mundo .NET

### O que é C# e o que é .NET
- **C#** é a linguagem em que escrevemos. Ela é *tipada*: toda variável tem um tipo (texto, número, data…) e o compilador recusa o que não combina. Isso pega erros antes de o programa rodar.
- **.NET** é a plataforma: o conjunto de bibliotecas prontas (ler arquivos, datas, JSON, consultas ao Windows) mais o programa que executa o seu código. Usamos o **.NET 10**.
- **SDK** é a caixa de ferramentas do desenvolvedor (`dotnet build`, `dotnet test`, `dotnet run`). O **runtime** é só o que executa. Quem usa o programa precisa só do runtime; nós precisamos do SDK.

### Solução, projeto, assembly
- **Solução** (`AutomationPlatform.sln`): a pasta-mãe que agrupa os projetos. Abrir a solução abre tudo.
- **Projeto** (`*.csproj`): um conjunto de arquivos `.cs` que compila junto num único resultado. O `.csproj` é um arquivo de texto (XML) que diz como compilar: qual versão do .NET, de quais projetos e pacotes depende.
- **Assembly** (`.dll` ou `.exe`): o resultado da compilação de um projeto. Cada projeto nosso vira um assembly.

Exemplo real, `Platform.Windows/...csproj`:

```xml
<ProjectReference Include="..\Contracts\...csproj" />        <!-- depende do projeto Contracts -->
<PackageReference Include="System.Management" Version="10.0.14" />  <!-- depende de um pacote da internet -->
<TargetFramework>net10.0-windows</TargetFramework>           <!-- só roda no Windows -->
<Nullable>enable</Nullable>                                  <!-- o compilador vigia valores nulos -->
<ImplicitUsings>enable</ImplicitUsings>                      <!-- importa automaticamente System, System.Linq, etc. -->
```

- **ProjectReference**: dependência entre projetos nossos. **PackageReference**: dependência de um pacote do **NuGet** (a "loja" de bibliotecas do .NET). Por segurança, só aceitamos pacotes com a sua autorização; hoje só existe o `System.Management`.
- **TargetFramework**: `net10.0` roda em qualquer sistema; `net10.0-windows` usa recursos do Windows. Por isso o núcleo é `net10.0` e só a parte que fala com o Windows é `-windows`.

### Namespace e `using`
- **Namespace** é o "sobrenome" de um tipo, para não haver confusão de nomes: `AutomationPlatform.Contracts`.
- `using AutomationPlatform.Contracts;` no topo do arquivo diz "pode usar os tipos desse namespace sem escrever o sobrenome inteiro".

---

## Parte 2. O mapa do projeto e o porquê

```
Host.Cli ──────────────┐
(terminal)             ▼
Platform.Windows ──► Diagnostics.Printing ──► Contracts ◄── Engine
(fala com o Windows)   (regras de impressão)   (o vocabulário)  (executa e protege)
                                                   ▲
Tests ─────────────────────────────────────────────┘ (testa todos)
```

A regra de ouro: **as setas apontam para o que é mais estável**. `Contracts` não depende de ninguém; todos dependem dele. Assim, mudar a interface do terminal nunca quebra as regras de impressão.

| Projeto | Para que serve | Analogia |
|---|---|---|
| **Contracts** | O vocabulário comum: relatório, achado, evidência, enums, `IDiagnostic` | A "língua" que todas as peças falam |
| **Engine** | Executa um diagnóstico com segurança: timeout, cancelamento, captura de erros; **sempre devolve um relatório** | O "gerente" que não deixa nada travar nem sumir |
| **Diagnostics.Printing** | As regras de impressão ("USB sem fila física = provável falta de driver") | O "especialista" em impressoras |
| **Platform.Windows** | Pergunta ao Windows (WMI) e entrega os dados ao especialista | O "tradutor" com o Windows |
| **Host.Cli** | A interface de terminal: comandos, narração em português, códigos de saída | O "balcão de atendimento" |
| **Tests** | 40 testes automáticos que provam que as regras funcionam | O "fiscal" |

**Por que separar tanto?** Três razões práticas:
1. **Testar sem Windows real:** o especialista (`Diagnostics.Printing`) não sabe o que é WMI; ele só pede dados por uma interface. Nos testes entregamos dados falsos.
2. **Trocar a "casca":** hoje o balcão é o terminal; amanhã é uma tela WPF e, um dia, o USE. O especialista não muda.
3. **Segurança:** a parte que mexe no sistema fica pequena, isolada e fácil de auditar.

---

## Parte 3. Os conceitos de C# que o código usa

Cada item mostra o conceito e onde ele aparece.

### 3.1 Classe, objeto e modificadores
Uma **classe** é um molde; um **objeto** é uma coisa criada a partir do molde.

```csharp
public sealed class DiagnosticRegistry { ... }
```
- `public`: qualquer projeto enxerga. `private`: só a própria classe.
- `sealed`: ninguém pode criar uma "filha" desta classe. Usamos por padrão: evita surpresas.
- `static`: pertence à classe, não a um objeto (ex.: `ReportSchema.CurrentVersion`).
- `const`: valor fixo escrito no código.
- `readonly`: o campo recebe valor uma vez e nunca muda (`private readonly IPrintDeviceProvider _provider;`).

### 3.2 Propriedade
Um "campo com porteiro": `get` lê, `set`/`init` escreve.
```csharp
public string DisplayName => "Reconhecimento de impressora pelo Windows";
```
O `=>` significa "é igual a": uma propriedade que só devolve esse valor.

### 3.3 Interface: o conceito mais importante do projeto
Uma **interface** é um *contrato*: lista o que alguém precisa saber fazer, sem dizer **como**.

```csharp
public interface IDiagnostic
{
    string Id { get; }
    string DisplayName { get; }
    Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken);
}
```
Qualquer classe que escreva `: IDiagnostic` **promete** ter essas três coisas. O `Engine` só conhece o contrato, então roda *qualquer* diagnóstico, inclusive os que ainda vamos escrever. Por convenção, nomes de interface começam com `I`.

**Inversão de dependência** é o truque que usamos o tempo todo:
```csharp
public interface IPrintDeviceProvider { ... }       // o especialista DECLARA o que precisa
public sealed class WmiPrintDeviceProvider : IPrintDeviceProvider { ... }   // o Windows ENTREGA
```
O especialista pede "dados de impressão" e não sabe de onde vêm. Em produção vêm do WMI; nos testes, de uma lista escrita à mão.

### 3.4 Injeção de dependência (a versão simples)
Em vez de a classe criar o que precisa, ela **recebe pronto** pelo construtor:
```csharp
public PrinterRecognitionDiagnostic(IPrintDeviceProvider provider) => _provider = provider;
```
E quem monta tudo é o `Program.cs`:
```csharp
registry.Register(new PrinterRecognitionDiagnostic(new WmiPrintDeviceProvider()));
```
É aqui que se decide "usar o WMI de verdade". Nos testes, a mesma linha usa um provedor falso.

### 3.5 `record`: classes para guardar dados
Um **record** é uma classe feita para ser um pacote de dados imutável (não muda depois de criado), com comparação por conteúdo.
```csharp
public sealed record PrintDeviceInfo(string Name, string DeviceId, int? ErrorCode);
```
Uma linha cria o tipo com três propriedades. Todo o nosso relatório (`DiagnosticReport`, `Finding`, `Evidence`…) são records.

### 3.6 `required`, `init` e inicializador de objeto
```csharp
public required string DiagnosticId { get; init; }
...
new Finding { Id = "f-1", Title = "...", Severity = Severity.High, ... }
```
- `required`: **obrigatório** preencher ao criar; o compilador reclama se esquecer.
- `init`: só pode ser definido na criação; depois fica travado.
Isso impede relatórios pela metade.

### 3.7 `null` e o `?`
`null` significa "sem valor". Com `Nullable` ligado, o compilador distingue:
- `string` = nunca é nulo. `string?` = pode ser nulo.
- `int?` em `ErrorCode` = "o Windows pode não ter informado". Isso é diferente de `0` (que quer dizer "sem erro"). Essa diferença é exatamente o princípio *ausência de evidência ≠ evidência de ausência*.
- `??` = "se for nulo, use isto": `o["Name"]?.ToString() ?? "(sem nome)"`.

### 3.8 `enum`
Uma lista fechada de opções: `ExecutionStatus` (`Completed`, `Failed`, `TimedOut`…). Melhor que texto solto, pois o compilador não deixa digitar uma opção que não existe.

### 3.9 Coleções, `var` e LINQ
- `IReadOnlyList<Finding>`: lista que o receptor só **lê**. `[]` é uma lista vazia.
- `var x = ...;` o compilador deduz o tipo.
- **LINQ** consulta listas como se fosse SQL:
```csharp
printers.Items.Where(PrinterClassifier.IsVirtualQueue).ToList();   // filtra
evidence.First(e => e.Description.Contains(...));                   // acha o primeiro
```
`e => ...` é uma **expressão lambda**: uma mini-função sem nome ("dado e, devolva isto").

### 3.10 Genéricos
`SourceResult<T>` é um molde com um "buraco" `T` para o tipo. `SourceResult<PrintDeviceInfo>` guarda dispositivos; `SourceResult<InstalledPrinterInfo>` guarda filas. Um código só, vários usos.

### 3.11 Assíncrono: `Task`, `async`, `await`
Consultar o WMI demora. Se o programa esperasse parado, a tela congelaria.
- `Task<T>` é uma "promessa" de um resultado que ainda vai chegar.
- `await` diz "espere a promessa, mas **sem travar** o programa; volte aqui quando chegar".
```csharp
var devices = await _provider.GetPrintDevicesAsync(cancellationToken);
```
Por convenção, métodos assíncronos terminam em `Async`.

### 3.12 `CancellationToken` e `IProgress<T>`
- **CancellationToken**: o "botão de cancelar" que passa de mão em mão. Quem faz trabalho longo olha o token (`ct.ThrowIfCancellationRequested()`). O Engine também o usa para o tempo limite de 30 s.
- **IProgress\<T\>**: um canal para o diagnóstico **avisar andamento** sem saber quem escuta. O terminal imprime a narração; os testes guardam numa lista.

### 3.13 Exceções: `try`, `catch`, `when`
Exceção é o jeito do .NET de dizer "algo deu errado".
```csharp
catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
```
`when` filtra: só captura *esse* tipo de erro. Nosso princípio: falha esperada vira dado (`SourceStatus.PermissionDenied`), falha inesperada vira relatório pelo Engine. **Nada é engolido em silêncio.**

### 3.14 Recursos modernos que aparecem
- **Pattern matching:** `device.ErrorCode is not (null or 0)` lê-se "não é nulo nem zero".
- **switch expression:** devolve valor conforme o caso (`Describe(SourceStatus)`).
- **Construtor primário:** `class FakeDiagnostic(Func<...> run)` declara o parâmetro direto no cabeçalho da classe.
- **`using var`:** libera o recurso (conexão, objeto do Windows) automaticamente ao sair do bloco.
- **Comentários `///`:** documentação que o editor mostra ao passar o mouse.

---

## Parte 4. Os testes

Usamos o **xUnit**. Um teste é um método marcado com `[Fact]` que prepara uma situação e confere o resultado:

```csharp
[Fact]
public async Task Id_inexistente_gera_falha_sem_lancar_excecao()
{
    var engine = new DiagnosticEngine(new DiagnosticRegistry());   // Arrange: prepara
    var report = await engine.RunAsync("nao.existe");              // Act: executa
    Assert.Equal(ExecutionStatus.Failed, report.ExecutionStatus);  // Assert: confere
}
```
É o padrão **Arrange / Act / Assert**. Um **fake** (ex.: `FakeDiagnostic`, `FakePrintDeviceProvider`) é uma peça de mentira que se comporta como mandarmos. Por isso testamos "spooler parado" sem precisar derrubar o spooler de verdade. `dotnet test` roda os 40 e mostra quantos passaram.

---

## Parte 5. O caminho completo de uma execução real

`dotnet run --project Host.Cli -- run printing.device-recognition`

1. **`Program.cs`** monta o registro e entrega ao `CliApp`.
2. **`CliApp`** lê o comando, pede ao `DiagnosticEngine` para rodar o id.
3. **`DiagnosticEngine`** acha o diagnóstico no `DiagnosticRegistry`, liga o cronômetro de 30 s e chama `RunAsync`.
4. **`PrinterRecognitionDiagnostic`** pede dados ao `IPrintDeviceProvider` e aplica as regras.
5. **`WmiPrintDeviceProvider`** faz as consultas `SELECT` no WMI e devolve `SourceResult`.
6. O diagnóstico devolve um `DiagnosticOutcome` (conclusão + evidências).
7. O **Engine** o embrulha num `DiagnosticReport` (com horários e status de execução).
8. **`ReportPrinter`** imprime em português, ou o `ReportSerializer` gera o JSON (`--json`), e o programa termina com o código 0/1/2/3.

Repare: se qualquer passo falhar, o Engine ainda devolve um relatório. É a garantia central do projeto.

---

## Parte 6. Conceitos de arquitetura (nomes para guardar)

- **Separação de responsabilidades:** cada projeto faz uma coisa.
- **Inversão de dependência:** o núcleo declara a interface; o exterior a implementa.
- **Injeção de dependência:** as peças chegam prontas pelo construtor.
- **Imutabilidade:** dados que não mudam depois de criados evitam bugs.
- **Fail-safe / falhar às claras:** erro vira relatório, nunca silêncio.
- **Menor privilégio:** a leitura roda sem poder; só a correção sobe de nível (próximas fases).
- **Esquema versionado:** o JSON tem `schemaVersion` para evoluir sem quebrar quem lê.

---

## Parte 7. Para fixar (exercícios rápidos)

1. Em `PrinterRecognitionDiagnostic`, qual a diferença entre `ErrorCode == 0` e `ErrorCode == null`? Por que isso importa?
2. Por que `Diagnostics.Printing` **não** referencia o `System.Management`?
3. Se criarmos um segundo diagnóstico, o que precisa mudar no `Engine`? (Resposta: nada.)
4. Por que o relatório usa `record` com `init` em vez de classe com `set`?
5. O que acontece, passo a passo, se o WMI negar a permissão?

Respostas e dúvidas: me pergunte em qualquer ponto; esta aula vai crescendo conforme as fases novas entram.
