namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// As causas em linguagem de quem não é técnico: sem "fila", "Spooler" nem "porta". Os títulos de
/// <see cref="ProbableCause"/> continuam técnicos (servem ao terminal, ao JSON e ao suporte); estas frases são
/// o que o USUÁRIO lê. Uma só fonte para a janela e para o fluxo.
/// </summary>
public static class CauseWording
{
    /// <summary>Uma frase curta, com o nome da impressora quando fizer sentido. <paramref name="fallback"/> cobre causas novas.</summary>
    public static string Plain(string code, string? printerName, string fallback)
    {
        var printer = printerName is null ? "a impressora" : $"a impressora '{printerName}'";
        return code switch
        {
            CauseCodes.SpoolerNotRunning => "O serviço que cuida das impressões neste computador está parado.",
            CauseCodes.PrinterPaused => $"{Cap(printer)} está pausada, por isso nada sai dela.",
            CauseCodes.PrinterOffline => $"O computador está vendo {printer} como desligada ou desconectada.",
            CauseCodes.PrinterNoPort => $"{Cap(printer)} não está ligada a nenhuma saída do computador.",
            CauseCodes.DeviceError => "O Windows está apontando um erro com a impressora (driver ou conexão).",
            CauseCodes.StuckJobs => $"Há documentos travados esperando para sair em {printer}.",
            CauseCodes.DefaultIsVirtual => "Os documentos estão indo para uma impressora que só gera arquivo (PDF), e não para uma impressora de verdade.",
            CauseCodes.NoDefaultPrinter => "Nenhuma impressora está marcada como a principal do computador.",
            CauseCodes.EquipmentError => $"{Cap(printer)} está avisando um problema: {EquipmentIssue(fallback)}.",
            _ => fallback
        };
    }

    /// <summary>O pedido a quem está diante da impressora (a única coisa que a automação não faz sozinha).</summary>
    public static string AtTheEquipment(string? printerName, string description) =>
        $"Olhe a impressora{(printerName is null ? "" : $" '{printerName}'")}: ela avisa {EquipmentIssue(description)}. Quando resolver, clique em \"Já fiz\" que eu confiro de novo.";

    /// <summary>Tira o problema ("papel atolado") da descrição técnica "Segundo o Windows, no equipamento 'X': papel atolado.".</summary>
    private static string EquipmentIssue(string description)
    {
        var at = description.LastIndexOf("': ", StringComparison.Ordinal);
        var issue = at >= 0 ? description[(at + 3)..] : description;
        return issue.TrimEnd('.', ' ');
    }

    private static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
