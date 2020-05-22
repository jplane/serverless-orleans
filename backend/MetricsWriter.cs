using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Backend
{
    public class MetricsWriter
    {
        private static Guid _id = Guid.NewGuid();
        private Timer _timer;

        public MetricsWriter()
        {
        }

        public Task StartAsync()
        {
            _timer = new Timer(GetMetrics,
                               null,
                               TimeSpan.FromSeconds(10),
                               TimeSpan.FromSeconds(10));

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            await _timer.DisposeAsync();
        }

        private static void GetMetrics(object _)
        {
            var memory = GetMemory();
            var cpu = GetCpu();

            var mem_used = ((memory.Used / memory.Total) * 100d);

            var output = $"{{ 'id': {_id.ToString("D")}, 'cpu_used': {cpu.Used:F2}, 'mem_used': {mem_used:F2}, 'mem_total': {memory.Total:F2} }}";

            Console.WriteLine(output);
        }

        private class CpuInfo
        {
            public double Used;
            public double Free;
        }

        private class MemoryInfo
        {
            public double Total;
            public double Used;
            public double Free;
        }

        private static CpuInfo GetCpu()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "/bin/bash";
            psi.Arguments = "-c \"iostat -c\"";
            psi.RedirectStandardOutput = true;

            double free;            
    
            using(var process = Process.Start(psi))
            {
                var output = process.StandardOutput.ReadToEnd();

                var lines = output.Split("\n");

                free = double.Parse(lines[3].Split(" ", StringSplitOptions.RemoveEmptyEntries)[5]);
            }

            return new CpuInfo
            {
                Free = free,
                Used = 100d - free
            };
        }

        private static MemoryInfo GetMemory()
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "/bin/bash";
            psi.Arguments = "-c \"head --lines 2 /proc/meminfo\"";
            psi.RedirectStandardOutput = true;
            
            double total;
            double free;
    
            using(var process = Process.Start(psi))
            {
                var output = process.StandardOutput.ReadToEnd();

                var lines = output.Split("\n");

                total = double.Parse(lines[0].Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);
                free = double.Parse(lines[1].Split(" ", StringSplitOptions.RemoveEmptyEntries)[1]);
            }

            var info = new MemoryInfo();

            info.Total = total / 1024d;
            info.Free = free / 1024d;
            info.Used = (total - free) / 1024d;
    
            return info;            
        }
    }
}
