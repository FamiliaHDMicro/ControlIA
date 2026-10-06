using System;
using System.Threading.Tasks;

namespace ControlIA.Core
{
    public class LicenseManager
    {
        public static async Task<bool> ProcessarAtivacaoAsync(string chave)
        {
            chave = chave?.Trim().ToUpper();

            // Valida o formato básico antes de chamar a rede (evita requisições inúteis)
            if (string.IsNullOrEmpty(chave) || !UserSettings.ValidarChaveLicenca(chave))
            {
                return false;
            }

            try
            {
                // Tenta validar rigorosamente na nuvem via API do ControlIA Cloud
                bool validaOnline = await ControlIACloudClient.ValidarLicencaOnlineAsync(chave);

                if (validaOnline)
                {
                    UserSettings.LicenseKey = chave;
                    return true;
                }
                
                // Se o servidor respondeu explicitamente que é inválida, rejeita na hora
                return false;
            }
            catch (Exception)
            {
                // Fallback seguro em caso de queda de internet: 
                // Apenas permite se houver uma chave offline validada por criptografia local (ex: arquivo assinado)
                // Evite liberar acesso irrestrito se a rede cair.
                bool tokenOfflineValido = UserSettings.ValidarAssinaturaOffline(chave);
                
                if (tokenOfflineValido)
                {
                    UserSettings.LicenseKey = chave;
                    return true;
                }

                return false;
            }
        }
    }
}
