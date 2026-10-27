namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>Descrições dos códigos de problema do Gerenciador de Dispositivos mais comuns.</summary>
internal static class DeviceErrorCodes
{
    public static string Describe(int code) => code switch
    {
        10 => "o dispositivo não consegue iniciar",
        22 => "o dispositivo está desabilitado",
        24 => "o dispositivo não está presente, foi removido ou não está funcionando",
        28 => "os drivers do dispositivo não estão instalados",
        43 => "o Windows parou o dispositivo porque ele reportou problemas",
        45 => "o dispositivo não está conectado no momento",
        _ => "o Windows reportou um problema neste dispositivo"
    };
}
