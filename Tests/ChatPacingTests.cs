using AutomationPlatform.Host.Desktop.Chat;

namespace AutomationPlatform.Tests;

public class ChatPacingTests
{
    [Theory]
    [InlineData("Olá! Escolha uma opção.")]
    [InlineData("  espaços   no início e no fim  ")]
    [InlineData("uma")]
    [InlineData("Fila 'TESTE Pausada': 1 trabalho(s), 0 preso(s).\nOutra linha.")]
    [InlineData("")]
    public void Juntar_as_palavras_devolve_exatamente_o_texto_original(string text) =>
        Assert.Equal(text, string.Concat(ChatPacing.Tokens(text)));

    [Fact]
    public void Texto_e_dividido_palavra_por_palavra()
    {
        var tokens = ChatPacing.Tokens("Vou conferir o Spooler.").ToList();

        Assert.Equal(["Vou ", "conferir ", "o ", "Spooler."], tokens);
    }

    [Fact]
    public void Pensar_demora_mais_para_fala_longa_mas_tem_teto()
    {
        var pacing = ChatPacing.Natural;

        var curta = pacing.Thinking("Oi.");
        var longa = pacing.Thinking(new string('a', 100));
        var enorme = pacing.Thinking(new string('a', 5000));

        Assert.True(longa > curta);
        Assert.Equal(pacing.ThinkingMax, enorme);
    }

    [Fact]
    public void Respira_mais_no_fim_de_frase_que_no_meio()
    {
        var pacing = ChatPacing.Natural;

        var meio = pacing.Delay("palavra ");
        var virgula = pacing.Delay("palavra, ");
        var fim = pacing.Delay("palavra. ");

        Assert.True(virgula > meio);
        Assert.True(fim > virgula);
        Assert.Equal(pacing.Delay("Fim?"), pacing.Delay("Fim."));
    }

    [Fact]
    public void Ritmo_instantaneo_nao_espera_nada()
    {
        var pacing = ChatPacing.Instant;

        Assert.Equal(TimeSpan.Zero, pacing.Thinking("qualquer texto longo aqui"));
        Assert.Equal(TimeSpan.Zero, pacing.Delay("fim."));
        Assert.Equal(TimeSpan.Zero, pacing.Reading("uma fala qualquer"));
        Assert.Equal(TimeSpan.Zero, pacing.AfterUserMessage);
    }

    [Fact]
    public void Depois_da_escolha_do_usuario_ha_uma_pausa_perceptivel_mas_curta()
    {
        var pause = ChatPacing.Natural.AfterUserMessage;

        Assert.InRange(pause.TotalMilliseconds, 400, 1500);
    }

    [Fact]
    public void Tempo_de_leitura_cresce_com_a_fala_anterior_e_tem_teto()
    {
        var pacing = ChatPacing.Natural;

        var curta = pacing.Reading("Oi.");
        var longa = pacing.Reading(new string('a', 80));
        var enorme = pacing.Reading(new string('a', 5000));

        Assert.True(longa > curta);
        Assert.Equal(pacing.ReadingMax, enorme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_fala_anterior_nao_ha_o_que_ler(string? previous) =>
        Assert.Equal(TimeSpan.Zero, ChatPacing.Natural.Reading(previous));

    [Fact]
    public void Ritmo_natural_deixa_uma_frase_tipica_levar_alguns_segundos_no_maximo()
    {
        // Uma narração típica (~12 palavras) não pode demorar tanto a ponto de cansar.
        var pacing = ChatPacing.Natural;
        var text = "Vou conferir se o serviço de impressão do Windows (Spooler) está em execução.";

        var total = pacing.Thinking(text) + ChatPacing.Tokens(text).Aggregate(TimeSpan.Zero, (sum, t) => sum + pacing.Delay(t));

        Assert.InRange(total.TotalSeconds, 0.5, 3.0);
    }
}
