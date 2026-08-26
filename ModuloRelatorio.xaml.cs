using System;
using System.IO;
using System.Speech.Synthesis;
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
            _relatorioTexto = await Task.Run(() => _diagnostic.GerarRelatorioCompleto());
            txtRelatorio.Text = _relatorioTexto;

            // Voz nativa do Windows avisando a conclusão do teste
            using (SpeechSynthesizer synth = new SpeechSynthesizer())
            {
                synth.SpeakAsync("Diagnóstico do ControlIA concluído com sucesso.");
            }

            if (_diagnostic.SaudeDiscoCritica)
            {
                MessageBox.Show(
                    "ALERTA DA IA: Detectada degradação física no disco. Substituição recomendada em até 72 horas.",
                    "Autodefesa ControlIA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Arquivo de Texto (*.txt)|*.txt",
                FileName = $"Relatorio_ControlIA_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveDialog.FileName, _relatorioTexto);
                MessageBox.Show("Relatório salvo com sucesso!", "ControlIA", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnEnviarNuvem_Click(object sender, RoutedEventArgs e)
        {
            if (UserSettings.SendTelemetryToCloud)
            {
                bool enviado = await ControlIACloudClient.EnviarTelemetriaAsync(_relatorioTexto);
                if (enviado)
                {
                    MessageBox.Show("Telemetria enviada para a nuvem do ControlIA!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Falha ao enviar telemetria para a nuvem.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
