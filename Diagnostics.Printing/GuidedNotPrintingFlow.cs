using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// O fluxo guiado do botão "Impressora não imprime": investiga (só lê), pergunta qual impressora quando há
/// dúvida, mostra a causa, corrige com a confirmação do usuário, confere de novo, oferece a página de teste e
/// fecha com a opinião do usuário. Tudo que a tela precisa chega como <see cref="GuidedEvent"/>; tudo que
/// depende do usuário passa por <see cref="IUserInteraction"/>.
/// <para>Quem age é a automação: o usuário só confirma. O que ela não corrige vai para o suporte; só um erro do
/// próprio equipamento (papel atolado...) é pedido a uma pessoa.</para>
/// <para>Garantias: nada é alterado sem confirmação; "resolvido" só sai da resposta do usuário ou de uma leitura
/// objetiva (e o relatório diz qual das duas); cancelar a qualquer momento não altera nada; sempre devolve um relatório.</para>
/// <para>Limite desta versão: trata UMA causa por execução (a principal). Se outra aparecer depois, o fechamento avisa.</para>
/// </summary>
public sealed class GuidedNotPrintingFlow(
    NotPrintingScenario scenario,
    IActionCatalog catalog,
    IActionRunner runner,
    IUserInteraction user,
    TimeProvider? clock = null,
    TimeSpan? collectTimeout = null)
{
    private static readonly TimeSpan DefaultCollectTimeout = TimeSpan.FromSeconds(30);
    private const string Headline = "Sintoma investigado: impressora não está imprimindo.";
    private const string AnyPrinter = "*";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TimeSpan _collectTimeout = collectTimeout ?? DefaultCollectTimeout;

    /// <summary>Quais impressoras entram na análise: o padrão do cenário, uma escolhida, ou todas as físicas ("não sei qual").</summary>
    private sealed record PrinterScope(string? Only = null, bool AllPhysical = false)
    {
        public static PrinterScope Default { get; } = new();
    }

    /// <summary>As perguntas e frases do fechamento humano: uma para "voltou a imprimir?", outra para "a página saiu?".</summary>
    private sealed record ClosingTexts(string Question, string Worked, string StillBroken)
    {
        public static ClosingTexts Printing { get; } = new(
            "A impressão voltou a funcionar?",
            "Você confirmou que a impressão voltou a funcionar.",
            "Você informou que a impressão continua sem funcionar. Procure o suporte.");

        public static ClosingTexts TestPage { get; } = new(
            "A página de teste saiu na impressora?",
            "Você confirmou que a página de teste saiu.",
            "Você informou que a página de teste não saiu. Procure o suporte.");
    }

    public async Task<DiagnosticReport> RunAsync(IProgress<GuidedEvent> events, CancellationToken cancellationToken = default)
    {
        var started = _clock.GetUtcNow();
        var actions = new List<ActionRecord>();
        try
        {
            return await RunCoreAsync(started, events, actions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(started, actions, ExecutionStatus.Cancelled, "O diagnóstico foi cancelado antes de terminar.", "Cancelado pelo solicitante.");
        }
        catch (TimeoutException)
        {
            return Failure(started, actions, ExecutionStatus.TimedOut, "As verificações excederam o tempo limite e foram interrompidas.",
                $"Tempo limite de {_collectTimeout.TotalSeconds:0.#} s excedido.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(started, actions, ExecutionStatus.PermissionDenied, "Sem permissão para coletar os dados necessários.", Describe(ex));
        }
        catch (Exception ex)
        {
            return Failure(started, actions, ExecutionStatus.Failed, "Ocorreu uma falha técnica durante o diagnóstico.", Describe(ex));
        }
    }

    private async Task<DiagnosticReport> RunCoreAsync(
        DateTimeOffset started, IProgress<GuidedEvent> events, List<ActionRecord> actions, CancellationToken ct)
    {
        Say(events, "Vou olhar o que está acontecendo. Por enquanto só observo: não mudo nada no seu computador.");
        var (snapshot, analysis) = await AnalyzeAsync(events, PrinterScope.Default, ct);

        // 1. Mais de uma impressora física: o motor não sabe qual é a do usuário, e o padrão não prova isso.
        var scope = PrinterScope.Default;
        var physical = PhysicalNames(snapshot);
        if (physical.Count > 1)
        {
            var options = physical.Select(n => new Choice(n, n)).Append(new Choice(AnyPrinter, "Não sei qual")).ToList();
            var choice = await user.ChooseAsync("Encontrei mais de uma impressora. Qual delas está com problema?", options, ct);
            if (choice is null)
                return Finish(started, actions, analysis, snapshot,
                    new FlowClosing(FlowResult.Cancelled, ClosingBasis.None, "Você cancelou. Nada foi alterado."), events);

            scope = choice.Id == AnyPrinter ? new PrinterScope(AllPhysical: true) : new PrinterScope(Only: choice.Id);
            analysis = Narrow(snapshot, analysis, scope);
        }

        PublishPrinterStates(events, snapshot, analysis);

        // 2. Sem causa: o motor não finge que está tudo bem. A página de teste deixa o usuário conferir com os próprios olhos.
        if (analysis.Primary is not { } primary)
        {
            const string message = "Não encontrei nenhum problema no que verifiquei. Isso não garante que a impressão esteja funcionando: se continuar sem imprimir, procure o suporte.";
            events.Report(new NoCauseFound(message));
            var closingNoCause = await OfferTestPageAsync(
                SoleTarget(snapshot, analysis, scope), FlowResult.Inconclusive, actions, events, ct)
                ?? new FlowClosing(FlowResult.Inconclusive, ClosingBasis.None, message);
            return Finish(started, actions, analysis, snapshot, closingNoCause, events);
        }

        events.Report(new CauseFound(primary.Code, primary.Title, primary.Description, primary.Guidance, primary.PrinterName, primary.CandidateActionId));

        // 3. Quem age é a automação, não o usuário:
        //    - correção disponível: pipeline (confirma, executa, verifica, confere de novo);
        //    - só uma pessoa diante do equipamento resolve (papel atolado, tampa aberta): pede e reverifica;
        //    - o resto é técnico: assume o problema e entrega o relatório ao suporte.
        var action = primary.CandidateActionId is { } id ? catalog.Find(id) : null;
        var closing = action is not null
            ? await FixAsync(action, primary, scope, actions, events, ct)
            : NeedsPersonAtEquipment(primary)
                ? await GuideAsync(primary, scope, actions, events, ct)
                : HandOffToSupport(primary, events);

        return Finish(started, actions, analysis, snapshot, closing, events);
    }

    // ---------- caminho da correção ----------

    private async Task<FlowClosing> FixAsync(
        IRemediationAction action, ProbableCause cause, PrinterScope scope,
        List<ActionRecord> actions, IProgress<GuidedEvent> events, CancellationToken ct)
    {
        Say(events, "Sei como resolver isso.");

        // Depois do "sim" do usuário, a tela já mostra que a impressora está sendo corrigida.
        var confirmation = new AnnounceOnConfirm(user, () =>
        {
            if (cause.PrinterName is { } name)
                events.Report(new PrinterStateChanged(name, PrinterCardState.Unknown, "corrigindo…"));
            Say(events, "Certo, vou fazer isso agora.");
        });

        var record = await runner.RunAsync(action, new ActionTarget(cause.PrinterName), confirmation, ct);
        actions.Add(record);
        events.Report(new ActionFinished(record));

        switch (record.Status)
        {
            case ActionStatus.NotConfirmed:
                // Voltou ao que era: o cartão mostra o problema de novo.
                if (cause.PrinterName is { } declined)
                    events.Report(new PrinterStateChanged(declined, PrinterCardState.Problem, ShortLabel(cause)));
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, "Você preferiu não corrigir agora. O problema continua e nada foi alterado.");
            case ActionStatus.PreconditionFailed:
                return new FlowClosing(FlowResult.Inconclusive, ClosingBasis.None, record.Summary);
            case ActionStatus.ElevationDenied or ActionStatus.ExecutionFailed:
                if (cause.PrinterName is { } failed)
                    events.Report(new PrinterStateChanged(failed, PrinterCardState.Problem, ShortLabel(cause)));
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, record.Summary);
        }

        // A ação rodou. O usuário vê a conferência acontecer: as seis verificações rodam de novo e os cartões mudam.
        Say(events, "Vou conferir de novo para ter certeza.");
        var (snapshot, analysis) = await AnalyzeAsync(events, scope, ct);
        PublishPrinterStates(events, snapshot, analysis);

        var objective = record.Status switch
        {
            ActionStatus.Resolved => FlowResult.Resolved,
            ActionStatus.NotResolved => FlowResult.NotResolved,
            _ => FlowResult.Inconclusive
        };

        // Consertou: a página de teste deixa o usuário ver o papel sair, sem abrir o Painel de Controle.
        if (objective == FlowResult.Resolved
            && await OfferTestPageAsync(cause.PrinterName, objective, actions, events, ct) is { } viaTestPage)
            return viaTestPage;

        return await CloseWithUserAsync(objective, ClosingTexts.Printing, ct);
    }

    // ---------- página de teste ----------

    /// <summary>
    /// Oferece a página de teste (se a ação existe e a impressora é uma só). Devolve o fechamento quando a página
    /// foi enviada e o usuário respondeu; null quando não houve página (recusou, indisponível ou falhou).
    /// </summary>
    private async Task<FlowClosing?> OfferTestPageAsync(
        string? printer, FlowResult objective,
        List<ActionRecord> actions, IProgress<GuidedEvent> events, CancellationToken ct)
    {
        if (printer is null || catalog.Find(PrinterActionIds.PrintTestPage) is not { } testPage) return null;

        var record = await runner.RunAsync(testPage, new ActionTarget(printer), user, ct);
        actions.Add(record);
        if (record.Status == ActionStatus.NotConfirmed) return null;   // não quis: sem fala sobre "ação não confirmada"

        events.Report(new ActionFinished(record));
        if (record.Status is not (ActionStatus.Resolved or ActionStatus.Inconclusive)) return null;

        return await CloseWithUserAsync(objective, ClosingTexts.TestPage, ct);
    }

    /// <summary>Em "não sei qual" ou com várias físicas avaliadas não há uma impressora só para a página de teste.</summary>
    private static string? SoleTarget(PrintingSnapshot snapshot, ScenarioAnalysis analysis, PrinterScope scope)
    {
        if (scope.Only is not null) return scope.Only;
        var physicalTargets = analysis.TargetPrinters.Where(n => IsPhysical(snapshot, n)).ToList();
        return physicalTargets.Count == 1 ? physicalTargets[0] : null;
    }

    // ---------- caminho do suporte ----------

    /// <summary>
    /// Só uma pessoa diante do equipamento resolve: erro que o próprio equipamento reporta (papel, tampa...)
    /// e que a automação não tem como corrigir. Qualquer causa com correção conhecida ou técnica NÃO entra aqui:
    /// o usuário não deve receber tarefa técnica.
    /// </summary>
    private static bool NeedsPersonAtEquipment(ProbableCause cause) =>
        cause.CandidateActionId is null && cause.Code == CauseCodes.EquipmentError;

    /// <summary>A automação descobriu o problema mas não corrige sozinha: assume e deixa o relatório para o suporte.</summary>
    private static FlowClosing HandOffToSupport(ProbableCause cause, IProgress<GuidedEvent> events)
    {
        var message = $"Encontrei o problema: {CauseWording.Plain(cause.Code, cause.PrinterName, cause.Description)} " +
                      "Ainda não consigo resolver isso por aqui. Procure o suporte; os detalhes ficam registrados no relatório.";
        Say(events, message);
        return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, message);
    }

    // ---------- caminho do equipamento (uma pessoa precisa olhar a impressora) ----------

    private async Task<FlowClosing> GuideAsync(
        ProbableCause cause, PrinterScope scope, List<ActionRecord> actions, IProgress<GuidedEvent> events, CancellationToken ct)
    {
        while (true)
        {
            var reply = await user.RequestPhysicalActionAsync(CauseWording.AtTheEquipment(cause.PrinterName, cause.Description), ct);
            if (reply == PhysicalActionReply.Cancel)
                return new FlowClosing(FlowResult.Cancelled, ClosingBasis.None, "Você cancelou. Nada foi alterado.");
            if (reply == PhysicalActionReply.Skipped)
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, "Você não fez isso agora. O problema continua.");

            Say(events, "Obrigado. Vou conferir de novo.");
            var (snapshot, analysis) = await AnalyzeAsync(events, scope, ct);
            // A nova rodada reenvia as impressoras no estado inicial: os cartões precisam do estado atualizado.
            PublishPrinterStates(events, snapshot, analysis);

            if (analysis.Primary is { } now)
            {
                if (now.Code == cause.Code && SamePrinter(now.PrinterName, cause.PrinterName))
                {
                    Say(events, "O problema ainda está aí.");
                    continue;
                }

                events.Report(new CauseFound(now.Code, now.Title, now.Description, now.Guidance, now.PrinterName, now.CandidateActionId));
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.ObjectiveVerification,
                    $"O problema anterior não aparece mais, mas encontrei outro: {CauseWording.Plain(now.Code, now.PrinterName, now.Description)} Comece um novo atendimento para tratar disso.");
            }

            if (await OfferTestPageAsync(cause.PrinterName, FlowResult.Resolved, actions, events, ct) is { } viaTestPage)
                return viaTestPage;
            return await CloseWithUserAsync(FlowResult.Resolved, ClosingTexts.Printing, ct);
        }
    }

    // ---------- fechamento humano ----------

    private async Task<FlowClosing> CloseWithUserAsync(FlowResult objective, ClosingTexts texts, CancellationToken ct)
    {
        var answer = await user.AskIfResolvedAsync(texts.Question, ct);
        return answer switch
        {
            ResolutionAnswer.Worked => new FlowClosing(FlowResult.Resolved, ClosingBasis.UserAnswer, texts.Worked),
            ResolutionAnswer.StillNotWorking => new FlowClosing(FlowResult.NotResolved, ClosingBasis.UserAnswer, texts.StillBroken),
            _ => new FlowClosing(objective, ClosingBasis.ObjectiveVerification, objective switch
            {
                FlowResult.Resolved => "Você não conseguiu verificar. Pelo que o computador mostra, o problema foi resolvido.",
                FlowResult.NotResolved => "Você não conseguiu verificar. Pelo que o computador mostra, o problema continua.",
                _ => "Você não conseguiu verificar, e o computador também não permite confirmar o resultado."
            })
        };
    }

    // ---------- apoio ----------

    private async Task<(PrintingSnapshot Snapshot, ScenarioAnalysis Analysis)> AnalyzeAsync(
        IProgress<GuidedEvent> events, PrinterScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_collectTimeout);
        try
        {
            var (snapshot, analysis) = await scenario.AnalyzeAsync(new NarrationProgress(events), timeout.Token, events);
            return (snapshot, Narrow(snapshot, analysis, scope));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private static ScenarioAnalysis Narrow(PrintingSnapshot snapshot, ScenarioAnalysis analysis, PrinterScope scope) =>
        scope.Only is { } only ? CauseAnalyzer.Analyze(snapshot, TargetMode.DefaultOrPhysical, only)
        : scope.AllPhysical ? CauseAnalyzer.Analyze(snapshot, TargetMode.AllPhysical)
        : analysis;

    private static List<string> PhysicalNames(PrintingSnapshot snapshot) =>
        (snapshot.PrinterState.Facts ?? []).Where(a => !a.IsVirtual).Select(a => a.Printer.Name).ToList();

    private static bool IsPhysical(PrintingSnapshot snapshot, string name) =>
        snapshot.PrinterState.Facts?.Any(a => !a.IsVirtual && string.Equals(a.Printer.Name, name, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool SamePrinter(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Atualiza os cartões depois do cruzamento. Quem não foi avaliada fica "desconhecida", não "ok".</summary>
    private static void PublishPrinterStates(IProgress<GuidedEvent> events, PrintingSnapshot snapshot, ScenarioAnalysis analysis)
    {
        var spoolerDown = analysis.Causes.Any(c => c.Code == CauseCodes.SpoolerNotRunning);
        foreach (var a in snapshot.PrinterState.Facts ?? [])
        {
            var name = a.Printer.Name;
            if (!analysis.TargetPrinters.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                events.Report(new PrinterStateChanged(name, PrinterCardState.Unknown, "não avaliada"));
                continue;
            }

            var cause = analysis.Causes.FirstOrDefault(c => c.IsBlocking && SamePrinter(c.PrinterName, name));
            if (cause is not null)
                events.Report(new PrinterStateChanged(name, PrinterCardState.Problem, ShortLabel(cause)));
            else if (spoolerDown)
                events.Report(new PrinterStateChanged(name, PrinterCardState.Problem, "serviço de impressão parado"));
            else
                events.Report(new PrinterStateChanged(name, PrinterCardState.Ok, "nada encontrado"));
        }
    }

    private static string ShortLabel(ProbableCause cause) => cause.Code switch
    {
        CauseCodes.PrinterPaused => "pausada",
        CauseCodes.PrinterOffline => "offline",
        CauseCodes.PrinterNoPort => "sem porta configurada",
        CauseCodes.StuckJobs => "trabalhos presos na fila",
        CauseCodes.EquipmentError => "erro no equipamento",
        _ => "com problema"
    };

    private DiagnosticReport Finish(
        DateTimeOffset started, List<ActionRecord> actions, ScenarioAnalysis analysis, PrintingSnapshot snapshot,
        FlowClosing closing, IProgress<GuidedEvent> events)
    {
        events.Report(new FlowClosed(closing));
        var outcome = AnalysisOutcomeBuilder.Build(snapshot, analysis, Headline);
        return new DiagnosticReport
        {
            DiagnosticId = NotPrintingScenario.DiagnosticId,
            ExecutionStatus = ExecutionStatus.Completed,
            OverallStatus = outcome.OverallStatus,
            StartedAtUtc = started,
            CompletedAtUtc = _clock.GetUtcNow(),
            Summary = $"{outcome.Summary} Conclusão: {closing.Message}",
            Findings = outcome.Findings,
            Evidence = outcome.Evidence,
            Sources = outcome.Sources,
            Limitations = outcome.Limitations,
            Recommendations = outcome.Recommendations,
            Actions = actions,
            Closing = closing
        };
    }

    private DiagnosticReport Failure(
        DateTimeOffset started, List<ActionRecord> actions, ExecutionStatus status, string summary, string error) =>
        new()
        {
            DiagnosticId = NotPrintingScenario.DiagnosticId,
            ExecutionStatus = status,
            OverallStatus = OverallStatus.NotEvaluated,
            StartedAtUtc = started,
            CompletedAtUtc = _clock.GetUtcNow(),
            Summary = summary,
            ErrorMessage = error,
            Actions = actions,
            Limitations = ["Nenhuma conclusão foi tirada: a execução não terminou normalmente."]
        };

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    private static void Say(IProgress<GuidedEvent> events, string message) => events.Report(new Narration(message));

    /// <summary>Transforma a narração técnica das verificações em eventos, na mesma ordem (a tela do usuário não os mostra).</summary>
    private sealed class NarrationProgress(IProgress<GuidedEvent> events) : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) => events.Report(new TechnicalNote(value.Message));
    }

    /// <summary>Repassa a confirmação ao usuário e, se ele disser "sim", avisa a tela antes de a ação executar.</summary>
    private sealed class AnnounceOnConfirm(IConfirmation inner, Action onYes) : IConfirmation
    {
        public async Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken)
        {
            var yes = await inner.ConfirmAsync(prompt, cancellationToken);
            if (yes) onYes();
            return yes;
        }
    }
}
