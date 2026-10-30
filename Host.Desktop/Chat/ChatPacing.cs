using System.Text.RegularExpressions;

namespace AutomationPlatform.Host.Desktop.Chat;

/// <summary>
/// O "ritmo" de uma conversa de assistente: uma pausa de "pensando" antes de cada fala e o texto
/// aparecendo palavra por palavra, com um respiro maior depois da pontuação. Só o comportamento;
/// sem tela, então dá para testar.
/// </summary>
public sealed record ChatPacing(
    TimeSpan ThinkingBase, TimeSpan ThinkingPerChar, TimeSpan ThinkingMax, TimeSpan PerToken)
{
    /// <summary>Pausa depois que o usuário escolhe, antes de o computador começar a "digitar".</summary>
    public TimeSpan AfterUserMessage { get; init; }

    /// <summary>Pausa depois de uma etapa terminar ou de uma impressora mudar de estado, para o olho acompanhar o quadro.</summary>
    public TimeSpan AfterStep { get; init; }

    /// <summary>Tempo para a pessoa LER a fala anterior antes de a próxima começar: base + por letra, com teto.</summary>
    public TimeSpan ReadingBase { get; init; }
    public TimeSpan ReadingPerChar { get; init; }
    public TimeSpan ReadingMax { get; init; }

    /// <summary>Ritmo normal, para uma pessoa acompanhar e ter tempo de ler.</summary>
    public static ChatPacing Natural { get; } = new(
        TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(8), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(90))
    {
        AfterUserMessage = TimeSpan.FromMilliseconds(1000),
        AfterStep = TimeSpan.FromMilliseconds(450),
        ReadingBase = TimeSpan.FromMilliseconds(500),
        ReadingPerChar = TimeSpan.FromMilliseconds(15),
        ReadingMax = TimeSpan.FromMilliseconds(2500)
    };

    /// <summary>Sem espera nenhuma, para testes automáticos da janela.</summary>
    public static ChatPacing Instant { get; } = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>Pausa de leitura depois de uma fala: mais para falas longas, com teto. Sem fala anterior, não espera.</summary>
    public TimeSpan Reading(string? previousText)
    {
        if (string.IsNullOrWhiteSpace(previousText)) return TimeSpan.Zero;
        var wanted = ReadingBase + ReadingPerChar * previousText.Length;
        return wanted < ReadingMax ? wanted : ReadingMax;
    }

    /// <summary>Quanto "pensar" antes de responder: mais para falas longas, com teto.</summary>
    public TimeSpan Thinking(string text)
    {
        var wanted = ThinkingBase + ThinkingPerChar * text.Length;
        return wanted < ThinkingMax ? wanted : ThinkingMax;
    }

    /// <summary>Espera depois de mostrar uma palavra: respira mais no fim de frase e um pouco após vírgula.</summary>
    public TimeSpan Delay(string token)
    {
        var end = token.TrimEnd();
        if (end.Length == 0) return PerToken;
        return end[^1] switch
        {
            '.' or '!' or '?' => PerToken * 7,
            ',' or ';' or ':' => PerToken * 3,
            _ => PerToken
        };
    }

    /// <summary>Quebra em palavras guardando os espaços: juntar tudo devolve o texto original.</summary>
    public static IEnumerable<string> Tokens(string text) =>
        Regex.Matches(text, @"\S+\s*|\s+").Select(m => m.Value);
}
