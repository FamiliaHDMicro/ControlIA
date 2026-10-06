using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ControlIA.Core
{
    public static class ControlIACloudClient
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly string BaseUrl = "https://control-ia.hdmicro-ml.workers.dev";

        // --- 1. TELEMETRIA E EVENTOS ---
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

        // --- 2. VALIDAÇÃO DE LICENÇA ---
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

        // --- 3. IA: SOPHIA / ALICE (Texto e Consciência Ecossistêmica) ---
        public static async Task<string> ConsultarSophiaAsync(string mensagem, string origem = "Sophia_Atendimento")
        {
            try
            {
                string json = $"{{\"origem\":\"{origem}\",\"conteudo\":\"{mensagem.Replace("\"", "\\\"")}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync($"{BaseUrl}/api/ia/texto", content);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return "Erro ao comunicar com a Sophia na nuvem.";
            }
            catch (Exception ex)
            {
                return $"Exceção ao falar com a Sophia: {ex.Message}";
            }
        }

        // --- 4. IA: TRANSCRIÇÃO DE ÁUDIO DA PORTARIA (Whisper) ---
        public static async Task<string> TranscreverAudioPortariaAsync(string caminhoArquivoAudio)
        {
            try
            {
                using (var form = new MultipartFormDataContent())
                using (var fileStream = new FileStream(caminhoArquivoAudio, FileMode.Open, FileAccess.Read))
                {
                    form.Add(new StreamContent(fileStream), "audio", Path.GetFileName(caminhoArquivoAudio));

                    HttpResponseMessage response = await client.PostAsync($"{BaseUrl}/api/ia/audio", form);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                }
                return "Erro na transcrição do áudio.";
            }
            catch (Exception ex)
            {
                return $"Exceção ao transcrever áudio: {ex.Message}";
            }
        }

        // --- 5. CIA: MÓDULO DE EMERGÊNCIA POLICIAL (PM / Órgãos de Segurança) ---
        public static async Task<string> AcionarEmergenciaPolicialAsync(string codigoOcorrencia, string telefoneDelegacia = "", string apiEndpointPolicial = "")
        {
            try
            {
                string json = $"{{\"codigoOcorrencia\":\"{codigoOcorrencia}\",\"telefoneDelegacia\":\"{telefoneDelegacia}\",\"apiEndpointPolicial\":\"{apiEndpointPolicial}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync($"{BaseUrl}/api/cia/emergencia-policial", content);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return "Erro ao despachar alerta para as autoridades.";
            }
            catch (Exception ex)
            {
                return $"Exceção crítica no acionamento policial: {ex.Message}";
            }
        }
    }

    public static class UserSettings
    {
        public static bool SendTelemetryToCloud { get; set; } = true;
        public static string NomeTecnico { get; set; } = "Técnico ControlIA";
    }
}
