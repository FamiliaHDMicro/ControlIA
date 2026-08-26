using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace ControlIA.UI
{
    public partial class ModuloRelatorio : Window
    {
        private readonly HardwareDiagnostic _diagnostic;
        private string _relatorioTexto;

        public ModuloRelatorio()
        {
            InitializeComponent();
            _diagnostic = new HardwareDiagnostic();
            _ = InicializarRelatorioAsync();
        }

        private async Task InicializarRelatorioAsync()
        {
            // Executa a medição em segundo plano para NÃO travar a janela
            _relatorioTexto = await Task.Run(() => _diagnostic.GerarRelatorioCompleto());
            txtRelatorio.Text = _relatorioTexto;

            if (_diagnostic.SaudeDiscoCritica)
            {
                MessageBox.Show(
                    "ALERTA DA IA: Detectada degradação física no disco. Substituição recomendada em até 72 horas.",
                    "Autodefesa ControlIA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnExportarTxt_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Arquivo de Texto (*.txt)|*.txt",
                FileName = $"RelatorioSaude_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, _relatorioTexto);
                    MessageBox.Show("Relatório exportado com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao salvar o arquivo:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void btnEnviarCloud_Click(object sender, RoutedEventArgs e)
        {
            if (!UserSettings.SendTelemetryToCloud)
            {
                var confirmar = MessageBox.Show(
                    "Deseja ativar e enviar os dados de telemetria para os responsáveis (Técnico / Gerente / Usuário)?",
                    "ControlIA Cloud", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirmar != MessageBoxResult.Yes) return;
                UserSettings.SendTelemetryToCloud = true;
            }

            btnEnviarCloud.IsEnabled = false;
            btnEnviarCloud.Content = "Enviando Telemetria...";

            string json = $"{{\"maquina\":\"{Environment.MachineName}\"," +
                           $"\"cpu\":{_diagnostic.CpuAtual.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                           $"\"memoriaDisponivelMb\":{_diagnostic.MemDisponivelMb.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                           $"\"statusDiscoCritico\":{_diagnostic.SaudeDiscoCritica.ToString().ToLower()}," +
                           $"\"timestamp\":\"{DateTime.UtcNow:o}\"}}";

            bool ok = await ControlIACloudClient.EnviarTelemetriaAsync(json);

            btnEnviarCloud.IsEnabled = true;
            btnEnviarCloud.Content = "☁️ Enviar para ControlIA Cloud";

            MessageBox.Show(ok ? "Alerta e relatório enviados com sucesso!" : "Modo Offline: O relatório foi saved localmente e será sincronizado quando houver conexão.",
                "ControlIA Cloud", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void btnVoltar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
