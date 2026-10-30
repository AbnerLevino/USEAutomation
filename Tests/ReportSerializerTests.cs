using AutomationPlatform.Contracts;

namespace AutomationPlatform.Tests;

public class ReportSerializerTests
{
    private static DiagnosticReport SampleReport() => new()
    {
        DiagnosticId = "printing.device-recognition",
        ExecutionStatus = ExecutionStatus.Completed,
        OverallStatus = OverallStatus.ProblemConfirmed,
        StartedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        CompletedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 2, TimeSpan.Zero),
        Summary = "Resumo",
        Findings =
        [
            new Finding
            {
                Id = "f1", Title = "Título", Description = "Descrição",
                Severity = Severity.High, Confidence = Confidence.Confirmed,
                EvidenceIds = ["e1"]
            }
        ],
        Evidence = [new Evidence { Id = "e1", Source = "PnP", Description = "Código de erro 28" }],
        Sources = [new DataSourceResult { Name = "PnP", Status = SourceStatus.Consulted }],
        Limitations = ["Impressoras de rede não verificadas"],
        Recommendations = [new Recommendation { Text = "Instalar o driver", FindingId = "f1" }]
    };

    [Fact]
    public void Ida_e_volta_preserva_o_relatorio()
    {
        var original = SampleReport();

        var back = ReportSerializer.Deserialize(ReportSerializer.Serialize(original));

        Assert.Equal(original.DiagnosticId, back.DiagnosticId);
        Assert.Equal(original.ExecutionStatus, back.ExecutionStatus);
        Assert.Equal(original.OverallStatus, back.OverallStatus);
        Assert.Equal(original.StartedAtUtc, back.StartedAtUtc);
        Assert.Equal(original.Summary, back.Summary);
        Assert.Equal("f1", Assert.Single(back.Findings).Id);
        Assert.Equal(Confidence.Confirmed, back.Findings[0].Confidence);
        Assert.Equal(["e1"], back.Findings[0].EvidenceIds);
        Assert.Equal("Código de erro 28", Assert.Single(back.Evidence).Description);
        Assert.Equal(SourceStatus.Consulted, Assert.Single(back.Sources).Status);
        Assert.Equal(original.Limitations, back.Limitations);
        Assert.Equal("f1", Assert.Single(back.Recommendations).FindingId);
    }

    [Fact]
    public void Json_usa_camelCase_enums_em_texto_e_versao_do_esquema()
    {
        var json = ReportSerializer.Serialize(SampleReport());

        Assert.Contains("\"schemaVersion\": \"1.3\"", json);
        Assert.Contains("\"overallStatus\": \"ProblemConfirmed\"", json);
        Assert.Contains("\"executionStatus\": \"Completed\"", json);
    }

    [Fact]
    public void Relatorio_1_0_antigo_sem_acoes_continua_valido()
    {
        var json = ReportSerializer.Serialize(SampleReport())
            .Replace("\"schemaVersion\": \"1.3\"", "\"schemaVersion\": \"1.0\"");
        var withoutActions = System.Text.RegularExpressions.Regex.Replace(json, "\\s*\"actions\": \\[\\],?", "");

        var back = ReportSerializer.Deserialize(withoutActions);

        Assert.Equal("1.0", back.SchemaVersion);
        Assert.Empty(back.Actions);
        Assert.Equal("Resumo", back.Summary);
    }

    [Fact]
    public void Acoes_tentadas_vao_e_voltam_no_json_com_enums_em_texto()
    {
        var record = new ActionRecord
        {
            ActionId = "printing.printer.resume", DisplayName = "Retomar impressora", Level = ActionLevel.N1,
            PrinterName = "Impressora X", StartedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 3, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 4, TimeSpan.Zero),
            Status = ActionStatus.ElevationDenied, Confirmed = true, Summary = "Nada foi alterado."
        };

        var json = ReportSerializer.Serialize(SampleReport() with { Actions = [record] });
        var back = ReportSerializer.Deserialize(json);

        Assert.Contains("\"status\": \"ElevationDenied\"", json);
        Assert.Contains("\"level\": \"N1\"", json);
        Assert.Equal(record, Assert.Single(back.Actions));
    }
}
