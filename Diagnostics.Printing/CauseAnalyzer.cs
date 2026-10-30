using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>Quais filas o cruzamento considera.</summary>
public enum TargetMode
{
    /// <summary>Sintoma "não imprime": a impressora padrão; se ela for virtual, também as físicas.</summary>
    DefaultOrPhysical,
    /// <summary>Diagnóstico completo: todas as filas.</summary>
    AllQueues
}

/// <summary>
/// Cruza as seis verificações e conclui a causa provável. Função PURA: não consulta o Windows,
/// só interpreta o que as verificações já trouxeram, o que a torna fácil de testar.
/// <para>Prioridade (Rank menor bloqueia mais): 1 spooler parado · 2 fila pausada · 3 fila offline ·
/// 4 fila sem porta · 5 erro de dispositivo · 6 erro do equipamento · 7 trabalho preso ·
/// 8 padrão virtual ou ausente · 9 avisos (pouco papel/toner).</para>
/// <para>Causa × consequência: se o spooler está parado, a pausa ou o offline de uma fila, os trabalhos
/// acumulados são EFEITO, não uma segunda causa; "trabalho preso" só vira causa quando o spooler roda.</para>
/// </summary>
public static class CauseAnalyzer
{
    /// <param name="onlyPrinter">Quando o usuário escolheu a impressora, só ela é alvo. Causas do sistema todo continuam valendo.</param>
    public static ScenarioAnalysis Analyze(PrintingSnapshot s, TargetMode mode, string? onlyPrinter = null)
    {
        var notes = new List<string>();
        var assessments = Or(s.PrinterState.Facts);
        var targets = SelectTargets(s, assessments, mode, notes);
        if (onlyPrinter is not null)
        {
            targets = targets.Where(t => string.Equals(t.Printer.Name, onlyPrinter, StringComparison.OrdinalIgnoreCase)).ToList();
            notes.Add($"Foi avaliada só a impressora escolhida: '{onlyPrinter}'.");
        }
        var targetNames = targets.Select(t => t.Printer.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var causes = new List<ProbableCause>();

        // 1. Spooler: bloqueia todas as filas.
        var spoolerDown = s.Spooler is { Verdict: CheckVerdict.Problem, Facts: not null };
        if (spoolerDown)
        {
            causes.Add(new ProbableCause(
                CauseCodes.SpoolerNotRunning, 1,
                "O serviço de impressão (Spooler) não está em execução",
                s.Spooler.Findings.FirstOrDefault()?.Description ?? "O Windows reporta o Spooler fora de execução.",
                Confidence.Confirmed, Severity.High, true, null, PrinterActionIds.RestartSpooler,
                s.Spooler.Recommendations.FirstOrDefault()?.Text ?? "Inicie o serviço 'Spooler de Impressão'.",
                Ev("spooler", s.Spooler.Evidence, "")));
        }

        // 2, 3, 6, 9. Estado de cada fila alvo.
        foreach (var a in targets)
        {
            var name = a.Printer.Name;
            var evidence = Ev("state", s.PrinterState.Evidence, $"Fila '{name}'");

            if (a.Paused)
                causes.Add(new ProbableCause(CauseCodes.PrinterPaused, 2,
                    $"A fila '{name}' está pausada",
                    $"O Windows reporta a fila '{name}' como pausada: nada sai dela até ser retomada.",
                    Confidence.Confirmed, Severity.High, true, name, PrinterActionIds.ResumePrinter,
                    $"Abra a fila de '{name}' e escolha retomar a impressão.", evidence));

            if (a.Offline)
                causes.Add(new ProbableCause(CauseCodes.PrinterOffline, 3,
                    $"A fila '{name}' está offline",
                    $"O Windows reporta a fila '{name}' como offline.",
                    Confidence.Confirmed, Severity.High, true, name, PrinterActionIds.SetPrinterOnline,
                    $"Confira se '{name}' está ligada e conectada. Se estiver, desmarque \"Usar impressora offline\" na fila.", evidence));

            if (a.EquipmentError is { } error)
            {
                var warning = a.Printer.DetectedErrorState is 3 or 5;
                causes.Add(new ProbableCause(
                    warning ? CauseCodes.EquipmentWarning : CauseCodes.EquipmentError,
                    warning ? 9 : 6,
                    warning ? $"Aviso em '{name}': {error}" : $"A impressora '{name}' reporta: {error}",
                    $"Segundo o Windows, no equipamento '{name}': {error}.",
                    Confidence.Confirmed, warning ? Severity.Medium : Severity.High, !warning, name, null,
                    $"Verifique o equipamento '{name}': {error}.", evidence));
            }
        }

        // 4. Fila sem porta.
        foreach (var p in Or(s.Port.Facts).Where(p => p.Kind == PortKind.None && targetNames.Contains(p.PrinterName)))
            causes.Add(new ProbableCause(CauseCodes.PrinterNoPort, 4,
                $"A fila '{p.PrinterName}' não tem porta configurada",
                $"O Windows não informa porta para a fila '{p.PrinterName}'; ela não consegue enviar trabalhos a um equipamento.",
                Confidence.Confirmed, Severity.High, true, p.PrinterName, null,
                $"Abra as propriedades de '{p.PrinterName}', aba Portas, e escolha a porta correta do equipamento.",
                Ev("port", s.Port.Evidence, $"Fila '{p.PrinterName}'")));

        // 5. Dispositivo com erro (o Windows não liga o dispositivo a uma fila, então vale para o sistema).
        foreach (var d in Or(s.DeviceError.Facts))
        {
            var code = d.ErrorCode ?? 0;
            causes.Add(new ProbableCause(CauseCodes.DeviceError, 5,
                $"O Windows reporta erro no dispositivo '{d.Name}'",
                $"Código {code}: {DeviceErrorCodes.Describe(code)}.",
                Confidence.Confirmed, Severity.High, true, null, null,
                $"Abra o Gerenciador de Dispositivos, localize '{d.Name}' e verifique o estado do driver (código {code}).",
                Ev("device", s.DeviceError.Evidence, $"Dispositivo '{d.Name}'")));
        }

        // 7. Trabalho preso: só é causa própria se o spooler está rodando.
        if (!spoolerDown)
        {
            foreach (var q in Or(s.Queue.Facts).Where(q => q.StuckJobs > 0 && targetNames.Contains(q.PrinterName)))
                causes.Add(new ProbableCause(CauseCodes.StuckJobs, 7,
                    $"Há {q.StuckJobs} trabalho(s) preso(s) na fila '{q.PrinterName}'",
                    $"O Windows marca {q.StuckJobs} de {q.TotalJobs} trabalho(s) como em erro, em exclusão ou bloqueado(s).",
                    Confidence.Confirmed, Severity.High, true, q.PrinterName, PrinterActionIds.CancelStuckJobs,
                    $"Abra a fila de '{q.PrinterName}' e cancele os trabalhos com erro. Se não saírem, reinicie o serviço de impressão.",
                    Ev("queue", s.Queue.Evidence, $"Fila '{q.PrinterName}'")));
        }

        // 8. Padrão ausente ou virtual: o documento pode estar indo para o lugar errado.
        if (s.DefaultPrinter is { Verdict: CheckVerdict.Problem, Facts: { } d0 })
        {
            var physicalNames = assessments.Where(a => !a.IsVirtual).Select(a => a.Printer.Name).ToList();
            var suggestion = physicalNames.Count == 1 ? physicalNames[0] : null;
            var evidence = Ev("default", s.DefaultPrinter.Evidence, "");
            var which = suggestion is null ? "a impressora desejada" : $"'{suggestion}'";

            if (d0.DefaultName is null)
                causes.Add(new ProbableCause(CauseCodes.NoDefaultPrinter, 8,
                    "Nenhuma impressora está definida como padrão",
                    "O Windows não tem nenhuma fila marcada como padrão; programas que imprimem sem perguntar podem não achar destino.",
                    Confidence.Confirmed, Severity.Medium, true, suggestion, PrinterActionIds.SetDefaultPrinter,
                    $"Defina {which} como padrão em Configurações > Impressoras e scanners.", evidence));
            else
                causes.Add(new ProbableCause(CauseCodes.DefaultIsVirtual, 8,
                    $"A impressora padrão é virtual ('{d0.DefaultName}'), mas há impressora física instalada",
                    $"Documentos enviados sem escolher a impressora vão para '{d0.DefaultName}', que não leva a um equipamento.",
                    Confidence.Probable, Severity.Medium, true, suggestion, PrinterActionIds.SetDefaultPrinter,
                    $"Defina {which} como padrão em Configurações > Impressoras e scanners.", evidence));
        }

        // Verificações que não puderam avaliar: a causa delas, se existir, não foi considerada.
        foreach (var part in s.Parts().Where(p => p.Verdict == CheckVerdict.NotEvaluable))
            notes.Add($"A verificação \"{part.Label}\" não pôde ser avaliada; uma causa nessa área não foi considerada.");

        if (mode == TargetMode.DefaultOrPhysical && assessments.All(a => a.IsVirtual))
            notes.Add("Não há impressora física instalada, só filas virtuais; para saber se o Windows reconhece o equipamento, rode printing.device-recognition.");

        var ordered = causes.OrderBy(c => c.Rank).ThenBy(c => c.PrinterName, StringComparer.OrdinalIgnoreCase).ToList();
        return new ScenarioAnalysis(ordered, Consequences(s, ordered, spoolerDown), targets.Select(t => t.Printer.Name).ToList(), notes);
    }

    private static List<PrinterAssessment> SelectTargets(
        PrintingSnapshot s, IReadOnlyList<PrinterAssessment> all, TargetMode mode, List<string> notes)
    {
        if (mode == TargetMode.AllQueues) return all.ToList();

        var physical = all.Where(a => !a.IsVirtual).ToList();
        if (s.DefaultPrinter.Facts?.DefaultName is not { } defaultName) return physical;

        var targets = all.Where(a => string.Equals(a.Printer.Name, defaultName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (s.DefaultPrinter.Facts.DefaultIsVirtual == true)
            targets.AddRange(physical.Where(p => !targets.Contains(p)));
        else
        {
            var others = physical.Where(p => !targets.Contains(p)).Select(p => $"'{p.Printer.Name}'").ToList();
            if (others.Count > 0)
                notes.Add($"Foi avaliada a impressora padrão. Outras filas físicas não foram avaliadas como alvo: {string.Join(", ", others)}.");
        }
        return targets;
    }

    /// <summary>Trabalhos parados que são EFEITO de spooler parado, fila pausada ou offline.</summary>
    private static List<string> Consequences(PrintingSnapshot s, IReadOnlyList<ProbableCause> causes, bool spoolerDown)
    {
        var result = new List<string>();
        var queues = Or(s.Queue.Facts).Where(q => q.TotalJobs > 0).ToList();

        if (spoolerDown)
        {
            foreach (var q in queues)
                result.Add($"{q.TotalJobs} trabalho(s) parado(s) na fila '{q.PrinterName}', provavelmente por causa do serviço de impressão parado.");
            return result;
        }

        foreach (var cause in causes.Where(c => c.Code is CauseCodes.PrinterPaused or CauseCodes.PrinterOffline))
        {
            var why = cause.Code == CauseCodes.PrinterPaused ? "a fila estar pausada" : "a fila estar offline";
            foreach (var q in queues.Where(q => string.Equals(q.PrinterName, cause.PrinterName, StringComparison.OrdinalIgnoreCase)))
                result.Add($"{q.TotalJobs} trabalho(s) parado(s) na fila '{q.PrinterName}', provavelmente por causa de {why}.");
        }
        return result;
    }

    private static IReadOnlyList<T> Or<T>(IReadOnlyList<T>? list) => list ?? Array.Empty<T>();

    private static IReadOnlyList<string> Ev(string key, IEnumerable<Evidence> evidence, string startsWith) =>
        evidence.Where(e => e.Description.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
                .Select(e => $"{key}:{e.Id}").ToList();
}
