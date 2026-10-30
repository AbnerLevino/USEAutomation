using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// O fluxo guiado do botão "Impressora não imprime": investiga (só lê), mostra a causa, propõe a correção,
/// confirma, executa, verifica e fecha com a opinião do usuário. Tudo que a tela precisa chega como
/// <see cref="GuidedEvent"/>; tudo que depende do usuário passa por <see cref="IUserInteraction"/>.
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

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TimeSpan _collectTimeout = collectTimeout ?? DefaultCollectTimeout;

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
        Say(events, "Antes de começar: enquanto investigo, não altero nada. Só leio o estado do computador.");
        var (snapshot, analysis) = await AnalyzeAsync(events, onlyPrinter: null, ct);

        // 1. Mais de uma impressora física candidata: quem sabe qual é a dele é o usuário.
        string? chosen = null;
        var candidates = analysis.TargetPrinters.Where(n => IsPhysical(snapshot, n)).ToList();
        if (candidates.Count > 1)
        {
            var choice = await user.ChooseAsync("Qual é a impressora que não está imprimindo?",
                candidates.Select(n => new Choice(n, n)).ToList(), ct);
            if (choice is null)
                return Finish(started, actions, analysis, snapshot,
                    new FlowClosing(FlowResult.Cancelled, ClosingBasis.None, "Você cancelou. Nada foi alterado."), events);
            chosen = choice.Id;
            analysis = CauseAnalyzer.Analyze(snapshot, TargetMode.DefaultOrPhysical, chosen);
        }

        PublishPrinterStates(events, snapshot, analysis);

        // 2. Sem causa: o motor não finge que está tudo bem.
        if (analysis.Primary is not { } primary)
        {
            const string message = "Não encontrei nenhuma causa nas verificações que fiz. Isso não prova que a impressão funciona: envie este relatório ao suporte.";
            events.Report(new NoCauseFound(message));
            return Finish(started, actions, analysis, snapshot, new FlowClosing(FlowResult.Inconclusive, ClosingBasis.None, message), events);
        }

        events.Report(new CauseFound(primary.Code, primary.Title, primary.Description, primary.Guidance, primary.PrinterName, primary.CandidateActionId));

        // 3. Com correção conhecida: pipeline (confirma, executa, verifica). Sem ela: orientação e reverificação.
        var action = primary.CandidateActionId is { } id ? catalog.Find(id) : null;
        var closing = action is not null
            ? await FixAsync(action, primary, actions, events, ct)
            : await GuideAsync(primary, chosen, events, ct);

        return Finish(started, actions, analysis, snapshot, closing, events);
    }

    // ---------- caminho da correção ----------

    private async Task<FlowClosing> FixAsync(
        IRemediationAction action, ProbableCause cause, List<ActionRecord> actions, IProgress<GuidedEvent> events, CancellationToken ct)
    {
        Say(events, $"Encontrei uma correção para isso: {action.DisplayName}.");
        var record = await runner.RunAsync(action, new ActionTarget(cause.PrinterName), user, ct);
        actions.Add(record);
        events.Report(new ActionFinished(record));

        switch (record.Status)
        {
            case ActionStatus.NotConfirmed:
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, "Você preferiu não corrigir agora. O problema continua e nada foi alterado.");
            case ActionStatus.PreconditionFailed:
                return new FlowClosing(FlowResult.Inconclusive, ClosingBasis.None, record.Summary);
            case ActionStatus.ElevationDenied or ActionStatus.ExecutionFailed:
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, record.Summary);
        }

        // A ação rodou: a opinião do usuário fecha; sem ela, vale a leitura objetiva da própria ação.
        var objective = record.Status switch
        {
            ActionStatus.Resolved => FlowResult.Resolved,
            ActionStatus.NotResolved => FlowResult.NotResolved,
            _ => FlowResult.Inconclusive
        };
        return await CloseWithUserAsync(objective, record.Summary, ct);
    }

    // ---------- caminho da orientação ----------

    private async Task<FlowClosing> GuideAsync(ProbableCause cause, string? chosen, IProgress<GuidedEvent> events, CancellationToken ct)
    {
        while (true)
        {
            var reply = await user.RequestPhysicalActionAsync(cause.Guidance, ct);
            if (reply == PhysicalActionReply.Cancel)
                return new FlowClosing(FlowResult.Cancelled, ClosingBasis.None, "Você cancelou. Nada foi alterado.");
            if (reply == PhysicalActionReply.Skipped)
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, "Você não fez isso agora. O problema continua.");

            Say(events, "Obrigado. Vou conferir de novo, sem presumir que deu certo.");
            var (snapshot, analysis) = await AnalyzeAsync(events, chosen, ct);
            // A nova rodada reenvia as impressoras no estado inicial: os cartões precisam do estado atualizado.
            PublishPrinterStates(events, snapshot, analysis);

            if (analysis.Primary is { } now)
            {
                if (now.Code == cause.Code && SamePrinter(now.PrinterName, cause.PrinterName))
                {
                    Say(events, "Ainda vejo o mesmo problema.");
                    continue;
                }

                events.Report(new CauseFound(now.Code, now.Title, now.Description, now.Guidance, now.PrinterName, now.CandidateActionId));
                return new FlowClosing(FlowResult.NotResolved, ClosingBasis.ObjectiveVerification,
                    $"O problema \"{cause.Title}\" não aparece mais, mas encontrei outro: \"{now.Title}\". Rode o diagnóstico de novo.");
            }

            return await CloseWithUserAsync(FlowResult.Resolved,
                $"A verificação não encontra mais o problema \"{cause.Title}\" (outros pontos não foram verificados).", ct);
        }
    }

    // ---------- fechamento humano ----------

    private async Task<FlowClosing> CloseWithUserAsync(FlowResult objective, string objectiveDetail, CancellationToken ct)
    {
        var answer = await user.AskIfResolvedAsync("A impressão voltou a funcionar?", ct);
        return answer switch
        {
            ResolutionAnswer.Worked => new FlowClosing(FlowResult.Resolved, ClosingBasis.UserAnswer, "Você confirmou que a impressão voltou a funcionar."),
            ResolutionAnswer.StillNotWorking => new FlowClosing(FlowResult.NotResolved, ClosingBasis.UserAnswer,
                "Você informou que a impressão continua sem funcionar. Envie este relatório ao suporte."),
            _ => new FlowClosing(objective, ClosingBasis.ObjectiveVerification,
                $"Você não pôde testar, então vale só a verificação do sistema: {objectiveDetail}")
        };
    }

    // ---------- apoio ----------

    private async Task<(PrintingSnapshot Snapshot, ScenarioAnalysis Analysis)> AnalyzeAsync(
        IProgress<GuidedEvent> events, string? onlyPrinter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_collectTimeout);
        try
        {
            var (snapshot, analysis) = await scenario.AnalyzeAsync(new NarrationProgress(events), timeout.Token, events);
            return (snapshot, onlyPrinter is null ? analysis : Narrow(snapshot, analysis, onlyPrinter));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private static ScenarioAnalysis Narrow(PrintingSnapshot snapshot, ScenarioAnalysis analysis, string? onlyPrinter) =>
        onlyPrinter is null ? analysis : CauseAnalyzer.Analyze(snapshot, TargetMode.DefaultOrPhysical, onlyPrinter);

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

    /// <summary>Transforma a narração das verificações em eventos de narração, na mesma ordem.</summary>
    private sealed class NarrationProgress(IProgress<GuidedEvent> events) : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) => events.Report(new Narration(value.Message));
    }
}
