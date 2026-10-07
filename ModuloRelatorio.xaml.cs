using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ControlIA.Core;
using Microsoft.Win32;

namespace NexusAdminTool
{
    public partial class ModuloRelatorio : Window
    {
        private readonly HardwareDiagnostic _diagnostic = new HardwareDiagnostic();
        private string _relatorioTexto = string.Empty;
        private bool _fechando;

        public ModuloRelatorio()
        {
            InitializeComponent();
            _ = CarregarAsync();
        }

        private async Task CarregarAsync()
        {
            txtRelatorio.Text = "Estou sentindo as minhas peças... aguarde alguns segundos.";

            try
            {
                string texto = await Task.Run(() => _diagnostic.GerarRelatorioCompleto());
                if (_fechando)
                    return;

                _relatorioTexto = texto;
                txtRelatorio.Text = texto;
            }
            catch (Exception ex)
            {
                if (_fechando)
                    return;

                txtRelatorio.Text = "Não consegui concluir a leitura das minhas peças:\n" + ex.Message;
            }
        }

        private async void btnEnviarCloud_Click(object sender, RoutedEventArgs e)
        {
            SinaisVitais? sinais = _diagnostic.UltimosSinais;
            if (sinais == null)
            {
                MessageBox.Show(
                    "Aguarde a leitura terminar antes de enviar.",
                    "ControlIA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult confirmacao = MessageBox.Show(
                "Serão enviados ao servidor ControlIA somente: uso de CPU, memória livre e espaço livre no disco. Nada além disso. Deseja enviar?",
                "Confirmar envio", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmacao != MessageBoxResult.Yes)
                return;

            btnEnviarCloud.IsEnabled = false;
            try
            {
                var payload = new TelemetryPayload
                {
                    Tipo = "relatorio_saude",
                    TimestampUtc = DateTime.UtcNow,
                    CpuPercentual = sinais.CpuUsoPercent ?? 0,
                    MemoriaDisponivelMb = (sinais.MemoriaLivreGb ?? 0) * 1024,
                    DiscoLivreGb = sinais.DiscoSistemaLivreGb ?? 0
                };

                bool enviado = await ControlIACloudClient.EnviarTelemetriaAsync(payload);
                MessageBox.Show(
                    enviado
                        ? "Enviado com sucesso."
                        : "Não consegui enviar agora. Os dados continuam somente nesta máquina.",
                    "ControlIA", MessageBoxButton.OK,
                    enviado ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            finally
            {
                if (!_fechando)
                    btnEnviarCloud.IsEnabled = true;
            }
        }

        private void btnExportarTxt_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_relatorioTexto))
            {
                MessageBox.Show(
                    "O relatório ainda não está pronto.",
                    "ControlIA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialogo = new SaveFileDialog
            {
                Filter = "Texto (*.txt)|*.txt",
                FileName = $"CIA-sinais-vitais-{DateTime.Now:yyyyMMdd-HHmm}.txt"
            };

            if (dialogo.ShowDialog(this) != true)
                return;

            try
            {
                File.WriteAllText(dialogo.FileName, _relatorioTexto, Encoding.UTF8);
                MessageBox.Show(
                    "Relatório salvo.",
                    "ControlIA", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível salvar o arquivo:\n" + ex.Message,
                    "ControlIA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnVoltar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _fechando = true;
            _diagnostic.Dispose();
            base.OnClosed(e);
        }
    }
}
