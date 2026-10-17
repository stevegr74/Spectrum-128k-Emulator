namespace Spectrum128kEmulator
{
    public partial class MainForm
    {
        private void InitializeKeyboard()
        {
            KeyPreview = true;
            PreviewKeyDown += MainForm_PreviewKeyDown;
            KeyDown += MainForm_KeyDown;
            KeyUp += MainForm_KeyUp;
            Deactivate += MainForm_Deactivate;
            screenBox.MouseClick += (_, _) => screenBox.Focus();
            screenBox.PreviewKeyDown += MainForm_PreviewKeyDown;
            Shown += (_, _) => screenBox.Focus();
        }

        private void MainForm_Deactivate(object? sender, EventArgs e)
        {
            LogInputDiagnostic("deactivate", null, "clearing input state");
            ClearHostAndSpectrumInputState();
        }

        private void MainForm_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (IsSpectrumMappedKey(e.KeyCode))
                e.IsInputKey = true;
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F4 && e.Alt)
                return;

            if (IsPlainShortcut(e, Keys.F1))
            {
                ToggleHelpWindow();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F2))
            {
                ToggleStatusOverlay();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F3))
            {
                ToggleMachineModel();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F5))
            {
                ToggleTapeTransport();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F6))
            {
                ToggleDisassemblerWindow();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F7))
            {
                SaveQuickState();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F8))
            {
                RestoreQuickState();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.F4 && !e.Alt && !e.Control && !e.Shift)
            {
                SelectDisplayMode(SpectrumDisplayModes.Next(displayMode));
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsSpectrumMappedKey(e.KeyCode))
            {
                LogInputDiagnostic("keydown", e.KeyCode, "mapped event");
                QueueSpectrumHostKeyEvent(e.KeyCode, true);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            HandleKey(e.KeyCode, true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void MainForm_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F4 && e.Alt)
                return;

            if (IsPlainShortcut(e, Keys.F1) || IsPlainShortcut(e, Keys.F2) ||
                IsPlainShortcut(e, Keys.F3) || IsPlainShortcut(e, Keys.F5) ||
                IsPlainShortcut(e, Keys.F6) || IsPlainShortcut(e, Keys.F7) ||
                IsPlainShortcut(e, Keys.F8))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.F4 && !e.Alt && !e.Control && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsSpectrumMappedKey(e.KeyCode))
            {
                LogInputDiagnostic("keyup", e.KeyCode, "mapped event");
                QueueSpectrumHostKeyEvent(e.KeyCode, false);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            HandleKey(e.KeyCode, false);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void HandleKey(Keys key, bool pressed)
        {
            if (pressed && key == Keys.F9)
            {
                LoadSnaSnapshotFromDialog();
                return;
            }

            if (pressed && key == Keys.F10)
            {
                LoadSnapshotOrRecordingFromDialog();
                return;
            }

            if (pressed && key == Keys.F11)
            {
                MountTapFromDialog();
                return;
            }

            if (pressed && key == Keys.F12)
            {
                DumpMachineDebugState();
                return;
            }

            QueueSpectrumHostKeyEvent(key, pressed);
        }

        private bool IsSpectrumMappedKey(Keys key)
        {
            return key switch
            {
                Keys.Left or Keys.Down or Keys.Up or Keys.Right or Keys.Back => true,
                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => true,
                Keys.Z or Keys.X or Keys.C or Keys.V => true,
                Keys.A or Keys.S or Keys.D or Keys.F or Keys.G => true,
                Keys.Q or Keys.W or Keys.E or Keys.R or Keys.T => true,
                Keys.D1 or Keys.NumPad1 or Keys.D2 or Keys.NumPad2 or Keys.D3 or Keys.NumPad3 or
                Keys.D4 or Keys.NumPad4 or Keys.D5 or Keys.NumPad5 => true,
                Keys.D0 or Keys.NumPad0 or Keys.D9 or Keys.NumPad9 or Keys.D8 or Keys.NumPad8 or
                Keys.D7 or Keys.NumPad7 or Keys.D6 or Keys.NumPad6 => true,
                Keys.P or Keys.O or Keys.I or Keys.U or Keys.Y => true,
                Keys.Enter or Keys.L or Keys.K or Keys.J or Keys.H => true,
                Keys.Space or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
                Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.M or Keys.N or Keys.B => true,
                _ => false
            };
        }

        private int[] GetSpectrumKeyRows(Keys key)
        {
            return key switch
            {
                Keys.Left => new[] { 0, 3 },
                Keys.Down => new[] { 0, 4 },
                Keys.Up => new[] { 0, 4 },
                Keys.Right => new[] { 0, 4 },
                Keys.Back => new[] { 0, 4 },
                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
                Keys.Z or Keys.X or Keys.C or Keys.V => new[] { 0 },
                Keys.A or Keys.S or Keys.D or Keys.F or Keys.G => new[] { 1 },
                Keys.Q or Keys.W or Keys.E or Keys.R or Keys.T => new[] { 2 },
                Keys.D1 or Keys.NumPad1 or Keys.D2 or Keys.NumPad2 or Keys.D3 or Keys.NumPad3 or
                Keys.D4 or Keys.NumPad4 or Keys.D5 or Keys.NumPad5 => new[] { 3 },
                Keys.D0 or Keys.NumPad0 or Keys.D9 or Keys.NumPad9 or Keys.D8 or Keys.NumPad8 or
                Keys.D7 or Keys.NumPad7 or Keys.D6 or Keys.NumPad6 => new[] { 4 },
                Keys.P or Keys.O or Keys.I or Keys.U or Keys.Y => new[] { 5 },
                Keys.Enter or Keys.L or Keys.K or Keys.J or Keys.H => new[] { 6 },
                Keys.Space or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
                Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.M or Keys.N or Keys.B => new[] { 7 },
                _ => Array.Empty<int>()
            };
        }

        private void ProcessPendingSpectrumKeyReleases()
        {
            foreach (var change in spectrumKeyInputBridge.CollectStateChanges(machine.GetKeyboardRowScanCount, inputBridgeTick))
            {
                LogInputDiagnostic("bridge-pending-change", change.Key, $"pressed={change.Pressed} {DescribeBridgeState(change.Key)}");
                ApplySpectrumKeyState(change.Key, change.Pressed);
            }
        }

        private void QueueSpectrumHostKeyEvent(Keys key, bool isDown)
        {
            if (frameClock.ElapsedTicks < Volatile.Read(ref suppressSpectrumHostInputUntilTicks))
            {
                LogInputDiagnostic("queue-suppressed", key, $"isDown={isDown}");
                return;
            }

            lock (inputStateLock)
            {
                pendingSpectrumHostKeyEvents.Enqueue(new SpectrumHostKeyEvent(key, isDown));
            }
        }

        private void SuppressSpectrumHostInputForMilliseconds(int milliseconds)
        {
            if (milliseconds <= 0)
                return;

            long durationTicks = milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000;
            Volatile.Write(ref suppressSpectrumHostInputUntilTicks, frameClock.ElapsedTicks + durationTicks);
        }

        private void ResetInputBridgeState()
        {
            lock (inputStateLock)
            {
                pressedSpectrumKeys.Clear();
                pendingSpectrumHostKeyEvents.Clear();
                spectrumKeyInputBridge.Reset();
            }
        }

        private void DrainPendingSpectrumHostKeyEvents()
        {
            while (true)
            {
                SpectrumHostKeyEvent? keyEvent = null;
                lock (inputStateLock)
                {
                    if (pendingSpectrumHostKeyEvents.Count == 0)
                        break;

                    keyEvent = pendingSpectrumHostKeyEvents.Dequeue();
                }

                UpdateSpectrumMappedKeyState(keyEvent.Value.Key, keyEvent.Value.IsDown);
            }
        }

        private bool UpdateSpectrumMappedKeyState(Keys key, bool isDown)
        {
            bool previousState = pressedSpectrumKeys.Contains(key);
            if (previousState == isDown)
            {
                LogInputDiagnostic("update-skip", key, $"isDown={isDown} previousState={previousState} {DescribeBridgeState(key)}");
                return false;
            }

            if (isDown)
                pressedSpectrumKeys.Add(key);
            else
                pressedSpectrumKeys.Remove(key);

            int[] rows = GetSpectrumKeyRows(key);
            LogInputDiagnostic("update-begin", key, $"isDown={isDown} previousState={previousState} {DescribeBridgeState(key)}");
            if (rows.Length > 1)
            {
                foreach (var change in isDown
                    ? spectrumKeyInputBridge.RegisterKeyDown(key, rows, machine.GetKeyboardRowScanCount, inputBridgeTick)
                    : spectrumKeyInputBridge.RegisterKeyUp(key, machine.GetKeyboardRowScanCount, inputBridgeTick))
                {
                    LogInputDiagnostic("bridge-change", change.Key, $"pressed={change.Pressed} source={(isDown ? "keydown" : "keyup")} {DescribeBridgeState(change.Key)}");
                    ApplySpectrumKeyState(change.Key, change.Pressed);
                }
            }
            else
            {
                LogInputDiagnostic("bridge-bypass", key, $"pressed={isDown} source={(isDown ? "keydown" : "keyup")}");
                ApplySpectrumKeyState(key, isDown);
            }

            LogInputDiagnostic("update-end", key, $"isDown={isDown} {DescribeBridgeState(key)}");

            return true;
        }

        private void ApplySpectrumKeyState(Keys key, bool pressed)
        {
            LogInputDiagnostic("apply", key, $"pressed={pressed}");
            switch (key)
            {
                case Keys.Left:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(3, 4, pressed);
                    break;
                case Keys.Down:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 4, pressed);
                    break;
                case Keys.Up:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 3, pressed);
                    break;
                case Keys.Right:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 2, pressed);
                    break;
                case Keys.Back:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 0, pressed);
                    break;

                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    machine.SetKey(0, 0, pressed); break;
                case Keys.Z:
                    machine.SetKey(0, 1, pressed); break;
                case Keys.X:
                    machine.SetKey(0, 2, pressed); break;
                case Keys.C:
                    machine.SetKey(0, 3, pressed); break;
                case Keys.V:
                    machine.SetKey(0, 4, pressed); break;

                case Keys.A:
                    machine.SetKey(1, 0, pressed); break;
                case Keys.S:
                    machine.SetKey(1, 1, pressed); break;
                case Keys.D:
                    machine.SetKey(1, 2, pressed); break;
                case Keys.F:
                    machine.SetKey(1, 3, pressed); break;
                case Keys.G:
                    machine.SetKey(1, 4, pressed); break;

                case Keys.Q:
                    machine.SetKey(2, 0, pressed); break;
                case Keys.W:
                    machine.SetKey(2, 1, pressed); break;
                case Keys.E:
                    machine.SetKey(2, 2, pressed); break;
                case Keys.R:
                    machine.SetKey(2, 3, pressed); break;
                case Keys.T:
                    machine.SetKey(2, 4, pressed); break;

                case Keys.D1:
                case Keys.NumPad1:
                    machine.SetKey(3, 0, pressed); break;
                case Keys.D2:
                case Keys.NumPad2:
                    machine.SetKey(3, 1, pressed); break;
                case Keys.D3:
                case Keys.NumPad3:
                    machine.SetKey(3, 2, pressed); break;
                case Keys.D4:
                case Keys.NumPad4:
                    machine.SetKey(3, 3, pressed); break;
                case Keys.D5:
                case Keys.NumPad5:
                    machine.SetKey(3, 4, pressed); break;

                case Keys.D0:
                case Keys.NumPad0:
                    machine.SetKey(4, 0, pressed); break;
                case Keys.D9:
                case Keys.NumPad9:
                    machine.SetKey(4, 1, pressed); break;
                case Keys.D8:
                case Keys.NumPad8:
                    machine.SetKey(4, 2, pressed); break;
                case Keys.D7:
                case Keys.NumPad7:
                    machine.SetKey(4, 3, pressed); break;
                case Keys.D6:
                case Keys.NumPad6:
                    machine.SetKey(4, 4, pressed); break;

                case Keys.P:
                    machine.SetKey(5, 0, pressed); break;
                case Keys.O:
                    machine.SetKey(5, 1, pressed); break;
                case Keys.I:
                    machine.SetKey(5, 2, pressed); break;
                case Keys.U:
                    machine.SetKey(5, 3, pressed); break;
                case Keys.Y:
                    machine.SetKey(5, 4, pressed); break;

                case Keys.Enter:
                    machine.SetKey(6, 0, pressed); break;
                case Keys.L:
                    machine.SetKey(6, 1, pressed); break;
                case Keys.K:
                    machine.SetKey(6, 2, pressed); break;
                case Keys.J:
                    machine.SetKey(6, 3, pressed); break;
                case Keys.H:
                    machine.SetKey(6, 4, pressed); break;

                case Keys.Space:
                    machine.SetKey(7, 0, pressed); break;
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                    machine.SetKey(7, 1, pressed); break;
                case Keys.M:
                    machine.SetKey(7, 2, pressed); break;
                case Keys.N:
                    machine.SetKey(7, 3, pressed); break;
                case Keys.B:
                    machine.SetKey(7, 4, pressed); break;
            }
        }
    }
}
