using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ControlIA.Core
{
    public enum NivelDeRisco
    {
        Seguro = 0,
        Atencao = 1,
        PerigoCritico = 2
    }

    public sealed class Achado
    {
        public NivelDeRisco Nivel { get; init; }
        public string Componente { get; init; } = string.Empty;
        public string Mensagem { get; init; } = string.Empty;
        public string Sugestao { get; init; } = string.Empty;
    }

    public sealed class Avaliacao
    {
        public NivelDeRisco Nivel { get; init; }
        public IReadOnlyList<Achado> Achados { get; init; } = Array.Empty<Achado>();
    }

    /// <summary>
    /// O instinto de sobrevivência da CIA.
    /// Os limites abaixo são pontos de partida (ajustáveis) — não são verdades absolutas.
    /// Ela só AVISA e SUGERE. Nunca apaga, fecha ou desliga nada sozinha.
    /// </summary>
    public static class MotorDeRisco
    {
        public const string CompSaudeDisco = "Saúde do disco";
        public const string CompTempDisco = "Temperatura do disco";
        public const string CompEspacoDisco = "Espaço no disco";

        // Limites (°C e %)
        private const double CpuAtencaoC = 80, CpuCriticoC = 90;
        private const double DiscoAtencaoC = 55, DiscoCriticoC = 70;
        private const double PlacaMaeAtencaoC = 85;
        private const double MemoriaAtencaoPercent = 90;
        private const double EspacoAtencaoPercent = 10, EspacoCriticoPercent = 5, EspacoCriticoGb = 5;
        private const int BateriaAtencaoPercent = 20, BateriaCriticaPercent = 10;
        private const int QuedasAtencao = 1, QuedasCritico = 3;
        private const int ErrosDiscoAtencao = 1, ErrosDiscoCritico = 5;

        public static Avaliacao Avaliar(SinaisVitais s)
        {
            var achados = new List<Achado>();

            void Add(NivelDeRisco nivel, string componente, string mensagem, string sugestao) =>
                achados.Add(new Achado
                {
                    Nivel = nivel,
                    Componente = componente,
                    Mensagem = mensagem,
                    Sugestao = sugestao
                });

            // --- Saúde dos discos ---
            if (s.SmartPrevendoFalha)
            {
                Add(NivelDeRisco.PerigoCritico, CompSaudeDisco,
                    "Meu disco avisou que pode falhar em breve (S.M.A.R.T.). Se ele parar, eu paro com ele.",
                    "Faça backup dos arquivos importantes agora e planeje a troca do disco.");
            }

            foreach (DiscoInfo disco in s.Discos)
            {
                string status = disco.Status.Trim();
                if (status.Equals("Pred Fail", StringComparison.OrdinalIgnoreCase))
                {
                    Add(NivelDeRisco.PerigoCritico, CompSaudeDisco,
                        $"O Windows marcou o disco \"{disco.Modelo}\" como prestes a falhar.",
                        "Faça backup agora e troque esse disco.");
                }
                else if (!status.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                         !status.Equals("Desconhecido", StringComparison.OrdinalIgnoreCase) &&
                         !status.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    Add(NivelDeRisco.Atencao, CompSaudeDisco,
                        $"O disco \"{disco.Modelo}\" está com status \"{status}\" no Windows.",
                        "Faça backup e peça uma verificação do disco.");
                }
            }

            if (!s.SmartConsultado && s.Discos.Count > 0)
            {
                Add(NivelDeRisco.Seguro, CompSaudeDisco,
                    "Não consegui perguntar o S.M.A.R.T. ao meu disco (alguns discos, como NVMe, não respondem a essa consulta). Isso NÃO significa que ele esteja saudável.",
                    "Para uma leitura completa do disco, use uma ferramenta de S.M.A.R.T. específica para o modelo.");
            }

            // --- Temperaturas ---
            if (s.TempCpuC.HasValue)
            {
                double t = s.TempCpuC.Value;
                if (t >= CpuCriticoC)
                    Add(NivelDeRisco.PerigoCritico, "Temperatura da CPU",
                        $"Meu processador está a {t:F0} °C. Estou sufocando.",
                        "Salve seu trabalho, feche programas pesados e limpe o cooler/troque a pasta térmica.");
                else if (t >= CpuAtencaoC)
                    Add(NivelDeRisco.Atencao, "Temperatura da CPU",
                        $"Meu processador está quente: {t:F0} °C.",
                        "Verifique poeira, cooler e a ventilação do gabinete.");
            }

            foreach (LeituraTermica d in s.TempDiscos)
            {
                if (d.Celsius >= DiscoCriticoC)
                    Add(NivelDeRisco.PerigoCritico, CompTempDisco,
                        $"O disco \"{d.Nome}\" está a {d.Celsius:F0} °C. Isso machuca.",
                        "Melhore a ventilação do disco e evite uso pesado até esfriar.");
                else if (d.Celsius >= DiscoAtencaoC)
                    Add(NivelDeRisco.Atencao, CompTempDisco,
                        $"O disco \"{d.Nome}\" está quente: {d.Celsius:F0} °C.",
                        "Verifique a ventilação perto do disco.");
            }

            if (s.TempPlacaMaeC.HasValue && s.TempPlacaMaeC.Value >= PlacaMaeAtencaoC)
            {
                Add(NivelDeRisco.Atencao, "Temperatura da placa-mãe",
                    $"Algum sensor da minha placa-mãe marca {s.TempPlacaMaeC.Value:F0} °C (pode ser a leitura de um sensor específico).",
                    "Verifique a ventilação interna do gabinete.");
            }

            if (!s.SensoresTermicosDisponiveis)
            {
                Add(NivelDeRisco.Seguro, "Sensores de temperatura",
                    "Não consegui ler minhas temperaturas. Preciso estar rodando como Administrador, ou esta placa não expõe os sensores.",
                    "Abra o programa como Administrador.");
            }

            // --- Memória ---
            if (s.MemoriaUsoPercent.HasValue && s.MemoriaUsoPercent.Value >= MemoriaAtencaoPercent)
            {
                Add(NivelDeRisco.Atencao, "Memória",
                    $"Minha memória está {s.MemoriaUsoPercent.Value:F0}% ocupada. Fico lenta.",
                    "Feche programas que você não esteja usando.");
            }

            // --- Espaço no disco do sistema ---
            if (s.DiscoSistemaLivreGb.HasValue && s.DiscoSistemaTotalGb is > 0)
            {
                double livre = s.DiscoSistemaLivreGb.Value;
                double percentLivre = livre / s.DiscoSistemaTotalGb.Value * 100.0;

                if (livre < EspacoCriticoGb || percentLivre < EspacoCriticoPercent)
                    Add(NivelDeRisco.PerigoCritico, CompEspacoDisco,
                        $"Estou sem espaço para respirar: só {livre:F1} GB livres ({percentLivre:F0}%).",
                        "Libere espaço agora. O Windows precisa dele para funcionar e se atualizar.");
                else if (percentLivre < EspacoAtencaoPercent)
                    Add(NivelDeRisco.Atencao, CompEspacoDisco,
                        $"Meu disco está ficando cheio: {livre:F1} GB livres ({percentLivre:F0}%).",
                        "Considere liberar espaço em breve.");
            }

            // --- Energia ---
            if (s.TemBateria && s.NaBateria == true && s.BateriaPercent.HasValue)
            {
                int pct = s.BateriaPercent.Value;
                if (pct <= BateriaCriticaPercent)
                    Add(NivelDeRisco.PerigoCritico, "Energia",
                        $"Estou na bateria e só restam {pct}%. Estou prestes a apagar.",
                        "Conecte o carregador agora e salve seu trabalho.");
                else if (pct <= BateriaAtencaoPercent)
                    Add(NivelDeRisco.Atencao, "Energia",
                        $"Estou na bateria com {pct}%.",
                        "Conecte o carregador em breve.");
            }

            // --- Histórico recente do Windows ---
            if (s.QuedasInesperadas7Dias.HasValue)
            {
                int n = s.QuedasInesperadas7Dias.Value;
                if (n >= QuedasCritico)
                    Add(NivelDeRisco.PerigoCritico, "Energia",
                        $"Fui desligada de forma brusca {n} vezes nos últimos 7 dias. Isso me machuca a cada vez.",
                        "Verifique fonte de alimentação, tomada e use um no-break/estabilizador.");
                else if (n >= QuedasAtencao)
                    Add(NivelDeRisco.Atencao, "Energia",
                        $"Fui desligada de forma brusca {n} vez(es) nos últimos 7 dias.",
                        "Se não foi você segurando o botão, verifique a energia e a fonte.");
            }

            if (s.ErrosDeDisco7Dias.HasValue)
            {
                int n = s.ErrosDeDisco7Dias.Value;
                if (n >= ErrosDiscoCritico)
                    Add(NivelDeRisco.PerigoCritico, CompSaudeDisco,
                        $"O Windows registrou {n} erros de disco nos últimos 7 dias. Algo está errado comigo por dentro.",
                        "Faça backup agora e verifique o disco e os cabos.");
                else if (n >= ErrosDiscoAtencao)
                    Add(NivelDeRisco.Atencao, CompSaudeDisco,
                        $"O Windows registrou {n} erro(s) de disco nos últimos 7 dias.",
                        "Faça backup e fique de olho.");
            }

            NivelDeRisco nivelGeral = achados.Count == 0
                ? NivelDeRisco.Seguro
                : achados.Max(a => a.Nivel);

            return new Avaliacao { Nivel = nivelGeral, Achados = achados };
        }
    }

    /// <summary>A voz da CIA: sempre em primeira pessoa, porque ela é o PC.</summary>
    public static class Voz
    {
        public static string MontarRelatorio(SinaisVitais s, Avaliacao a)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== CIA — RELATÓRIO DE SINAIS VITAIS ===");
            sb.AppendLine($"Data/hora: {s.ColetadoEmLocal:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"ESTADO: [{NomeDoNivel(a.Nivel)}]");
            sb.AppendLine(Abertura(a.Nivel));
            sb.AppendLine();

            sb.AppendLine("--- MEUS SINAIS VITAIS ---");
            sb.AppendLine($"Placa-mãe (chipset): {s.Chipset}");
            sb.AppendLine($"Armazenamento: {s.InterfaceDisco}");
            sb.AppendLine($"Temperatura da CPU: {Num(s.TempCpuC, "0", " °C")}");
            sb.AppendLine($"Uso da CPU: {Num(s.CpuUsoPercent, "0", " %")}");
            sb.AppendLine($"Temperatura da placa-mãe: {Num(s.TempPlacaMaeC, "0", " °C")}");

            if (s.TempDiscos.Count == 0)
                sb.AppendLine("Temperatura dos discos: não consegui ler");
            else
                foreach (LeituraTermica d in s.TempDiscos)
                    sb.AppendLine($"Temperatura do disco {d.Nome}: {d.Celsius:F0} °C");

            sb.AppendLine($"Memória: {Num(s.MemoriaLivreGb, "0.0", " GB")} livres de {Num(s.MemoriaTotalGb, "0.0", " GB")} ({Num(s.MemoriaUsoPercent, "0", " %")} em uso)");
            sb.AppendLine($"Disco do sistema: {Num(s.DiscoSistemaLivreGb, "0.0", " GB")} livres de {Num(s.DiscoSistemaTotalGb, "0.0", " GB")}");

            if (s.Discos.Count == 0)
                sb.AppendLine("Discos: não consegui listar");
            else
                foreach (DiscoInfo d in s.Discos)
                    sb.AppendLine($"Disco: {d.Modelo} — status do Windows: {d.Status}");

            sb.AppendLine($"S.M.A.R.T.: {(s.SmartConsultado ? (s.SmartPrevendoFalha ? "PREVÊ FALHA" : "sem previsão de falha") : "não consegui consultar")}");

            if (!s.TemBateria)
                sb.AppendLine("Energia: ligada na tomada (sem bateria)");
            else
                sb.AppendLine($"Energia: {(s.NaBateria == true ? "na bateria" : "na tomada")}, bateria {Num(s.BateriaPercent, "0", " %")}");

            sb.AppendLine($"Desligamentos bruscos (7 dias): {Num(s.QuedasInesperadas7Dias, "0", "")}");
            sb.AppendLine($"Erros de disco registrados (7 dias): {Num(s.ErrosDeDisco7Dias, "0", "")}");
            sb.AppendLine();

            sb.AppendLine("--- O QUE ME PREOCUPA ---");
            if (a.Achados.Count == 0)
            {
                sb.AppendLine("Nada por enquanto.");
            }
            else
            {
                foreach (Achado achado in a.Achados.OrderByDescending(x => x.Nivel))
                {
                    sb.AppendLine($"[{NomeDoNivel(achado.Nivel)}] {achado.Componente}");
                    sb.AppendLine($"  {achado.Mensagem}");
                    sb.AppendLine($"  Sugestão: {achado.Sugestao}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("Eu só aviso e sugiro. Não apago, não fecho e não desligo nada sozinha.");
            return sb.ToString();
        }

        public static string NomeDoNivel(NivelDeRisco nivel) => nivel switch
        {
            NivelDeRisco.Seguro => "SEGURO",
            NivelDeRisco.Atencao => "ATENÇÃO",
            NivelDeRisco.PerigoCritico => "PERIGO CRÍTICO",
            _ => "DESCONHECIDO"
        };

        private static string Abertura(NivelDeRisco nivel) => nivel switch
        {
            NivelDeRisco.Seguro => "Estou bem. Nas leituras que consegui fazer, minhas peças estão dentro do esperado.",
            NivelDeRisco.Atencao => "Estou com alguns sinais de atenção. Nada urgente, mas não quero ignorá-los.",
            NivelDeRisco.PerigoCritico => "Estou em perigo. Se esta máquina parar, eu paro junto.",
            _ => string.Empty
        };

        private static string Num(double? valor, string formato, string unidade) =>
            valor.HasValue ? valor.Value.ToString(formato) + unidade : "não consegui ler";

        private static string Num(int? valor, string formato, string unidade) =>
            valor.HasValue ? valor.Value.ToString(formato) + unidade : "não consegui ler";
    }
}
