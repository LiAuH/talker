using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Symbols;


class ThreadStackAnalyzer
{
    static async Task Main(string[] args)
    {
        string tracePath = @"d:\1.etl";
        using ITraceProcessor trace = TraceProcessor.Create(tracePath);
        Console.WriteLine("正在加载跟踪文件...");

        var pendingSymbolData = trace.UseSymbols();
        var pendingCpuSamplingData = trace.UseCpuSamplingData();
        var pendingContextSwitches = trace.UseContextSwitchData();
        var pendingCpuSchedulingData = trace.UseCpuSchedulingData();
        var pendingStacks = trace.UseStacks();
        var pendingStacksTags = trace.UseStackTags();
        var pendingThreads = trace.UseThreads();
        var pendingProcess = trace.UseProcesses();

        trace.Process();
        Console.WriteLine("跟踪文件处理完成");

        var symbolData = pendingSymbolData.Result;
        var cpuSamplingData = pendingCpuSamplingData.Result;
        var contextSwitches = pendingContextSwitches.Result;
        var cpuSchedulingData = pendingCpuSchedulingData.Result;
        var stacks = pendingStacks.Result;
        var tags = pendingStacksTags.Result;
        var threads = pendingThreads.Result;
        var process = pendingProcess.Result;

        var symbolPath = new SymbolPath("srv*D:\\SymCache*https://msdl.microsoft.com/download/symbols");
        await symbolData.LoadSymbolsForConsoleAsync(SymCachePath.Automatic, symbolPath);

        var maps = tags.CreateDefaultMapper();
        using var writer = new StreamWriter("slice.csv");
        writer.WriteLine("ProcessName,Pid,Tid,Cpu,TimeStamp,TimeStop,Tags");
        foreach (var slice in cpuSchedulingData.CpuTimeSlices)
        {
            var name = slice.Process.ImageName;
            var tid = slice.Thread.Id;
            var pid = slice.Process.Id;
            var ts = slice.StartTime.Value.RawValue;
            var te = slice.StopTime.Value.RawValue;
            var cpu = slice.Processor;
            var tag = "";
            if (slice.SwitchIn.Stack != null)
            {
                foreach (var stk in slice.SwitchIn.Stack.GetStackFrameTags(maps))
                {
                    if (stk.Symbol != null)
                    {
                        tag = stk.Symbol.Image.FileName + "!" + stk.Symbol.FunctionName + "/" + tag;
                    }
                }
                if (tag.EndsWith('/'))
                {
                    tag = tag.TrimEnd('/');
                }
                tag = tag.Replace(',', ';');
            }
            writer.WriteLine($"{name},{pid},{tid},{cpu},{ts},{te},{tag}");
        }
        writer.Close();
    }
}