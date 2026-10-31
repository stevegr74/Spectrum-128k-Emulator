namespace Spectrum128kEmulator
{
    public partial class MainForm
    {
        private void DumpMachineDebugState()
        {
            try
            {
                string dump = string.Empty;
                ExecuteWithEmulationPaused(() =>
                {
                    dump = machine.BuildDebugDump("Manual F12 debug dump.");
                });
                WriteDebugDumpToFile(dump, "Manual F12");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to write debug dump:\n{ex.Message}",
                    "Debug Dump Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void WriteDebugDumpToFile(string dump, string reason)
        {
            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            string fileName = $"machine-debug-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
            string path = Path.Combine(debugFolder, fileName);
            File.WriteAllText(path, dump);
        }

        private void WriteCrashReport(Exception ex, string context)
        {
            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            string fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
            string path = Path.Combine(debugFolder, fileName);

            string report =
                $"Context: {context}{Environment.NewLine}" +
                $"Exception: {ex}{Environment.NewLine}{Environment.NewLine}" +
                BuildCrashDiagnosticDump(context);

            File.WriteAllText(path, report);
        }

        public string BuildCrashDiagnosticDump(string context)
        {
            try
            {
                string dump = string.Empty;
                ExecuteWithEmulationPaused(() =>
                {
                    dump = machine.BuildDebugDump($"Crash context: {context}");
                });
                return dump;
            }
            catch (Exception dumpEx)
            {
                return $"Failed to build machine dump: {dumpEx}";
            }
        }

        private void InitializeInputDiagnostics()
        {
            if (!EnableInputDiagnostics)
                return;

            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            inputDiagnosticsPath = Path.Combine(debugFolder, $"input-diagnostics-{DateTime.Now:yyyyMMdd-HHmmssfff}.log");
            inputDiagnosticsWriter = new StreamWriter(new FileStream(inputDiagnosticsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
            LogInputDiagnostic("init", null, $"path={inputDiagnosticsPath}");
        }

        private void InitializePerformanceDiagnostics()
        {
            if (!EnablePerformanceDiagnostics)
                return;

            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            string performancePath = Path.Combine(debugFolder, $"performance-diagnostics-{DateTime.Now:yyyyMMdd-HHmmssfff}.log");
            performanceDiagnosticsWriter = new StreamWriter(new FileStream(performancePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        private string DescribeBridgeState(Keys key)
        {
            return keyboardInputController.DescribeKeyState(key, machine.GetKeyboardRowScanCount);
        }

        private string DescribeKeyboardMatrix(Keys key)
        {
            int[] rows = SpectrumKeyboardMapping.GetRows(key);
            if (rows.Length == 0)
                return "matrixRows=[]";

            byte[] matrix = machine.GetKeyboardMatrixCopy();
            return "matrixRows=[" + string.Join(",", rows.Select(row => $"{row}:{matrix[row]:X2}/scan={machine.GetKeyboardRowScanCount(row)}")) + "]";
        }

        private void LogInputDiagnostic(string phase, Keys? key, string extra)
        {
            if (!EnableInputDiagnostics)
                return;

            lock (inputDiagnosticsLock)
            {
                if (inputDiagnosticsWriter == null)
                    return;

                string keyPart = key.HasValue ? $" key={key.Value}" : string.Empty;
                string matrixPart = key.HasValue ? $" {DescribeKeyboardMatrix(key.Value)}" : string.Empty;
                inputDiagnosticsWriter.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} phase={phase}{keyPart} inputTStates={inputEmulatedTStates} frame={machine.FrameCount} tstates={machine.Cpu.TStates} pc=0x{machine.Cpu.Regs.PC:X4}{matrixPart} {extra}");
            }
        }

        private void RecordPerformanceSample(
            long nowTicks,
            int completedFrames,
            bool presentedFrame,
            double executeSliceMilliseconds,
            double audioSubmitMilliseconds,
            double renderMilliseconds,
            double presentMilliseconds)
        {
            if (!EnablePerformanceDiagnostics)
                return;

            lock (performanceDiagnosticsLock)
            {
                performanceTickCount++;
                performanceCompletedFrames += completedFrames;
                if (presentedFrame)
                    performancePresentedFrames++;
                performanceExecuteSliceMilliseconds += executeSliceMilliseconds;
                performanceAudioSubmitMilliseconds += audioSubmitMilliseconds;
                performanceRenderMilliseconds += renderMilliseconds;
                performancePresentMilliseconds += presentMilliseconds;

                long ticksPerSecond = System.Diagnostics.Stopwatch.Frequency;
                if (nowTicks - lastPerformanceStatsTicks < ticksPerSecond || performanceDiagnosticsWriter == null)
                    return;

                performanceDiagnosticsWriter.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} ticks={performanceTickCount} completedFrames={performanceCompletedFrames} presentedFrames={performancePresentedFrames} " +
                    $"turboActive={IsTurboTapeLoadActive(machine)} protected={machine.MountedTape?.IsStreamingProtectedByteStream == true} " +
                    $"execMs={performanceExecuteSliceMilliseconds:F2} audioMs={performanceAudioSubmitMilliseconds:F2} renderMs={performanceRenderMilliseconds:F2} presentMs={performancePresentMilliseconds:F2} " +
                    $"presentedFps={framesRenderedThisSecond} frame={machine.FrameCount}");

                performanceTickCount = 0;
                performanceCompletedFrames = 0;
                performancePresentedFrames = 0;
                performanceExecuteSliceMilliseconds = 0;
                performanceAudioSubmitMilliseconds = 0;
                performanceRenderMilliseconds = 0;
                performancePresentMilliseconds = 0;
                lastPerformanceStatsTicks = nowTicks;
            }
        }
    }
}
