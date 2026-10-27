using System.Text;
using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Host.Cli;
using AutomationPlatform.Platform.Windows;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var registry = new DiagnosticRegistry();
registry.Register(new PrinterRecognitionDiagnostic(new WmiPrintDeviceProvider()));

return await CliApp.RunAsync(args, registry, Console.Out, Console.Error);
