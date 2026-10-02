using System;
using System.IO;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ControlIA.Core;

namespace ControlIA.UI
{
    public partial class ModuloRelatorio : Window
    {
        private readonly HardwareDiagnostic _diagnostic;
        private string _relatorioTexto;
        private DispatcherTimer _timerRemocao;
        private int _segundosRestantes = 30;

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

            using (SpeechSynthesizer synth = new SpeechSynthesizer())
            {
                synth.SpeakAsync("Diagnóstico do ControlIA concluído com sucesso. Aguarde a liberação do pendrive.");
            }

            if (_diagnostic.SaudeDiscoCritica)
            {
                string chipset = _diagnostic.ObterChipsetPlacaMae();
                string tipoInterface = _diagnostic.ObterTipoInterfaceDisco();
                
                _ = HardwareDiagnostic.BuscarPecasCompativeisAsync(chipset, tipoInterface);

                MessageBox.Show(
                    $"ALERTA DA IA: Detectada degradação física no armazenamento.\n" +
                    $"Compatibilidade para Chipset [{chipset}]: {tipoInterface}.\n" +
                    $"Buscando substitutos com melhores preços na nuvem...",
                    "Autodefesa ControlIA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            IniciarContagemRemocaoPendrive();
        }

        private void IniciarContagemRemocaoPendrive()
        {
            _timerRemocao = new DispatcherTimer();
            _timerRemocao.Interval = TimeSpan.FromSeconds(1);
            _timerRemocao.Tick += (s, e) =>
            {
                _segundosRestantes--;
                Title = $"ControlIA - Finalizando processos... Pode remover o pendrive em: {_segundosRestantes}s";

                if (_segundosRestantes <= 0)
                {
                    _timerRemocao.Stop();
                    Title = "ControlIA - Operação concluída. Pendrive liberado!";
                    
                    using (SpeechSynthesizer synth = new SpeechSynthesizer())
                    {
                        synth.SpeakAsync("Unidade portátil liberada. Você pode remover o pendrive com segurança.");
                    }
                }
            };
            _timerRemocao.Start();
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
                bool enviado = await ControlIACloudClient.EnviarTelemetriaAsync(_relatorioTexto, UserSettings.NomeTecnico);
                if (enviado)
                {
                    MessageBox.Show("Telemetria enviada para a nuvem!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Falha ao enviar telemetria.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
