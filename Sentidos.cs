using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace ControlIA.Core
{
    /// <summary>
    /// Os sentidos da CIA: lê o corpo (o PC) de verdade.
    /// Só LÊ. Nunca altera, apaga ou fecha nada.
    /// Precisa rodar como Administrador para ler as temperaturas.
    /// Ler() é bloqueante: chame de uma thread de fundo (Task.Run).
    /// </summary>
    public sealed class Sentidos : IDisposable
    {
        private const string SeteDiasEmMs = "604800000";

        private readonly object _trava = new object();
        private Computer? _computer;
        private bool _tentouAbrir;
        private bool _descartado;

        public SinaisVitais Ler()
        {
            var s = new SinaisVitais { ColetadoEmLocal = DateTime.Now };

            LerTemperaturas(s);
            LerMemoria(s);
            LerDiscoDoSistema(s);
            LerSaudeDosDiscos(s);
            LerEnergia(s);
            LerHistoricoDoWindows(s);

            s.Chipset = LerChipset();
            s.InterfaceDisco = LerInterfaceDisco();
            return s;
        }

        // ------------------------------------------------------------------
        // Temperaturas e carga (LibreHardwareMonitor)
        // ------------------------------------------------------------------
        private void LerTemperaturas(SinaisVitais s)
        {
            lock (_trava)
            {
                if (_descartado)
                    return;

                try
                {
                    if (_computer == null && !_tentouAbrir)
                    {
                        _tentouAbrir = true;
                        var computer = new Computer
                        {
                            IsCpuEnabled = true,
                            IsMotherboardEnabled = true,
                            IsStorageEnabled = true
                        };
                        computer.Open();
                        _computer = computer;
                    }

                    if (_computer == null)
                        return;

                    foreach (IHardware hardware in _computer.Hardware)
                        Visitar(hardware, s);

                    s.SensoresTermicosDisponiveis =
                        s.TempCpuC.HasValue || s.TempPlacaMaeC.HasValue || s.TempDiscos.Count > 0;
                }
                catch
                {
                    s.SensoresTermicosDisponiveis = false;
                }
            }
        }

        private static void Visitar(IHardware hardware, SinaisVitais s)
        {
            hardware.Update();

            double? maiorTemperatura = null;
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue)
                    continue;

                double valor = sensor.Value.Value;

                if (sensor.SensorType == SensorType.Temperature && valor > 0 && valor < 150)
                    maiorTemperatura = Math.Max(maiorTemperatura ?? double.MinValue, valor);

                if (hardware.HardwareType == HardwareType.Cpu &&
                    sensor.SensorType == SensorType.Load &&
                    sensor.Name == "CPU Total")
                {
                    s.CpuUsoPercent = valor;
                }
            }

            if (maiorTemperatura.HasValue)
            {
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        s.TempCpuC = Math.Max(s.TempCpuC ?? double.MinValue, maiorTemperatura.Value);
                        break;
                    case HardwareType.Motherboard:
                    case HardwareType.SuperIO:
                        s.TempPlacaMaeC = Math.Max(s.TempPlacaMaeC ?? double.MinValue, maiorTemperatura.Value);
                        break;
                    case HardwareType.Storage:
                        s.TempDiscos.Add(new LeituraTermica
                        {
                            Nome = hardware.Name,
                            Celsius = maiorTemperatura.Value
                        });
                        break;
                }
            }

            foreach (IHardware sub in hardware.SubHardware)
                Visitar(sub, s);
        }

        // ------------------------------------------------------------------
        // Memória (valor real do Windows)
        // ------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        private static void LerMemoria(SinaisVitais s)
        {
            try
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (!GlobalMemoryStatusEx(ref status))
                    return;

                const double gb = 1024.0 * 1024 * 1024;
                s.MemoriaTotalGb = status.ullTotalPhys / gb;
                s.MemoriaLivreGb = status.ullAvailPhys / gb;
                s.MemoriaUsoPercent = status.dwMemoryLoad;
            }
            catch
            {
                // Fica nulo: "não consegui ler".
            }
        }

        // ------------------------------------------------------------------
        // Espaço do disco do sistema
        // ------------------------------------------------------------------
        private static void LerDiscoDoSistema(SinaisVitais s)
        {
            try
            {
                string? raiz = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrWhiteSpace(raiz))
                    return;

                var drive = new DriveInfo(raiz);
                if (!drive.IsReady)
                    return;

                const double gb = 1024.0 * 1024 * 1024;
                s.DiscoSistemaTotalGb = drive.TotalSize / gb;
                s.DiscoSistemaLivreGb = drive.AvailableFreeSpace / gb;
            }
            catch
            {
                // Fica nulo.
            }
        }

        // ------------------------------------------------------------------
        // Saúde dos discos (status do Windows + S.M.A.R.T. quando disponível)
        // ------------------------------------------------------------------
        private static void LerSaudeDosDiscos(SinaisVitais s)
        {
            try
            {
                using var buscador = new ManagementObjectSearcher("SELECT Model, Status FROM Win32_DiskDrive");
                foreach (ManagementObject disco in buscador.Get())
                {
                    using (disco)
                    {
                        s.Discos.Add(new DiscoInfo
                        {
                            Modelo = disco["Model"]?.ToString() ?? "Disco desconhecido",
                            Status = disco["Status"]?.ToString() ?? "Desconhecido"
                        });
                    }
                }
            }
            catch
            {
                // Lista vazia: "não consegui ler".
            }

            try
            {
                var escopo = new ManagementScope(@"\\.\root\wmi");
                var consulta = new ObjectQuery("SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
                using var buscador = new ManagementObjectSearcher(escopo, consulta);
                foreach (ManagementObject item in buscador.Get())
                {
                    using (item)
                    {
                        if (item["PredictFailure"] is bool preveFalha)
                        {
                            s.SmartConsultado = true;
                            if (preveFalha)
                                s.SmartPrevendoFalha = true;
                        }
                    }
                }
            }
            catch
            {
                // Muitos discos (principalmente NVMe) não respondem a essa consulta.
                // Nesse caso SmartConsultado continua false — a CIA diz que não conseguiu perguntar.
            }
        }

        // ------------------------------------------------------------------
        // Energia
        // ------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        private static void LerEnergia(SinaisVitais s)
        {
            try
            {
                if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS p))
                    return;

                bool semBateria = (p.BatteryFlag & 128) != 0 || p.BatteryFlag == 255;
                s.TemBateria = !semBateria;

                if (s.TemBateria)
                {
                    s.NaBateria = p.ACLineStatus == 0;
                    if (p.BatteryLifePercent <= 100)
                        s.BateriaPercent = p.BatteryLifePercent;
                }
            }
            catch
            {
                // Fica como "sem informação".
            }
        }

        // ------------------------------------------------------------------
        // Histórico do Windows: quedas de energia/travadas e erros de disco (7 dias)
        // ------------------------------------------------------------------
        private static void LerHistoricoDoWindows(SinaisVitais s)
        {
            s.QuedasInesperadas7Dias = ContarEventos(
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 " +
                "and TimeCreated[timediff(@SystemTime) <= " + SeteDiasEmMs + "]]]");

            s.ErrosDeDisco7Dias = ContarEventos(
                "*[System[((Provider[@Name='disk'] and (EventID=7 or EventID=11 or EventID=51)) " +
                "or (Provider[@Name='Ntfs'] and EventID=55)) " +
                "and TimeCreated[timediff(@SystemTime) <= " + SeteDiasEmMs + "]]]");
        }

        private static int? ContarEventos(string xpath)
        {
            try
            {
                var consulta = new EventLogQuery("System", PathType.LogName, xpath);
                using var leitor = new EventLogReader(consulta);

                int total = 0;
                EventRecord? registro;
                while ((registro = leitor.ReadEvent()) != null)
                {
                    using (registro)
                    {
                        total++;
                    }

                    if (total >= 100)
                        break;
                }

                return total;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Identificação das peças
        // ------------------------------------------------------------------
        public string LerChipset()
        {
            try
            {
                using var buscador = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
                foreach (ManagementObject item in buscador.Get())
                {
                    using (item)
                    {
                        string? produto = item["Product"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(produto))
                            return produto;
                    }
                }
            }
            catch
            {
                // Cai no padrão abaixo.
            }

            return "Não identificado";
        }

        public string LerInterfaceDisco()
        {
            try
            {
                var tipos = new List<string>();
                using var buscador = new ManagementObjectSearcher("SELECT InterfaceType, Model FROM Win32_DiskDrive");
                foreach (ManagementObject disco in buscador.Get())
                {
                    using (disco)
                    {
                        string modelo = (disco["Model"]?.ToString() ?? string.Empty).ToUpperInvariant();
                        string tipo;
                        if (modelo.Contains("NVME"))
                            tipo = "M.2 NVMe";
                        else if (modelo.Contains("SSD"))
                            tipo = "SSD";
                        else
                            tipo = disco["InterfaceType"]?.ToString() ?? "Desconhecida";

                        tipos.Add(tipo);
                    }
                }

                if (tipos.Count > 0)
                    return string.Join(", ", tipos.Distinct());
            }
            catch
            {
                // Cai no padrão abaixo.
            }

            return "Não identificada";
        }

        public void Dispose()
        {
            lock (_trava)
            {
                _descartado = true;
                try
                {
                    _computer?.Close();
                }
                catch
                {
                    // Nada a fazer ao fechar.
                }

                _computer = null;
            }
        }
    }
}
