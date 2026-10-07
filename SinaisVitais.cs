using System;
using System.Collections.Generic;

namespace ControlIA.Core
{
    /// <summary>Uma temperatura lida de uma peça (ex.: um disco).</summary>
    public sealed class LeituraTermica
    {
        public string Nome { get; init; } = string.Empty;
        public double Celsius { get; init; }
    }

    /// <summary>Um disco como o Windows o enxerga.</summary>
    public sealed class DiscoInfo
    {
        public string Modelo { get; init; } = string.Empty;
        public string Status { get; init; } = "Desconhecido";
    }

    /// <summary>
    /// Tudo o que a CIA consegue sentir do próprio corpo (o PC) em um instante.
    /// Valor nulo significa "não consegui ler" — nunca "está tudo bem".
    /// </summary>
    public sealed class SinaisVitais
    {
        public DateTime ColetadoEmLocal { get; set; } = DateTime.Now;

        // Temperaturas e carga
        public bool SensoresTermicosDisponiveis { get; set; }
        public double? TempCpuC { get; set; }
        public double? CpuUsoPercent { get; set; }
        public double? TempPlacaMaeC { get; set; }
        public List<LeituraTermica> TempDiscos { get; } = new List<LeituraTermica>();

        // Memória
        public double? MemoriaTotalGb { get; set; }
        public double? MemoriaLivreGb { get; set; }
        public double? MemoriaUsoPercent { get; set; }

        // Disco do sistema (onde o Windows está instalado)
        public double? DiscoSistemaTotalGb { get; set; }
        public double? DiscoSistemaLivreGb { get; set; }

        // Saúde dos discos
        public List<DiscoInfo> Discos { get; } = new List<DiscoInfo>();
        public bool SmartConsultado { get; set; }
        public bool SmartPrevendoFalha { get; set; }

        // Energia
        public bool TemBateria { get; set; }
        public bool? NaBateria { get; set; }
        public int? BateriaPercent { get; set; }

        // Histórico recente do Windows (últimos 7 dias)
        public int? QuedasInesperadas7Dias { get; set; }
        public int? ErrosDeDisco7Dias { get; set; }

        // Identificação das peças
        public string Chipset { get; set; } = "Não identificado";
        public string InterfaceDisco { get; set; } = "Não identificada";
    }
}
