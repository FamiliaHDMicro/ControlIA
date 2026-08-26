using System;
using System.Diagnostics;
using System.Management;
using System.Text;

namespace ControlIA.Core
{
    public class HardwareDiagnostic
    {
        public double CpuAtual { get; private set; }
        public double MemDisponivelMb { get; private set; }
        public bool SaudeDiscoCritica { get; private set; }

        public string GerarRelatorioCompleto()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("=== Relatório de Saúde e Diagnóstico - ControlIA ===");
            sb.AppendLine($"Data do Diagnóstico: {DateTime.Now}");
            sb.AppendLine();

            sb.AppendLine("== Especificações do Hardware ==");
            sb.AppendLine($"Sistema Operacional: {GetOSInfo()}");
            sb.AppendLine($"Processador: {GetProcessorInfo()}");
            sb.AppendLine($"Memória Total: {GetTotalMemory()} MB");
            sb.AppendLine();

            sb.AppendLine("== Telemetria do Sistema ==");
            CpuAtual = GetCpuUsage();
            MemDisponivelMb = GetAvailableMemory();
            sb.AppendLine($"Uso Atual da CPU: {CpuAtual:F1} %");
            sb.AppendLine($"Memória Livre: {MemDisponivelMb} MB");
            sb.AppendLine();

            sb.AppendLine("== Saúde dos Discos e S.M.A.R.T. ==");
            VerificarSmart(sb);

            return sb.ToString();
        }

        private void VerificarSmart(StringBuilder sb)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Model, Status FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject drive in searcher.Get())
                    {
                        string modelo = drive["Model"]?.ToString() ?? "Disco";
                        string status = drive["Status"]?.ToString() ?? "Desconhecido";

                        sb.AppendLine($"Unidade: {modelo} | Status S.M.A.R.T.: {status}");

                        if (status != "OK")
                        {
                            SaudeDiscoCritica = true;
                            sb.AppendLine("  └─> [RISCO CRÍTICO] Sinais iminentes de falha física detectados!");
                        }
                    }
                }
            }
            catch
            {
                sb.AppendLine("Não foi possível acessar a telemetria S.M.A.R.T. dos discos.");
            }
        }

        private string GetOSInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                {
                    foreach (var os in searcher.Get())
                        return os["Caption"]?.ToString() ?? "Desconhecido";
                }
            }
            catch { }
            return "Desconhecido";
        }

        private string GetProcessorInfo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                {
                    foreach (var proc in searcher.Get())
                        return proc["Name"]?.ToString() ?? "Desconhecido";
                }
            }
            catch { }
            return "Desconhecido";
        }

        private string GetTotalMemory()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    foreach (var mem in searcher.Get())
                    {
                        ulong kb = (ulong)mem["TotalVisibleMemorySize"];
                        return (kb / 1024).ToString();
                    }
                }
            }
            catch { }
            return "Desconhecido";
        }

        private double GetAvailableMemory()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (var mem in searcher.Get())
                    {
                        ulong kb = (ulong)mem["FreePhysicalMemory"];
                        return kb / 1024;
                    }
                }
            }
            catch { }
            return 0;
        }

        private double GetCpuUsage()
        {
            try
            {
                using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue();
                    System.Threading.Thread.Sleep(200);
                    return cpuCounter.NextValue();
                }
            }
            catch
            {
                return 0;
            }
        }
    }
}
