using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ControlIA.Core
{
    public static class ControlIACloudClient
    {
        private static readonly HttpClient client = new HttpClient();
        private const string BaseUrl = "https://control-ia.hdmicro-ml.workers.dev";

        public static async Task<bool> EnviarTelemetriaAsync(string jsonPayload, string tecnicoCliente)
        {
            try
            {
                string body = $"{{\"tecnicoCliente\":\"{tecnicoCliente}\",\"relatorio\":{jsonPayload}}}";
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync($"{BaseUrl}/api/telemetria", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro de conexão com Cloud ControlIA: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> ValidarLicencaOnlineAsync(string chave)
        {
            try
            {
                string json = $"{{\"chave\":\"{chave}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync($"{BaseUrl}/api/validar-licenca", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    public static class UserSettings
    {
        public static bool SendTelemetryToCloud { get; set; } = true;
        public static string NomeTecnico { get; set; } = "Técnico ControlIA";
    }
}
