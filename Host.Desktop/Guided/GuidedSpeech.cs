using AutomationPlatform.Contracts;

namespace AutomationPlatform.Host.Desktop.Guided;

/// <summary>O que o computador "fala" para cada evento, e os textos dos botões. Só texto: dá para testar.</summary>
public static class GuidedSpeech
{
    /// <summary>A fala de um evento; null = o evento só atualiza o quadro, sem fala.</summary>
    public static string? For(GuidedEvent e) => e switch
    {
        Narration n => n.Message,
        // O título já foi dito pela narração do cenário ("Causa provável: ..."); aqui só se explica o porquê.
        CauseFound c => c.Description,
        NoCauseFound n => n.Message,
        ActionFinished a => a.Record.Summary,
        _ => null
    };

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
