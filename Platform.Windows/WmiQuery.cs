using System.Management;
using System.Runtime.InteropServices;
using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows;

/// <summary>Executa uma consulta WQL (somente SELECT) e traduz falhas esperadas em <see cref="SourceResult{T}"/>.</summary>
internal static class WmiQuery
{
    public static SourceResult<T> Run<T>(string wql, CancellationToken ct, Func<ManagementBaseObject, T> map)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var results = searcher.Get();
            var items = new List<T>();
            foreach (ManagementBaseObject obj in results)
            {
                ct.ThrowIfCancellationRequested();
                using (obj) items.Add(map(obj));
            }
            return SourceResult<T>.Ok(items);
        }
        catch (UnauthorizedAccessException ex)
        {
            return SourceResult<T>.Fail(SourceStatus.PermissionDenied, ex.Message);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return SourceResult<T>.Fail(SourceStatus.PermissionDenied, ex.Message);
        }
        catch (ManagementException ex)
        {
            return SourceResult<T>.Fail(SourceStatus.Unavailable, $"{ex.ErrorCode}: {ex.Message}");
        }
        catch (COMException ex)
        {
            return SourceResult<T>.Fail(SourceStatus.Unavailable, ex.Message);
        }
    }
}
