using System;
using System.Diagnostics;

namespace DotNetCompose.Runtime.HotReload;

internal static class HotReloadDiagnostics
{
    public static void ReportError(Exception exception)
    {
        try
        {
            Trace.TraceError("DotNetCompose Hot Reload: {0}", exception);
        }
        catch
        {
            // A failing diagnostic listener must not interrupt other hosts.
        }
    }
}
