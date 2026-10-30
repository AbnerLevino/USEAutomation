using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Host.Desktop.Guided;

/// <summary>O que o computador "fala" para cada evento, e os textos dos botões. Só texto: dá para testar.</summary>
public static class GuidedSpeech
{
    /// <summary>
    /// A linha que a tela mostra para um evento; null = o evento só atualiza o quadro, sem linha. A linguagem é a de
    /// quem não é técnico: as observações técnicas das verificações (<see cref="TechnicalNote"/>) não aparecem aqui.
    /// </summary>
    public static string? For(GuidedEvent e) => e switch
    {
        Narration n => n.Message,
        StepStarted s => StepText(s.Key, s.Label),
        CauseFound c => CauseWording.Plain(c.Code, c.PrinterName, c.Description),
        NoCauseFound n => n.Message,
        ActionFinished a => ActionText(a.Record),
        _ => null
    };

    private static string StepText(string key, string label) => key switch
    {
        "spooler" => "Verificando se o serviço de impressão do computador está funcionando.",
        "state" => "Vendo como estão as impressoras.",
        "queue" => "Procurando documentos travados esperando para imprimir.",
        "device" => "Conferindo se o computador reconhece a impressora.",
        "default" => "Conferindo qual impressora o computador usa como principal.",
        "port" => "Conferindo a ligação das impressoras com o computador.",
        _ => $"Verificando: {label}."
    };

    /// <summary>O resultado de uma ação em palavras simples. Uma ação recusada pelo usuário não gera linha.</summary>
    private static string? ActionText(ActionRecord r) => r.Status switch
    {
        ActionStatus.Resolved => $"Pronto! {Cap(r.VerificationDetail ?? r.Summary)}",
        ActionStatus.NotResolved => $"Não deu certo: {r.VerificationDetail ?? r.Summary}",
        ActionStatus.Inconclusive => Cap(r.VerificationDetail ?? r.Summary),
        ActionStatus.PreconditionFailed => $"Não foi preciso mudar nada: {r.PreconditionDetail ?? r.Summary}",
        ActionStatus.ExecutionFailed => $"Não consegui fazer isso: {r.ExecutionDetail ?? "o Windows não permitiu."}",
        ActionStatus.ElevationDenied => "Para continuar é preciso a aprovação de um administrador. Nada foi alterado.",
        _ => null
    };

    private static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>O texto da pergunta de confirmação, com os avisos de administrador e de perda de dados quando couber.</summary>
    public static string ConfirmText(ConfirmationPrompt prompt)
    {
        var text = prompt.Text;
        if (prompt.WarnsDataLoss)
            text += " Atenção: isso pode apagar trabalhos de impressão que ainda não saíram.";
        if (prompt.MayNeedAdministrator)
            text += " Pode aparecer um aviso do Windows pedindo a aprovação de um administrador; se não for aprovado, nada será alterado.";
        return text;
    }

    public const string Yes = "Sim, pode fazer";
    public const string NotNow = "Agora não";
    public const string Cancel = "Cancelar";
    public const string Done = "Já fiz";
    public const string CouldNot = "Não consegui";
    public const string Worked = "Sim";
    public const string StillBroken = "Não";
    public const string CannotTest = "Não consegui verificar";

    // Enquanto o relatório não for enviado a lugar nenhum, o texto não pode dizer que foi: só diz o que é verdade.
    public const string FeedbackTitle = "Como foi o atendimento?";
    public const string FeedbackInfo = "Sua avaliação e o resultado deste atendimento ficam registrados no relatório.";
    public const string FeedbackComment = "Comentário (opcional)";
    public const string Finish = "Concluir atendimento";
    public const string Closed = "Atendimento encerrado. Obrigado!";
}
