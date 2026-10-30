using System.Security.AccessControl;
using System.Security.Principal;

namespace AutomationPlatform.Platform.Windows.Elevation;

/// <summary>Uma regra de permissão de uma pasta ou arquivo, já reduzida ao que importa para a confiança.</summary>
/// <param name="InheritOnly">true = vale só para os itens dentro da pasta, não para a pasta em si.</param>
public sealed record AclRule(string Sid, FileSystemRights Rights, bool IsAllow, bool InheritOnly = false);

/// <summary>Lê as regras de permissão. A versão real consulta o Windows; os testes usam uma falsa.</summary>
public interface IAclReader
{
    IReadOnlyList<AclRule> Read(string path, bool isDirectory);
}

/// <summary>
/// Decide se o executor elevado está num lugar em que usuário comum NÃO consegue trocá-lo. Se um usuário comum
/// pudesse substituir o executável, aprovar o aviso do administrador entregaria poder de administrador a ele.
/// A regra: nem o arquivo, nem a pasta, nem nenhuma pasta acima podem dar permissão de escrita a quem não seja
/// o sistema, o instalador do Windows ou os administradores. Regras de "negar" são ignoradas (checagem conservadora).
/// </summary>
public static class ExecutableTrust
{
    private static readonly HashSet<string> TrustedSids =
    [
        "S-1-5-18",       // SYSTEM
        "S-1-5-32-544",   // Administradores
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",   // TrustedInstaller
        "S-1-3-0"         // CREATOR OWNER: só vale para quem já pôde criar o item
    ];

    // O arquivo e a pasta onde ele está: qualquer escrita basta para trocá-lo ou plantar um arquivo ao lado dele.
    private const FileSystemRights WriteLike =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    // As pastas ACIMA: criar itens dentro delas (ex.: "criar pastas" em C:, que todo usuário tem) não troca uma pasta
    // que já existe. Só apagar/renomear o item, mudar permissões ou tomar posse permitiria substituí-la.
    private const FileSystemRights AncestorRisk =
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    /// <summary>Devolve (true, null) se é seguro, ou (false, motivo em palavras simples).</summary>
    public static (bool Trusted, string? Reason) Check(string executablePath, IAclReader reader)
    {
        var fullPath = Path.GetFullPath(executablePath);

        if (Problem(fullPath, isDirectory: false, WriteLike, reader) is { } onFile) return (false, onFile);

        var first = true;
        for (var dir = Path.GetDirectoryName(fullPath); dir is not null; dir = Path.GetDirectoryName(dir), first = false)
            if (Problem(dir, isDirectory: true, first ? WriteLike : AncestorRisk, reader) is { } onDir) return (false, onDir);

        return (true, null);
    }

    private static string? Problem(string path, bool isDirectory, FileSystemRights risky, IAclReader reader)
    {
        foreach (var rule in reader.Read(path, isDirectory))
        {
            if (!rule.IsAllow) continue;
            if (isDirectory && rule.InheritOnly) continue;   // só vale para os filhos, que são checados à parte
            if ((rule.Rights & risky) == 0) continue;
            if (TrustedSids.Contains(rule.Sid)) continue;
            return $"\"{path}\" pode ser alterado por quem não é administrador ({rule.Sid}).";
        }
        return null;
    }
}

/// <summary>A leitura real das permissões pelo Windows.</summary>
public sealed class WindowsAclReader : IAclReader
{
    public IReadOnlyList<AclRule> Read(string path, bool isDirectory)
    {
        FileSystemSecurity security = isDirectory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => new AclRule(
                r.IdentityReference.Value, r.FileSystemRights, r.AccessControlType == AccessControlType.Allow,
                InheritOnly: r.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)))
            .ToList();
    }
}
