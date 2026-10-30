using System.Threading.Channels;
using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Tests;

public class WindowInteractionTests
{
    private static (WindowInteraction Interaction, Channel<ScreenItem> Queue) Create()
    {
        var queue = Channel.CreateUnbounded<ScreenItem>();
        return (new WindowInteraction(queue.Writer), queue);
    }

    [Fact]
    public async Task Pergunta_vai_para_a_fila_e_espera_a_resposta_da_tela()
    {
        var (interaction, queue) = Create();

        var asking = interaction.AskIfResolvedAsync("Voltou?", CancellationToken.None);

        Assert.False(asking.IsCompleted);
        var item = Assert.IsType<ResolvedItem>(await queue.Reader.ReadAsync());
        Assert.Equal("Voltou?", item.Question);
        item.Reply.SetResult(ResolutionAnswer.Worked);
        Assert.Equal(ResolutionAnswer.Worked, await asking);
    }

    [Fact]
    public async Task Cada_tipo_de_pergunta_vira_o_item_certo()
    {
        var (interaction, queue) = Create();
        var prompt = new ConfirmationPrompt("a", "T", "texto", ActionLevel.N1);
        var options = new[] { new Choice("x", "X") };

        _ = interaction.ConfirmAsync(prompt, CancellationToken.None);
        _ = interaction.ChooseAsync("Qual?", options, CancellationToken.None);
        _ = interaction.RequestPhysicalActionAsync("Ligue.", CancellationToken.None);

        Assert.IsType<ConfirmItem>(await queue.Reader.ReadAsync());
        Assert.Equal("Qual?", Assert.IsType<ChooseItem>(await queue.Reader.ReadAsync()).Question);
        Assert.Equal("Ligue.", Assert.IsType<PhysicalItem>(await queue.Reader.ReadAsync()).Instruction);
    }

    [Fact]
    public async Task Cancelar_enquanto_espera_cancela_a_pergunta_pendente()
    {
        var (interaction, _) = Create();
        using var cts = new CancellationTokenSource();

        var asking = interaction.ConfirmAsync(new ConfirmationPrompt("a", "T", "x", ActionLevel.N1), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
    }

    [Fact]
    public async Task Fila_ja_fechada_nao_deixa_a_pergunta_esperando_para_sempre()
    {
        var (interaction, queue) = Create();
        queue.Writer.Complete();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => interaction.AskIfResolvedAsync("Voltou?", CancellationToken.None));
    }

    [Fact]
    public async Task Token_ja_cancelado_nem_chega_a_perguntar()
    {
        var (interaction, queue) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => interaction.RequestPhysicalActionAsync("Ligue.", cts.Token));
        Assert.False(queue.Reader.TryRead(out _));
    }
}
