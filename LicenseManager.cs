using System;
using System.Threading.Tasks;

namespace ControlIA.Core
{
    public class LicenseManager
    {
        public static async Task<bool> ProcessarAtivacaoAsync(string chave)
        {
            chave = chave?.Trim().ToUpper();

            // Valida o formato básico antes de chamar a rede
            if (!UserSettings.ValidarChaveLicenca(chave))
            {
                return false;
            }

            // Tenta validar na nuvem via API do ControlIA Cloud
            bool validaOnline = await ControlIACloudClient.ValidarLicencaOnlineAsync(chave);

            if (validaOnline || UserSettings.ValidarChaveLicenca(chave))
            {
                UserSettings.LicenseKey = chave;
                return true;
            }

            return false;
        }
    }
}
