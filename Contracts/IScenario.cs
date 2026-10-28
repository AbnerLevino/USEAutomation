namespace AutomationPlatform.Contracts;

/// <summary>
/// Um diagnóstico que parte de um SINTOMA ("Impressora não está imprimindo"), reúne várias
/// verificações, cruza as evidências e conclui a causa provável. Por ser um <see cref="IDiagnostic"/>,
/// roda no mesmo Engine, relatório e CLI que qualquer outro.
/// </summary>
public interface IScenario : IDiagnostic
{
    /// <summary>O sintoma na linguagem do usuário, ex.: "Impressora não está imprimindo".</summary>
    string Symptom { get; }
}
