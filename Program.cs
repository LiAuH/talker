using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Symbols;

class ThreadStackAnalyzer
{
    static async Task Main(string[] args)
    {
        var symbolPath = "srv*D:\\\\Symbols*https://msdl.microsoft.com/download/symbols";
        var outputPath = "output.csv";
        var inputPath = "";
        var filter = Array.Empty<string>();
        for (int i = 0; i < args.Length; i++){
            switch (args[i]){
                case "-s":
                    if (++i < args.Length){
                        symbolPath = args[i];
                    }
                    break;
                case "-o":
                    if (++i < args.Length){
                        outputPath = args[i];
                    }
                    break;
                case "-f":
                    if (++i < args.Length){
                        filter = args[i].Split(",");
                    }
                    break;
                default:
                    inputPath = args[i];
                    break;
            }
        }
        if (inputPath.Length == 0){
            Console.WriteLine("-s -o -f");
            return;
        }
        Console.WriteLine($"正在加载跟踪文件...{inputPath}");
        using ITraceProcessor trace = TraceProcessor.Create(inputPath);

        var pendingSymbolData = trace.UseSymbols();
        var pendingCpuSchedulingData = trace.UseCpuSchedulingData();
        var pendingStacksTags = trace.UseStackTags();

        trace.Process();

        var symbolData = pendingSymbolData.Result;
        var cpuSchedulingData = pendingCpuSchedulingData.Result;
        var tags = pendingStacksTags.Result;

        await symbolData.LoadSymbolsForConsoleAsync(SymCachePath.Automatic, new SymbolPath(symbolPath));

        var maps = tags.CreateDefaultMapper();
        using var writer = new StreamWriter(outputPath);
        writer.WriteLine("ProcessName,NewPid,NewTid,OldName,OldPid,OldTid,Cpu,TimeStamp,TimeStop,Tags");
        foreach (var slice in cpuSchedulingData.ThreadActivity) {
            var name = slice.Process.ImageName;
            var ok = false;
            if (filter.Length > 0){
                foreach (var t in filter){
                    if (name.Contains(t, StringComparison.OrdinalIgnoreCase)) {
                        ok = true;
                        break;
                    }
                }
                if (!ok) {
                    continue;
                }
            }
            var tid = slice.Thread.Id;
            var pid = slice.Process.Id;
            var ts = slice.StartTime.Value.RawValue;
            var te = slice.StopTime.Value.RawValue;
            var cpu = slice.Processor;
            var tag = "";
            if (slice.SwitchIn.Stack != null){
                foreach (var stk in slice.SwitchIn.Stack.GetStackFrameTags(maps)){
                    if (stk.Symbol != null){
                        tag = stk.Symbol.Image.FileName + "!" + stk.Symbol.FunctionName + "/" + tag;
                    }
                }
                if (tag.EndsWith('/')){
                    tag = tag.TrimEnd('/');
                }
                tag = tag.Replace(',', ';');
            }
            var opid = 0;
            var otid = 0;
            var oname = "";
            if (slice.ReadyingThread != null) otid = slice.ReadyingThread.Id;
            if (slice.ReadyingProcess != null) {
                opid = slice.ReadyingProcess.Id;
                oname = slice.ReadyingProcess.ImageName;
            }
            writer.WriteLine($"{name},{pid},{tid},{oname},{opid},{otid},{cpu},{ts},{te},{tag}");
        }
        writer.Close();
    }
}