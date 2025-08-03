using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Utils;
public static class ParallelHelper
{
    /*
    /// <summary>
    /// Для Windows — половина логических ядер (min 1), 
    /// для Linux/macOS — число физических ядер, или fallback в логические.
    /// </summary>
    private static int GetCoreCountForDop()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Math.Max(1, Environment.ProcessorCount / 2);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                var physCorePairs = new HashSet<(string phys, string core)>();
                string phys = null, core = null;
                foreach (var line in lines)
                {
                    if (line.StartsWith("physical id"))
                        phys = line.Split(':')[1].Trim();
                    else if (line.StartsWith("core id"))
                    {
                        core = line.Split(':')[1].Trim();
                        if (phys != null)
                            physCorePairs.Add((phys, core));
                    }
                }
                if (physCorePairs.Count > 0)
                    return physCorePairs.Count;
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var psi = new ProcessStartInfo("sysctl", "-n hw.physicalcpu")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                var outp = proc.StandardOutput.ReadToEnd();
                if (int.TryParse(outp.Trim(), out var cores) && cores > 0)
                    return cores;
            }
            catch { }
        }

        // fallback
        return Environment.ProcessorCount;
    }

    /// <summary>
    /// Параллельный перебор 
    /// </summary>
    public static void ForEachPartitioned<T>(
        IEnumerable<T> workItemsEnumerable,
        Action<T> action)
    {

        int maxDop=1;

        if (ElectricalProgressive.multiThreading) // если включена многопоточность
        {
            int items = workItemsEnumerable.Count();
            int coreCount = GetCoreCountForDop();

            // не больше задач и не больше ядер, минимум 1
            maxDop = Math.Clamp(items, 1, coreCount);
            //maxDop = 2;
        }

        var po = new ParallelOptions { MaxDegreeOfParallelism = maxDop };

        Parallel.ForEach(workItemsEnumerable, po, action);
    }
    */
}
