using System;
using System.Diagnostics;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

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
            sb.AppendLine($"Chipset Detectado: {ObterChipsetPlacaMae()}");
            sb.AppendLine();

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
                            sb.AppendLine("  └─> [RISCO CRÍTICO] Falha física iminente!");
                        }
                    }
                }
            }
            catch
            {
                sb.AppendLine("Acesso S.M.A.R.T. indisponível.");
            }
        }

        public string ObterChipsetPlacaMae()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        return obj["Product"]?.ToString() ?? "Chipset Genérico";
                }
            }
            catch { }
            return "Chipset Genérico";
        }

        public string ObterTipoInterfaceDisco()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Model, InterfaceType FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string model = obj["Model"]?.ToString().ToUpper() ?? "";
                        if (model.Contains("NVME")) return "M.2 NVMe PCIe";
                        if (model.Contains("SSD")) return "SATA III SSD";
                        return obj["InterfaceType"]?.ToString() ?? "SATA";
                    }
                }
            }
            catch { }
            return "SATA III";
        }

        public static async Task BuscarPecasCompativeisAsync(string chipset, string interfaceDisco)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = $"https://control-ia.hdmicro-ml.workers.dev/api/buscar-pecas?chipset={Uri.EscapeDataString(chipset)}&interface={Uri.EscapeDataString(interfaceDisco)}";
                    await client.GetAsync(url);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro na consulta de peças: {ex.Message}");
            }
        }
    }
}
