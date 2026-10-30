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
            keyboardMessageFilter = new SpectrumKeyboardMessageFilter(
                () => ContainsFocus && !displayContextMenu.Visible,
                QueueSpectrumHostKeyEvent);
            Application.AddMessageFilter(keyboardMessageFilter);
        }

        private void MainForm_Deactivate(object? sender, EventArgs e)
        {
            LogInputDiagnostic("deactivate", null, "clearing input state");
            QueueSpectrumHostInputReset();
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

            if (IsPlainShortcut(e, Keys.F9))
            {
                LoadSnaSnapshotFromDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F10))
            {
                LoadSnapshotOrRecordingFromDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsTapeReplacementShortcut(e))
            {
                ReplaceMountedTapeFromDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F11))
            {
                MountTapFromDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (IsPlainShortcut(e, Keys.F12))
            {
                DumpMachineDebugState();
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

        private static bool IsTapeReplacementShortcut(KeyEventArgs e) =>
            e.KeyCode == Keys.F11 && e.Shift && !e.Control && !e.Alt;

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
            if (IsSpectrumMappedKey(key))
                QueueSpectrumHostKeyEvent(key, pressed);
        }

        private bool IsSpectrumMappedKey(Keys key)
        {
            return SpectrumKeyboardMapping.IsMappedKey(key);
        }

        private void ProcessPendingSpectrumKeyReleases()
        {
            keyboardInputController.Advance(
                machine.GetKeyboardRowScanCount,
                inputEmulatedTStates,
                machine.SetKey);
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

        private void QueueSpectrumHostInputReset()
        {
            lock (inputStateLock)
            {
                pendingSpectrumHostKeyEvents.Clear();
                pendingSpectrumHostKeyEvents.Enqueue(new SpectrumHostKeyEvent(Keys.None, false, Reset: true));
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
                pendingSpectrumHostKeyEvents.Clear();
                keyboardInputController.Reset();
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

                if (keyEvent.Value.Reset)
                {
                    keyboardInputController.Reset();
                    machine.ClearKeyboard();
                    continue;
                }

                UpdateSpectrumMappedKeyState(keyEvent.Value.Key, keyEvent.Value.IsDown);
            }
        }

        private bool UpdateSpectrumMappedKeyState(Keys key, bool isDown)
        {
            bool changed = keyboardInputController.HandleHostKeyEvent(
                key,
                isDown,
                machine.GetKeyboardRowScanCount,
                inputEmulatedTStates,
                (ulong)machine.FrameTStates,
                machine.SetKey);
            LogInputDiagnostic("update", key, $"isDown={isDown} changed={changed} {DescribeBridgeState(key)}");
            return changed;
        }
    }
}
