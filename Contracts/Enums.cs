using System.Text.Json.Serialization;

namespace AutomationPlatform.Contracts;

/// <summary>
/// Como a EXECUÇÃO terminou (pergunta: "o diagnóstico conseguiu rodar?").
/// Independe de ter encontrado problema ou não.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionStatus
{
    /// <summary>Rodou até o fim e produziu um resultado.</summary>
    Completed,
    /// <summary>Uma exceção inesperada interrompeu a execução.</summary>
    Failed,
    /// <summary>O Windows (ou outra fonte) negou acesso aos dados.</summary>
    PermissionDenied,
    /// <summary>Passou do tempo limite.</summary>
    TimedOut,
    /// <summary>Foi cancelado por quem pediu a execução.</summary>
    Cancelled
}

/// <summary>
/// O que o diagnóstico CONCLUIU (pergunta: "o que as evidências dizem?").
/// Só faz sentido quando a execução foi <see cref="ExecutionStatus.Completed"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverallStatus
{
    /// <summary>Há evidência direta de um problema.</summary>
    ProblemConfirmed,
    /// <summary>Há indícios, mas não prova direta.</summary>
    ProblemProbable,
    /// <summary>As verificações feitas não encontraram problema (não prova que ele não existe).</summary>
    NoProblemDetected,
    /// <summary>Não há evidência suficiente para concluir nada.</summary>
    Inconclusive,
    /// <summary>A execução falhou, então não houve conclusão.</summary>
    NotEvaluated
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Severity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Quão sustentado é um achado. Não é uma porcentagem: é uma categoria
/// definida pelas regras do diagnóstico (ver docs/DIAGNOSTIC-REPORT.md).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Confidence
{
    /// <summary>Provado por uma evidência direta (ex.: código de erro reportado pelo Windows).</summary>
    Confirmed,
    /// <summary>Sugerido por evidências indiretas.</summary>
    Probable
}

/// <summary>Resultado da consulta a uma fonte de dados.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceStatus
{
    Consulted,
    Unavailable,
    PermissionDenied,
    Failed
}
