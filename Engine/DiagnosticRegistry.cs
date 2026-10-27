using AutomationPlatform.Contracts;

namespace AutomationPlatform.Engine;

/// <summary>Catálogo dos diagnósticos disponíveis, indexado pelo id.</summary>
public sealed class DiagnosticRegistry
{
    private readonly Dictionary<string, IDiagnostic> _diagnostics = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<IDiagnostic> All => _diagnostics.Values;

    public void Register(IDiagnostic diagnostic)
    {
        if (!_diagnostics.TryAdd(diagnostic.Id, diagnostic))
            throw new InvalidOperationException($"Já existe um diagnóstico registrado com o id '{diagnostic.Id}'.");
    }

    public bool TryGet(string id, out IDiagnostic? diagnostic) =>
        _diagnostics.TryGetValue(id, out diagnostic);
}
