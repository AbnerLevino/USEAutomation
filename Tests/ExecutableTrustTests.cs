using System.Security.AccessControl;
using AutomationPlatform.Platform.Windows.Elevation;

namespace AutomationPlatform.Tests;

public class ExecutableTrustTests
{
    private const string System = "S-1-5-18";
    private const string Admins = "S-1-5-32-544";
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string Users = "S-1-5-32-545";
    private const string AuthenticatedUsers = "S-1-5-11";
    private const string Everyone = "S-1-1-0";

    private const string Exe = @"C:\Program Files\Automaxion\AutomationPlatform.Host.Elevated.exe";

    /// <summary>Permissões falsas por caminho; o que não foi configurado é "só leitura para todos".</summary>
    private sealed class FakeAcl : IAclReader
    {
        private readonly Dictionary<string, List<AclRule>> _rules = new(StringComparer.OrdinalIgnoreCase);

        public FakeAcl Add(string path, params AclRule[] rules)
        {
            if (!_rules.TryGetValue(path, out var list)) _rules[path] = list = [];
            list.AddRange(rules);
            return this;
        }

        public IReadOnlyList<AclRule> Read(string path, bool isDirectory) =>
            _rules.TryGetValue(path, out var list) ? list : [new AclRule(Users, FileSystemRights.ReadAndExecute, true)];
    }

    private static AclRule Allow(string sid, FileSystemRights rights, bool inheritOnly = false) => new(sid, rights, true, inheritOnly);

    /// <summary>O jeito normal de "Program Files": administradores e sistema mandam, usuários só leem.</summary>
    private static FakeAcl ProtectedInstall() => new FakeAcl()
        .Add(Exe, Allow(System, FileSystemRights.FullControl), Allow(Admins, FileSystemRights.FullControl),
            Allow(TrustedInstaller, FileSystemRights.FullControl), Allow(Users, FileSystemRights.ReadAndExecute))
        .Add(@"C:\Program Files\Automaxion", Allow(System, FileSystemRights.FullControl), Allow(Admins, FileSystemRights.FullControl),
            Allow(Users, FileSystemRights.ReadAndExecute))
        .Add(@"C:\Program Files", Allow(System, FileSystemRights.FullControl), Allow(Admins, FileSystemRights.FullControl),
            Allow(TrustedInstaller, FileSystemRights.FullControl), Allow(Users, FileSystemRights.ReadAndExecute))
        .Add(@"C:\",
            Allow(System, FileSystemRights.FullControl), Allow(Admins, FileSystemRights.FullControl),
            // O padrão do C:\: todo usuário pode criar pastas na raiz. Isso NÃO deixa trocar uma pasta que já existe.
            Allow(Users, FileSystemRights.AppendData | FileSystemRights.ReadAndExecute),
            Allow(AuthenticatedUsers, FileSystemRights.Modify, inheritOnly: true));

    [Fact]
    public void Instalacao_protegida_normal_e_confiavel_mesmo_com_o_padrao_do_C_raiz()
    {
        var (trusted, reason) = ExecutableTrust.Check(Exe, ProtectedInstall());

        Assert.True(trusted, reason);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(Users)]
    [InlineData(AuthenticatedUsers)]
    [InlineData(Everyone)]
    [InlineData("S-1-5-21-1111-2222-3333-1105")]   // um usuário específico
    public void Se_um_usuario_comum_pode_escrever_no_arquivo_nao_e_confiavel(string sid)
    {
        var acl = ProtectedInstall().Add(Exe, Allow(sid, FileSystemRights.Modify));

        var (trusted, reason) = ExecutableTrust.Check(Exe, acl);

        Assert.False(trusted);
        Assert.Contains(sid, reason);
    }

    [Fact]
    public void Se_um_usuario_comum_pode_criar_arquivos_na_pasta_do_executor_nao_e_confiavel()
    {
        // Plantar um arquivo ao lado do executor (ex.: uma DLL) também daria poder de administrador.
        var acl = ProtectedInstall().Add(@"C:\Program Files\Automaxion", Allow(Users, FileSystemRights.WriteData));

        Assert.False(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    [Fact]
    public void Permissao_que_vale_so_para_os_filhos_da_pasta_nao_conta_para_a_pasta_em_si()
    {
        var acl = ProtectedInstall().Add(@"C:\Program Files\Automaxion", Allow(Users, FileSystemRights.Modify, inheritOnly: true));

        // O arquivo continua protegido pela própria regra dele; a regra "só filhos" da pasta não muda isso.
        Assert.True(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    [Fact]
    public void Mas_se_a_permissao_dos_filhos_chega_ao_arquivo_ele_nao_e_confiavel()
    {
        // O arquivo herdou "usuários podem modificar": a leitura das regras do próprio arquivo pega isso.
        var acl = ProtectedInstall().Add(Exe, Allow(Users, FileSystemRights.Modify));

        Assert.False(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    [Fact]
    public void Se_um_usuario_comum_pode_apagar_ou_renomear_uma_pasta_acima_nao_e_confiavel()
    {
        // Quem apaga/renomeia "Program Files" consegue pôr outra pasta no lugar.
        var acl = ProtectedInstall().Add(@"C:\Program Files", Allow(Users, FileSystemRights.DeleteSubdirectoriesAndFiles));

        Assert.False(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    [Fact]
    public void Se_um_usuario_comum_pode_mudar_permissoes_de_uma_pasta_acima_nao_e_confiavel()
    {
        var acl = ProtectedInstall().Add(@"C:\Program Files", Allow(Users, FileSystemRights.ChangePermissions));

        Assert.False(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    [Fact]
    public void Regras_de_negar_sao_ignoradas_a_checagem_e_conservadora()
    {
        var acl = ProtectedInstall().Add(Exe, new AclRule(Users, FileSystemRights.Modify, IsAllow: false));

        Assert.True(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    [Fact]
    public void Permissao_de_so_leitura_para_usuarios_nao_e_problema()
    {
        var acl = ProtectedInstall().Add(Exe, Allow(Everyone, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize));

        Assert.True(ExecutableTrust.Check(Exe, acl).Trusted);
    }

    // ---------- leitura real do Windows ----------

    [Fact]
    public void Pasta_do_proprio_usuario_como_a_de_desenvolvimento_NAO_e_confiavel_na_leitura_real()
    {
        var thisAssembly = typeof(ExecutableTrustTests).Assembly.Location;

        var (trusted, reason) = ExecutableTrust.Check(thisAssembly, new WindowsAclReader());

        Assert.False(trusted);
        Assert.NotNull(reason);
    }

    [Fact]
    public void Arquivo_do_sistema_em_pasta_protegida_e_confiavel_na_leitura_real()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var (trusted, reason) = ExecutableTrust.Check(cmd, new WindowsAclReader());

        Assert.True(trusted, reason);
    }
}
