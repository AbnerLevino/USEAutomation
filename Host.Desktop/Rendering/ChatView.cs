using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPlatform.Host.Desktop.Chat;

namespace AutomationPlatform.Host.Desktop.Rendering;

/// <summary>
/// Desenha a conversa: bolhas, pontinhos de "digitando", cartões e o texto surgindo palavra por palavra.
/// Só desenha; não conhece diagnóstico, fluxo nem regra de negócio. Todas as esperas respeitam o
/// <c>closing</c>, para a janela poder fechar a qualquer momento.
/// </summary>
public sealed class ChatView(ScrollViewer scroll, Panel chat, ChatPacing pacing, CancellationToken closing)
{
    public ChatPacing Pacing => pacing;

    public Task WaitAsync(TimeSpan time) => time > TimeSpan.Zero ? Task.Delay(time, closing) : Task.CompletedTask;

    /// <summary>Faz o texto aparecer palavra por palavra, com a rolagem acompanhando.</summary>
    public async Task StreamAsync(TextBlock target, string text)
    {
        foreach (var token in ChatPacing.Tokens(text))
        {
            Motion.AppendFading(target, token);
            SmoothScroll.ToEnd(scroll);
            await WaitAsync(pacing.Delay(token));
        }
    }

    /// <summary>Bolha do computador com os pontinhos de "digitando" animados. A MESMA bolha vira a fala.</summary>
    public Border ShowTyping()
    {
        DimPrevious();
        var bubble = BubbleShell(fromComputer: true);
        bubble.Child = Motion.TypingDots(Brush("#9CA3AF"));
        Append(bubble);
        return bubble;
    }

    /// <summary>Uma fala do computador (ou do usuário) já com o texto pronto, sem animação de digitação.</summary>
    public Border AddBubble(bool fromComputer, out TextBlock text)
    {
        text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush(fromComputer ? "#111827" : "#FFFFFF") };
        DimPrevious();
        var bubble = BubbleShell(fromComputer);
        bubble.Child = text;
        Append(bubble);
        return bubble;
    }

    /// <summary>Troca os pontinhos pela fala, que surge palavra por palavra.</summary>
    public async Task SpeakAsync(Border typingBubble, string text)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#111827") };
        typingBubble.Child = block;
        await StreamAsync(block, text);
    }

    public void Remove(UIElement element) => chat.Children.Remove(element);

    /// <summary>O cartão de conclusão: título colorido pelo tom e as linhas de detalhe.</summary>
    public async Task ShowCardAsync(ChatMessage message)
    {
        DimPrevious();
        var color = message.Tone switch
        {
            ChatTone.Good => "#16A34A",
            ChatTone.Problem => "#DC2626",
            ChatTone.Error => "#B45309",
            _ => "#6B7280"
        };

        var body = new StackPanel();
        var card = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush(color),
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 4, 0, 12),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = body
        };
        Append(card);

        var headline = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 16, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(headline);
        await StreamAsync(headline, message.Text);

        var rest = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Detail)) rest.Add(message.Detail);
        rest.AddRange(message.Lines ?? []);
        foreach (var line in rest)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#111827") };
            body.Children.Add(block);
            await StreamAsync(block, line);
        }
        SmoothScroll.ToEnd(scroll);
    }

    /// <summary>Tudo o que já está na conversa fica mais apagado quando uma fala nova chega.</summary>
    private void DimPrevious()
    {
        foreach (var child in chat.Children)
            if (child is Border border) Motion.Dim(border.Child);
    }

    private void Append(FrameworkElement element)
    {
        chat.Children.Add(element);
        Motion.Enter(element);
        SmoothScroll.ToEnd(scroll);
    }

    private static Border BubbleShell(bool fromComputer) => new()
    {
        Background = Brush(fromComputer ? "#FFFFFF" : "#2563EB"),
        BorderBrush = Brush("#E5E7EB"),
        BorderThickness = new Thickness(fromComputer ? 1 : 0),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8),
        MaxWidth = 440,
        HorizontalAlignment = fromComputer ? HorizontalAlignment.Left : HorizontalAlignment.Right
    };

    public static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
