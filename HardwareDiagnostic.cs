using System;
using System.Linq;

namespace ControlIA.Core
{
    /// <summary>
    /// Fachada simples sobre a CIA (Sentidos + MotorDeRisco + Voz).
    /// Mantém os mesmos nomes que o resto do app já usa.
    /// Use com "using" para liberar os sensores ao terminar.
    /// </summary>
    public sealed class HardwareDiagnostic : IDisposable
    {
        private readonly Sentidos _sentidos = new Sentidos();

        public SinaisVitais? UltimosSinais { get; private set; }
        public Avaliacao? UltimaAvaliacao { get; private set; }

        /// <summary>True somente se os sinais REAIS do disco indicam perigo (saúde/temperatura).</summary>
        public bool SaudeDiscoCritica { get; private set; }

        public string NivelRiscoGestao { get; private set; } = "SEGURO";

        /// <summary>Bloqueante: chame de uma thread de fundo (Task.Run) quando estiver na interface.</summary>
        public string GerarRelatorioCompleto()
        {
            SinaisVitais sinais = _sentidos.Ler();
            Avaliacao avaliacao = MotorDeRisco.Avaliar(sinais);

            UltimosSinais = sinais;
            UltimaAvaliacao = avaliacao;
            NivelRiscoGestao = Voz.NomeDoNivel(avaliacao.Nivel);
            SaudeDiscoCritica = avaliacao.Achados.Any(a =>
                a.Nivel == NivelDeRisco.PerigoCritico &&
                (a.Componente == MotorDeRisco.CompSaudeDisco ||
                 a.Componente == MotorDeRisco.CompTempDisco));

            return Voz.MontarRelatorio(sinais, avaliacao);
        }

        public string ObterChipsetPlacaMae() => UltimosSinais?.Chipset ?? _sentidos.LerChipset();

        public string ObterTipoInterfaceDisco() => UltimosSinais?.InterfaceDisco ?? _sentidos.LerInterfaceDisco();

        public void Dispose() => _sentidos.Dispose();
    }
}
