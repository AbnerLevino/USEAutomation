using System.Text;
using AutomationPlatform.Host.Cli;
using AutomationPlatform.Platform.Windows;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

return await CliApp.RunAsync(args, PrintingComposition.CreateRegistry(), Console.Out, Console.Error);
