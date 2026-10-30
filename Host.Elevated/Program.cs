using AutomationPlatform.Platform.Windows.Elevation;

// O executor elevado: aceita UM pedido da lista fixa, faz, e termina. Sem janela, sem menu, sem comando livre.
// O resultado volta só pelo código de saída (ver ElevatedProtocol); não escreve em arquivo nenhum.
// Quem mostra o aviso do administrador é o Windows, antes de este programa começar.
var runner = new ElevatedActionRunner(new WmiServiceControl(), new WindowsElevationProbe());
return await runner.RunAsync(args, CancellationToken.None);
