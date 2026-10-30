using AutomationPlatform.Contracts;

namespace AutomationPlatform.Engine;

/// <summary>A lista fixa de ações conhecidas, por id. Quem não está na lista não executa. Lista vazia = só diagnóstico e orientação.</summary>
public sealed class ActionCatalog(IEnumerable<IRemediationAction> actions) : IActionCatalog
{
    private readonly Dictionary<string, IRemediationAction> _byId =
        actions.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public IRemediationAction? Find(string actionId) => _byId.GetValueOrDefault(actionId);
}
