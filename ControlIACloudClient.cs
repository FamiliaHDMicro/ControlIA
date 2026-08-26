using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ControlIA.Core
{
    public static class ControlIACloudClient
    {
        private static readonly HttpClient client = new HttpClient();
        
        // URL da sua API no Cloudflare Workers
        private const string BaseUrl = "https://control-ia.hdmicro-ml.workers.dev";

        /// <summary>
        /// Envia a telemetria do hardware para a nuvem
        /// </summary>
        public static async Task<bool> EnviarTelemetriaAsync(string jsonPayload)
        {
            try
            {
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync($"{BaseUrl}/api/telemetria", content);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao conectar com a nuvem do ControlIA: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Valida a chave de licença online
        /// </summary>
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
    }
}
