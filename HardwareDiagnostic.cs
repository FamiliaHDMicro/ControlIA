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
        public bool SaudeDiscoCritica { get; private set; }
        public bool RiscoTermicoCritico { get; private set; }
        public string NivelRiscoGestao { get; private set; } = "SEGURO";

        public string GerarRelatorioCompleto()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== RELATÓRIO DE VISTORIA E GESTÃO DE RISCO - CONTROLIA ===");
            sb.AppendLine($"Data/Hora da Auditoria: {DateTime.Now}");
            sb.AppendLine($"Chipset Principal: {ObterChipsetPlacaMae()}");
            sb.AppendLine($"Controlador Gráfico: {ObterPlacaVideo()}");
            sb.AppendLine($"Interface de Armazenamento: {ObterTipoInterfaceDisco()}");
            sb.AppendLine();

            sb.AppendLine("--- ANÁLISE DE SAÚDE S.M.A.R.T. E PERIGO FÍSICO ---");
            VerificarSmart(sb);
            
            sb.AppendLine();
            VerificarEstresseSistema(sb);

            sb.AppendLine();
            sb.AppendLine($"=== STATUS DE GESTÃO DA CIA: [{NivelRiscoGestao}] ===");
            
            return sb.ToString();
        }

        private void VerificarSmart(StringBuilder sb)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Model, Status, PNPDeviceID FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject drive in searcher.Get())
                    {
                        string modelo = drive["Model"]?.ToString() ?? "Disco Desconhecido";
                        string status = drive["Status"]?.ToString() ?? "Desconhecido";

                        sb.AppendLine($"Unidade: {modelo} | S.M.A.R.T.: {status}");

                        if (status != "OK" && !string.IsNullOrEmpty(status))
                        {
                            SaudeDiscoCritica = true;
                            NivelRiscoGestao = "PERIGO CRÍTICO (FALHA DE DISCO IMINENTE)";
                            sb.AppendLine("  └─> [ALERTA VERMELHO] Risco iminente de perda de dados!");
                        }
                    }
                }
            }
            catch
            {
                sb.AppendLine("Acesso restrito ao S.M.A.R.T. WMI.");
            }
        }

        private void VerificarEstresseSistema(StringBuilder sb)
        {
            try
            {
                // Verificação rápida de memória disponível
                using (var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        double livreKb = Convert.ToDouble(obj["FreePhysicalMemory"]);
                        double totalKb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        double usoPorcentagem = ((totalKb - livreKb) / totalKb) * 100;

                        sb.AppendLine($"Uso de Memória RAM: {usoPorcentagem:F1}%");

                        if (usoPorcentagem > 90 && NivelRiscoGestao == "SEGURO")
                        {
                            NivelRiscoGestao = "ALERTA MODERADO (ESTRESSE DE MEMÓRIA)";
                        }
                    }
                }
            }
            catch
            {
                sb.AppendLine("Monitoramento de memória indisponível no momento.");
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

        public string ObterPlacaVideo()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        return obj["Name"]?.ToString() ?? "Controlador Padrão";
                }
            }
            catch { }
            return "Controlador Genérico";
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

        public static async Task EnviarRelatorioTelemetriaAsync(string chave, string tecnico, string cidade, string relatorio, string nivelRisco)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = "https://control-ia.hdmicro-ml.workers.dev/api/telemetria";
                    var payload = new
                    {
                        chave = chave,
                        tecnicoCliente = tecnico,
                        evento = nivelRisco.Contains("PERIGO") ? "PERIGO_HARDWARE_CRITICO" : "DIAGNOSTICO_CONCLUIDO",
                        detalhes = relatorio,
                        cidadeLocal = cidade
                    };

                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                    HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                    await client.PostAsync(url, content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao enviar telemetria para a CIA: {ex.Message}");
            }
        }
    }
}
