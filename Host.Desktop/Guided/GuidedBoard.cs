using AutomationPlatform.Contracts;

namespace AutomationPlatform.Host.Desktop.Guided;

public enum StepStatus { Running, Healthy, Problem, NotEvaluable }

public sealed record StepRow(string Key, string Label, StepStatus Status, string? Summary);

public sealed record PrinterRow(string Name, bool IsVirtual, bool? IsDefault, PrinterCardState State, string StateText);

/// <summary>
/// O "quadro" ao vivo da tela: as impressoras (cartões) e as etapas da investigação. É só estado:
/// recebe os eventos do fluxo e diz se algo mudou. Quem desenha é a tela. Sem WPF, então dá para testar.
/// </summary>
public sealed class GuidedBoard
{
    private readonly List<StepRow> _steps = [];
    private readonly List<PrinterRow> _printers = [];

    public IReadOnlyList<StepRow> Steps => _steps;
    public IReadOnlyList<PrinterRow> Printers => _printers;

    /// <summary>Aplica um evento. Devolve true se o quadro mudou (a tela precisa redesenhar).</summary>
    public bool Apply(GuidedEvent e)
    {
        switch (e)
        {
            case StepStarted s:
                // Uma etapa que já existe volta a começar: é uma nova rodada (reverificação). Recomeça a lista.
                if (_steps.Any(r => r.Key == s.Key)) _steps.Clear();
                _steps.Add(new StepRow(s.Key, s.Label, StepStatus.Running, null));
                return true;

            case StepFinished f:
                var at = _steps.FindIndex(r => r.Key == f.Key);
                var done = new StepRow(f.Key, f.Label, StatusOf(f.Verdict), f.Summary);
                if (at >= 0) _steps[at] = done; else _steps.Add(done);
                return true;

            case PrintersFound p:
                _printers.Clear();
                _printers.AddRange(p.Printers.Select(c => new PrinterRow(c.Name, c.IsVirtual, c.IsDefault, c.State, c.StateText)));
                return true;

            case PrinterStateChanged c:
                var i = _printers.FindIndex(r => string.Equals(r.Name, c.Name, StringComparison.OrdinalIgnoreCase));
                if (i < 0) return false;
                _printers[i] = _printers[i] with { State = c.State, StateText = c.StateText };
                return true;

            default:
                return false;
        }
    }

    private static StepStatus StatusOf(CheckVerdict verdict) => verdict switch
    {
        CheckVerdict.Healthy => StepStatus.Healthy,
        CheckVerdict.Problem => StepStatus.Problem,
        _ => StepStatus.NotEvaluable
    };
}
