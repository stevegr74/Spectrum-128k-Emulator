using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;
namespace Spectrum128kEmulator.Tap
{
    public static partial class TapLoader
    {
        private sealed partial class BasicBootstrapExecutor
        {
            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadUsrContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                return CreateMountedLoadUsrContinuationResolver(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    requireUsrReturnAddressBetweenSteps: true);
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadUsrContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                bool requireUsrReturnAddressBetweenSteps = true,
                bool requireFollowOnUsr = false)
            {
                if (!TryBuildMountedUsrContinuationSteps(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    out List<MountedUsrContinuationStep> continuationSteps))
                {
                    return null;
                }

                if (requireFollowOnUsr && continuationSteps.Count == 0)
                    return null;

                if (continuationSteps.Count == 0)
                {
                    if (!TryBuildInitialMountedUsrContinuationStep(
                            machine,
                            programStart,
                            programLength,
                            autoStartLine,
                            out MountedUsrContinuationStep? initialStep) ||
                        initialStep is null)
                    {
                        return null;
                    }

                    continuationSteps.Add((MountedUsrContinuationStep)initialStep);
                }

                return CreateMountedUsrContinuationResolver(
                    continuationSteps,
                    0,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    programStart,
                    programLength,
                    requireUsrReturnAddressBetweenSteps);
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadRomBasicContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (!TryBuildMountedUsrContinuationSteps(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    out List<MountedUsrContinuationStep> continuationSteps))
                {
                    return null;
                }

                MountedUsrContinuationStep firstStep;
                if (continuationSteps.Count == 0)
                {
                    if (!TryBuildInitialMountedUsrContinuationStep(
                            machine,
                            programStart,
                            programLength,
                            autoStartLine,
                            out MountedUsrContinuationStep? initialStep) ||
                        initialStep is null)
                    {
                        if (!TryBuildInitialRomBasicResumeStep(
                                machine,
                                programStart,
                                programLength,
                                autoStartLine,
                                out MountedUsrContinuationStep? romResumeStep) ||
                            romResumeStep is null)
                        {
                            return null;
                        }

                        firstStep = (MountedUsrContinuationStep)romResumeStep;
                    }
                    else
                    {
                        firstStep = (MountedUsrContinuationStep)initialStep;
                    }
                }
                else
                {
                    firstStep = continuationSteps[0];
                }

                return currentMachine =>
                {
                    UpdateMountedUsrContinuationRomResumeContext(currentMachine, firstStep);
                    currentMachine.SetPendingMountedLoadBasicResume(
                        firstStep.LineNumber,
                        (byte)Math.Clamp(firstStep.StatementIndex, 0, byte.MaxValue));
                    return ushort.MaxValue;
                };
            }

            private static bool TryBuildMountedUsrContinuationSteps(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out List<MountedUsrContinuationStep> continuationSteps)
            {
                continuationSteps = new List<MountedUsrContinuationStep>();
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawInitialUsr = false;
                List<List<string>> pendingPreamble = new();
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    for (int parsedStatementIndex = 0; parsedStatementIndex < parsedLines[lineIndex].Statements.Count; parsedStatementIndex++)
                    {
                        List<string> statement = parsedLines[lineIndex].Statements[parsedStatementIndex];
                        int usrClauseOrdinal = 0;
                        foreach (List<TokenSpan> clauseTokens in EnumerateUsrContinuationClauses(parsedLines[lineIndex].StatementTokenSpans[parsedStatementIndex]))
                        {
                            if (clauseTokens.Count == 0)
                                continue;

                            List<string> clause = clauseTokens.ConvertAll(token => token.Text);
                            if (!sawInitialUsr)
                            {
                                if (TryGetRandomizeUsrExpressionTokens(clause, out _))
                                {
                                    sawInitialUsr = true;
                                    continue;
                                }

                                if (IsSafeUsrContinuationPreambleStatement(clause))
                                    continue;

                                machine.Trace?.Invoke(
                                    $"[MountedLoad] Unsupported clause before initial USR: {string.Join(' ', clause)}");
                                return false;
                            }

                            if (TryGetRandomizeUsrExpressionInfo(clauseTokens, out List<string> expressionTokens, out int expressionByteOffset))
                            {
                                continuationSteps.Add(new MountedUsrContinuationStep(
                                    parsedLines[lineIndex].Number,
                                    parsedStatementIndex + 1,
                                    usrClauseOrdinal,
                                    parsedLines[lineIndex].StatementByteOffsets[parsedStatementIndex],
                                    clauseTokens[0].ByteOffset,
                                    expressionByteOffset,
                                    parsedLines[lineIndex].DataAddress,
                                    pendingPreamble.Select(step => step.ToArray()).ToArray(),
                                    expressionTokens.ToArray()));
                                usrClauseOrdinal++;
                                pendingPreamble.Clear();
                                continue;
                            }

                            if (IsSafeUsrContinuationPreambleStatement(clause))
                            {
                                pendingPreamble.Add(new List<string>(clause));
                                continue;
                            }

                            machine.Trace?.Invoke(
                                $"[MountedLoad] Unsupported clause after initial USR: {string.Join(' ', clause)}");
                            return false;
                        }
                    }
                }

                return true;
            }

            private static bool TryBuildInitialMountedUsrContinuationStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out MountedUsrContinuationStep? continuationStep)
            {
                continuationStep = null;
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                var pendingPreamble = new List<List<string>>();
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    for (int statementIndex = 0; statementIndex < parsedLines[lineIndex].StatementTokenSpans.Count; statementIndex++)
                    {
                        List<TokenSpan> statementTokens = parsedLines[lineIndex].StatementTokenSpans[statementIndex];
                        foreach (List<TokenSpan> clauseTokens in EnumerateUsrContinuationClauses(statementTokens))
                        {
                            List<string> clause = clauseTokens.ConvertAll(token => token.Text);
                            if (TryGetRandomizeUsrExpressionInfo(
                                clauseTokens,
                                out List<string> expressionTokens,
                                out int expressionByteOffset))
                            {
                                continuationStep = new MountedUsrContinuationStep(
                                    parsedLines[lineIndex].Number,
                                    statementIndex,
                                    0,
                                    parsedLines[lineIndex].StatementByteOffsets[statementIndex],
                                    clauseTokens[0].ByteOffset,
                                    expressionByteOffset,
                                    parsedLines[lineIndex].DataAddress,
                                    pendingPreamble.Select(step => step.ToArray()).ToArray(),
                                    expressionTokens.ToArray());
                                return true;
                            }

                            if (IsSafeUsrContinuationPreambleStatement(clause))
                            {
                                pendingPreamble.Add(new List<string>(clause));
                                continue;
                            }

                            return false;
                        }
                    }
                }

                return false;
            }

            private static bool TryBuildInitialRomBasicResumeStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out MountedUsrContinuationStep? continuationStep)
            {
                continuationStep = null;
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    if (parsedLines[lineIndex].StatementTokenSpans.Count == 0)
                        continue;

                    for (int statementIndex = 0; statementIndex < parsedLines[lineIndex].StatementTokenSpans.Count; statementIndex++)
                    {
                        List<TokenSpan> statementTokens = parsedLines[lineIndex].StatementTokenSpans[statementIndex];
                        if (statementTokens.Count == 0)
                            continue;

                        continuationStep = new MountedUsrContinuationStep(
                            parsedLines[lineIndex].Number,
                            statementIndex,
                            0,
                            parsedLines[lineIndex].StatementByteOffsets[statementIndex],
                            statementTokens[0].ByteOffset,
                            statementTokens[0].ByteOffset,
                            parsedLines[lineIndex].DataAddress,
                            Array.Empty<string[]>(),
                            Array.Empty<string>());
                        return true;
                    }
                }

                return false;
            }

            private static Func<Spectrum128Machine, ushort?> CreateMountedUsrBasicResumeResolver(
                MountedUsrContinuationStep step,
                Func<Spectrum128Machine, ushort?>? followOnResolver = null,
                Func<Spectrum128Machine, bool>? canResume = null)
            {
                return currentMachine =>
                {
                    if (canResume != null && !canResume(currentMachine))
                    {
                        currentMachine.ClearPendingMountedLoadUsrContinuation();
                        return null;
                    }

                    if (followOnResolver != null)
                    {
                        // A follow-on step after a ROM-driven BASIC resume should not fire
                        // from the intermediate ROM callback/service loop that the resumed
                        // USR routine may use internally. It should wait until the prior
                        // USR genuinely returns through STACK-BC.
                        currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                            followOnResolver);
                    }

                    currentMachine.SetPendingMountedLoadBasicResume(
                        step.LineNumber,
                        (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                    return ushort.MaxValue;
                };
            }

            private static Func<Spectrum128Machine, ushort?> CreateMountedUsrContinuationResolver(
                IReadOnlyList<MountedUsrContinuationStep> steps,
                int stepIndex,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                bool requireUsrReturnAddressBetweenSteps)
            {
                return currentMachine =>
                {
                    int currentStepIndex = stepIndex;
                    bool allowCachedLiteralUsrZeroFallback = false;
                    while ((uint)currentStepIndex < (uint)steps.Count)
                    {
                        MountedUsrContinuationStep step = steps[currentStepIndex];
                        try
                        {
                            bool hasConditionalPreamble = ContainsConditionalUsrContinuationPreamble(step.PreambleStatements);
                            if (ShouldForceRomBasicResumeForLiveConditionalMountedUsrStep(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step))
                            {
                                UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                if (currentStepIndex + 1 < steps.Count)
                                {
                                    currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                        requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                }
                                else
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                }

                                currentMachine.SetPendingMountedLoadBasicResume(
                                    step.LineNumber,
                                    (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                return ushort.MaxValue;
                            }

                            bool? romBasicResumeDecision = ShouldUseRomBasicResumeForMountedUsrStep(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step,
                                allowCachedLiteralUsrZeroFallback);

                            if (hasConditionalPreamble)
                            {
                                allowCachedLiteralUsrZeroFallback = false;

                                if (!romBasicResumeDecision.HasValue)
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                    return null;
                                }

                                if (romBasicResumeDecision.Value)
                                {
                                    UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                    if (currentStepIndex + 1 < steps.Count)
                                    {
                                        currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                            requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                    }
                                    else
                                    {
                                        currentMachine.ClearPendingMountedLoadUsrContinuation();
                                    }

                                    currentMachine.SetPendingMountedLoadBasicResume(
                                        step.LineNumber,
                                        (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                    return ushort.MaxValue;
                                }
                            }

                            MountedUsrContinuationPreambleResult preambleResult =
                                ExecuteMountedUsrContinuationPreamble(currentMachine, variables, step.PreambleStatements);
                            if (preambleResult == MountedUsrContinuationPreambleResult.SkipStep)
                            {
                                allowCachedLiteralUsrZeroFallback = true;
                                currentStepIndex++;
                                continue;
                            }

                            allowCachedLiteralUsrZeroFallback = false;

                            if (!romBasicResumeDecision.HasValue)
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                                return null;
                            }

                            if (romBasicResumeDecision.Value)
                            {
                                UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                if (currentStepIndex + 1 < steps.Count)
                                {
                                    currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                        requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                }
                                else
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                }

                                currentMachine.SetPendingMountedLoadBasicResume(
                                    step.LineNumber,
                                    (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                return ushort.MaxValue;
                            }

                            if (currentStepIndex + 1 < steps.Count)
                            {
                                currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                    CreateMountedUsrContinuationResolver(
                                        steps,
                                        currentStepIndex + 1,
                                        variables,
                                        programStart,
                                        programLength,
                                        requireUsrReturnAddressBetweenSteps),
                                    requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                            }
                            else
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                            }

                            bool preserveLiveInterpreterStateForDirectUsr =
                                ShouldPreserveLiveInterpreterStateForDirectMountedUsrStep(
                                    currentMachine,
                                    programStart,
                                    programLength,
                                    step) ||
                                ShouldPreserveLiveInterpreterStateForFollowOnMountedUsrSteps(
                                    steps,
                                    currentStepIndex);
                            ushort? entryPoint = EvaluateMountedUsrContinuationExpression(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step);

                            if (!entryPoint.HasValue)
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                                return null;
                            }

                            if (entryPoint.Value == 0)
                                return 0;

                            UpdateMountedUsrContinuationDirectUsrContext(
                                currentMachine,
                                step,
                                preserveLiveInterpreterStateForDirectUsr);
                            return entryPoint.Value;
                        }
                        catch
                        {
                            currentMachine.ClearPendingMountedLoadUsrContinuation();
                            return null;
                        }
                    }

                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                    return null;
                };
            }

            private static bool? ShouldUseRomBasicResumeForMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step,
                bool allowCachedLiteralUsrZeroFallback = false)
            {
                IReadOnlyList<string> expressionTokens = step.ExpressionTokens;
                bool hasLiveTokens = TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens);
                if (hasLiveTokens && refreshedTokens is { Length: > 0 })
                    expressionTokens = refreshedTokens;

                if (hasLiveTokens &&
                    ShouldPreferRomBasicResumeForLiveMountedUsrStep(machine, variables, step, expressionTokens))
                {
                    return true;
                }

                if (ContainsConditionalUsrContinuationPreamble(step.PreambleStatements) &&
                    !CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                {
                    return true;
                }

                if (expressionTokens.Count == 1 &&
                    string.Equals(expressionTokens[0], "0", StringComparison.Ordinal))
                {
                    if (!hasLiveTokens && !allowCachedLiteralUsrZeroFallback)
                        return null;

                    return false;
                }

                return ShouldPreferRomBasicResumeForLiveMountedUsrStep(machine, variables, step, expressionTokens);
            }

            private static bool ShouldForceRomBasicResumeForLiveConditionalMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                if (!ContainsConditionalUsrContinuationPreamble(step.PreambleStatements))
                    return false;

                if (CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                    return false;

                return TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out _);
            }

            private static bool ShouldPreferRomBasicResumeForLiveMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                MountedUsrContinuationStep step,
                IReadOnlyList<string> expressionTokens)
            {
                if (CanEvaluateLiveMountedUsrStepDirectly(machine, variables, step, expressionTokens))
                    return false;

                return !IsSimpleLiteralUsrExpression(expressionTokens);
            }

            private static bool CanEvaluateLiveMountedUsrStepDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                MountedUsrContinuationStep step,
                IReadOnlyList<string> expressionTokens)
            {
                if (step.PreambleStatements.Length != 0 &&
                    !CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                {
                    return false;
                }

                return CanEvaluateMountedUsrExpressionDirectly(machine, variables, expressionTokens);
            }

            private static bool ShouldPreserveLiveInterpreterStateForDirectMountedUsrStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                IReadOnlyList<string> expressionTokens = step.ExpressionTokens;
                if (TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens) &&
                    refreshedTokens is { Length: > 0 })
                {
                    expressionTokens = refreshedTokens;
                }

                return !IsSimpleLiteralUsrExpression(expressionTokens);
            }

            private static bool ShouldPreserveLiveInterpreterStateForFollowOnMountedUsrSteps(
                IReadOnlyList<MountedUsrContinuationStep> steps,
                int currentStepIndex)
            {
                for (int i = currentStepIndex + 1; i < steps.Count; i++)
                {
                    MountedUsrContinuationStep followOnStep = steps[i];
                    if (ContainsConditionalUsrContinuationPreamble(followOnStep.PreambleStatements) ||
                        followOnStep.PreambleStatements.Length != 0 ||
                        !IsSimpleLiteralUsrExpression(followOnStep.ExpressionTokens))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool ContainsConditionalUsrContinuationPreamble(IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length != 0 &&
                        NormalizeStatementKeyword(clause[0]) == "IF")
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool CanEvaluateMountedUsrPreambleDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length == 0)
                        continue;

                    string keyword = NormalizeStatementKeyword(clause[0]);
                    if (keyword == "IF")
                    {
                        if (!CanEvaluateMountedUsrContinuationIfConditionDirectly(machine, variables, clause))
                            return false;
                        continue;
                    }

                    if (keyword == "LET")
                    {
                        int equalsIndex = Array.IndexOf(clause, "=");
                        if (equalsIndex <= 1 || equalsIndex >= clause.Length - 1)
                            return false;

                        if (!CanEvaluateMountedUsrExpressionDirectly(
                            machine,
                            variables,
                            clause.Skip(equalsIndex + 1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (keyword == "CLEAR")
                    {
                        if (clause.Length > 1 &&
                            !CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (keyword == "POKE")
                    {
                        int commaIndex = Array.IndexOf(clause, ",");
                        if (commaIndex <= 1 || commaIndex >= clause.Length - 1)
                            return false;

                        if (!CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(1).Take(commaIndex - 1).ToArray()) ||
                            !CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(commaIndex + 1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!IsSafeUsrContinuationPreambleStatement(new List<string>(clause)))
                        return false;
                }

                return true;
            }

            private static bool CanEvaluateMountedUsrContinuationIfConditionDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> clause)
            {
                int equalsIndex = -1;
                for (int i = 1; i < clause.Count; i++)
                {
                    if (clause[i] == "=")
                    {
                        equalsIndex = i;
                        break;
                    }
                }

                if (equalsIndex <= 1 || equalsIndex >= clause.Count - 1)
                    return false;

                string[] left = clause.Skip(1).Take(equalsIndex - 1).ToArray();
                string[] right = clause.Skip(equalsIndex + 1).ToArray();
                return CanEvaluateMountedUsrExpressionDirectly(machine, variables, left) &&
                       CanEvaluateMountedUsrExpressionDirectly(machine, variables, right);
            }

            private static bool CanEvaluateMountedUsrExpressionDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> expressionTokens)
            {
                if (!IsSafeUsrContinuationExpression(expressionTokens))
                    return false;

                foreach (string token in expressionTokens)
                {
                    if (string.IsNullOrEmpty(token) ||
                        token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                    {
                        continue;
                    }

                    if (NormalizeStatementKeyword(token) == "PEEK")
                        continue;

                    if (int.TryParse(token, out _))
                        continue;

                    if (variables.ContainsKey(token))
                        continue;

                    if (TryReadMountedContinuationNumericVariable(machine, token, out _))
                        continue;

                    // Spectrum BASIC numeric variables read as zero until assigned.
                    // Treating an unresolved safe variable token as evaluable keeps
                    // direct mounted-continuation IF/THEN logic aligned with the ROM
                    // instead of needlessly forcing a ROM BASIC resume.
                    continue;
                }

                return true;
            }

            private static bool IsSimpleLiteralUsrExpression(IReadOnlyList<string> expressionTokens)
            {
                if (expressionTokens.Count != 1)
                    return false;

                string token = expressionTokens[0];
                if (string.IsNullOrEmpty(token))
                    return false;

                for (int i = 0; i < token.Length; i++)
                {
                    if (!char.IsAsciiDigit(token[i]))
                        return false;
                }

                return true;
            }

            private static bool IsSafeUsrContinuationExpression(IReadOnlyList<string> expressionTokens)
            {
                if (expressionTokens.Count == 0)
                    return false;

                foreach (string token in expressionTokens)
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                        continue;

                    string normalized = NormalizeStatementKeyword(token);
                    if (normalized == "PEEK")
                        continue;

                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    for (int i = 0; i < token.Length; i++)
                    {
                        if (!IsAsciiLetterOrDigit(token[i]))
                            return false;
                    }
                }

                return true;
            }

            private static ushort? EvaluateMountedUsrContinuationExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                bool hasRefreshedTokens = TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens);

                if (hasRefreshedTokens &&
                    TryEvaluateMountedUsrExpression(machine, variables, refreshedTokens!, out ushort? refreshedValue))
                {
                    return refreshedValue;
                }

                return TryEvaluateMountedUsrExpression(machine, variables, step.ExpressionTokens, out ushort? fallbackValue)
                    ? fallbackValue
                    : null;
            }

            private static bool TryEvaluateMountedUsrExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> expressionTokens,
                out ushort? value)
            {
                value = null;
                if (expressionTokens.Count == 0)
                    return false;

                try
                {
                    var parser = new ExpressionParser(
                        machine,
                        variables,
                        expressionTokens.ToList(),
                        0,
                        expressionTokens.Count - 1,
                        requireVariableResolution: true);
                    int parsedValue = parser.Parse();
                    if ((uint)parsedValue > ushort.MaxValue)
                        return false;

                    value = (ushort)parsedValue;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryRefreshMountedUsrExpressionTokens(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step,
                out string[]? expressionTokens)
            {
                expressionTokens = null;
                if (TryRefreshMountedUsrExpressionTokensFromLiveLine(machine, step, out expressionTokens))
                    return true;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int lineIndex = parsedLines.FindIndex(line => line.Number == step.LineNumber);
                if (lineIndex < 0)
                    return false;

                int statementIndex = step.StatementIndex - 1;
                if ((uint)statementIndex >= (uint)parsedLines[lineIndex].Statements.Count)
                    return false;

                int usrClauseOrdinal = 0;
                foreach (List<string> clause in EnumerateUsrContinuationClauses(parsedLines[lineIndex].Statements[statementIndex]))
                {
                    if (!TryGetRandomizeUsrExpressionTokens(clause, out List<string> liveExpressionTokens))
                        continue;

                    if (usrClauseOrdinal == step.UsrClauseOrdinal)
                    {
                        expressionTokens = liveExpressionTokens.ToArray();
                        return expressionTokens.Length > 0;
                    }

                    usrClauseOrdinal++;
                }

                return false;
            }

            private static bool TryRefreshMountedUsrExpressionTokensFromLiveLine(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                out string[]? expressionTokens)
            {
                expressionTokens = null;

                if (!TryParseLiveBasicLine(machine, step.LineDataAddress, step.LineNumber, out BasicLine? parsedLine) ||
                    parsedLine is null)
                    return false;
                BasicLine liveLine = (BasicLine)parsedLine;

                int statementIndex = step.StatementIndex - 1;
                if ((uint)statementIndex >= (uint)liveLine.Statements.Count)
                    return false;

                int usrClauseOrdinal = 0;
                foreach (List<string> clause in EnumerateUsrContinuationClauses(liveLine.Statements[statementIndex]))
                {
                    if (!TryGetRandomizeUsrExpressionTokens(clause, out List<string> liveExpressionTokens))
                        continue;

                    if (usrClauseOrdinal == step.UsrClauseOrdinal)
                    {
                        expressionTokens = liveExpressionTokens.ToArray();
                        return expressionTokens.Length > 0;
                    }

                    usrClauseOrdinal++;
                }

                return false;
            }

            private static bool TryParseLiveBasicLine(
                Spectrum128Machine machine,
                ushort lineDataAddress,
                ushort expectedLineNumber,
                out BasicLine? line)
            {
                line = null;
                if (lineDataAddress < 4)
                    return false;

                ushort lineHeaderAddress = (ushort)(lineDataAddress - 4);
                ushort liveLineNumber = (ushort)(
                    (machine.PeekMemory(lineHeaderAddress) << 8) |
                    machine.PeekMemory((ushort)(lineHeaderAddress + 1)));
                if (liveLineNumber != expectedLineNumber)
                    return false;

                ushort lineLength = ReadWord(machine, (ushort)(lineHeaderAddress + 2));
                if (lineLength == 0 || lineLength > 1024)
                    return false;

                byte[] lineBytes = new byte[lineLength];
                for (int i = 0; i < lineLength; i++)
                    lineBytes[i] = machine.PeekMemory((ushort)(lineDataAddress + i));

                (List<List<string>> statements, List<int> statementByteOffsets, List<List<TokenSpan>> statementTokenSpans) =
                    SplitStatements(Tokenize(lineBytes));
                line = new BasicLine(liveLineNumber, statements, statementByteOffsets, statementTokenSpans, lineDataAddress);
                return true;
            }

            private static void UpdateMountedUsrContinuationRomResumeContext(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                bool suppressCursorOverride = false)
            {
                WriteWord(machine, NewPpcAddress, step.LineNumber);
                machine.PokeMemory(NspPcAddress, (byte)Math.Clamp(step.StatementIndex, 0, 255));
                WriteWord(machine, PpcAddress, step.LineNumber);
                machine.PokeMemory(SubPpcAddress, (byte)Math.Clamp(step.StatementIndex, 0, 255));

                if (!suppressCursorOverride)
                {
                    ushort chAdd = (ushort)(step.LineDataAddress + step.ClauseStartByteOffset);
                    machine.SetPendingMountedLoadResumeCursorOverride(chAdd: chAdd);
                }
            }

            private static void UpdateMountedUsrContinuationDirectUsrContext(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                bool preserveLiveInterpreterState)
            {
                UpdateMountedUsrContinuationRomResumeContext(
                    machine,
                    step,
                    suppressCursorOverride: preserveLiveInterpreterState);
                machine.SetPendingMountedLoadDirectUsrContextPolicy(preserveLiveInterpreterState);
                if (preserveLiveInterpreterState)
                    return;

                ushort eLine = ReadWord(machine, EditLineAddress);
                ushort chAdd = (ushort)(step.LineDataAddress + step.ExpressionByteOffset + 1);
                machine.SetPendingMountedLoadResumeCursorOverride(kCur: eLine, chAdd: chAdd, xPtr: chAdd);
            }

            private enum MountedUsrContinuationPreambleResult
            {
                ContinueStep,
                SkipStep
            }

            private static MountedUsrContinuationPreambleResult ExecuteMountedUsrContinuationPreamble(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length == 0)
                        continue;

                    string keyword = NormalizeStatementKeyword(clause[0]);
                    switch (keyword)
                    {
                        case "LET":
                        {
                            int equalsIndex = Array.IndexOf(clause, "=");
                            if (equalsIndex <= 1 || equalsIndex >= clause.Length - 1)
                                throw new InvalidOperationException("Malformed LET statement in mounted USR continuation.");

                            string variableName = clause[1];
                            var parser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                equalsIndex + 1,
                                clause.Length - 1,
                                requireVariableResolution: true);
                            int value = parser.Parse();
                            variables[variableName] = value;
                            TryUpdateMountedUsrContinuationBasicVariable(machine, variableName, value);
                            break;
                        }

                        case "IF":
                        {
                            if (!EvaluateMountedUsrContinuationIfCondition(machine, variables, clause))
                                return MountedUsrContinuationPreambleResult.SkipStep;
                            break;
                        }

                        case "REM":
                        case "?":
                        case "BORDER":
                        case "PAPER":
                        case "INK":
                        case "CLS":
                            ApplyMountedContinuationDisplaySideEffects(machine);
                            break;

                        case "CLEAR":
                        {
                            if (clause.Length > 1)
                            {
                                var parser = new ExpressionParser(
                                    machine,
                                    variables,
                                    new List<string>(clause),
                                    1,
                                    clause.Length - 1,
                                    requireVariableResolution: true);
                                ApplyMountedContinuationClear(machine, parser.Parse());
                            }
                            else
                            {
                                ApplyMountedContinuationDisplaySideEffects(machine);
                            }
                            break;
                        }

                        case "LOAD":
                        {
                            BootstrapTapeLoadResult loadResult = machine.TryConsumeBootstrapTapeLoad();
                            if (!loadResult.Success || loadResult.LoadedBasicProgram)
                                throw new InvalidOperationException("Mounted CODE LOAD could not consume a standard tape data block.");
                            break;
                        }

                        case "POKE":
                        {
                            int commaIndex = Array.IndexOf(clause, ",");
                            if (commaIndex <= 1 || commaIndex >= clause.Length - 1)
                                throw new InvalidOperationException("Malformed POKE statement in mounted USR continuation.");

                            var addressParser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                1,
                                commaIndex - 1,
                                requireVariableResolution: true);
                            var valueParser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                commaIndex + 1,
                                clause.Length - 1,
                                requireVariableResolution: true);
                            int address = addressParser.Parse();
                            int value = valueParser.Parse();
                            machine.PokeMemory((ushort)address, (byte)value);
                            if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                machine.ForceApply7ffdValue((byte)value);
                            break;
                        }

                        default:
                            throw new InvalidOperationException($"Unsupported mounted USR continuation preamble '{keyword}'.");
                    }
                }

                return MountedUsrContinuationPreambleResult.ContinueStep;
            }

            private static bool EvaluateMountedUsrContinuationIfCondition(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> clause)
            {
                int equalsIndex = -1;
                for (int i = 1; i < clause.Count; i++)
                {
                    if (clause[i] == "=")
                    {
                        equalsIndex = i;
                        break;
                    }
                }

                if (equalsIndex <= 1 || equalsIndex >= clause.Count - 1)
                    throw new InvalidOperationException("Malformed IF statement in mounted USR continuation.");

                var leftParser = new ExpressionParser(
                    machine,
                    variables,
                    new List<string>(clause),
                    1,
                    equalsIndex - 1,
                    requireVariableResolution: true);
                var rightParser = new ExpressionParser(
                    machine,
                    variables,
                    new List<string>(clause),
                    equalsIndex + 1,
                    clause.Count - 1,
                    requireVariableResolution: true);
                return leftParser.Parse() == rightParser.Parse();
            }

            private static void ApplyMountedContinuationDisplaySideEffects(Spectrum128Machine machine)
            {
                machine.PokeMemory(FlagsSystemVariableAddress, (byte)(machine.PeekMemory(FlagsSystemVariableAddress) | 0x20));
                machine.PokeMemory(TvFlagSystemVariableAddress, (byte)(machine.PeekMemory(TvFlagSystemVariableAddress) | 0x20));
            }

            private static void ApplyMountedContinuationClear(Spectrum128Machine machine, int clearAddress)
            {
                ushort top = ClampClearTopAddress(clearAddress);
                WriteWord(machine, RamTopAddress, top);
                WriteWord(machine, PhysicalRamTopAddress, top);
                ushort workspace = GetWorkspaceAddressForClear(top);
                WriteWord(machine, VarsAddress, workspace);
                WriteWord(machine, EditLineAddress, workspace);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
                WriteWord(machine, KCurAddress, workspace);
                WriteWord(machine, ChAddAddress, workspace);
                if (ShouldRelocateMachineStackAfterClear(machine, top))
                    machine.Cpu.Regs.SP = GetMachineStackPointerForClear(top);
            }

            private static void TryUpdateMountedUsrContinuationBasicVariable(
                Spectrum128Machine machine,
                string variableName,
                int value)
            {
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return;

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return;

                if (value < short.MinValue || value > ushort.MaxValue)
                    return;

                ushort varsAddress = ReadWord(machine, VarsAddress);
                ushort eLineAddress = ReadWord(machine, EditLineAddress);
                ushort current = varsAddress;
                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));

                while (current < eLineAddress)
                {
                    byte header = machine.PeekMemory(current);
                    if (header == 0x80)
                        return;

                    int entryLength = GetBasicVariableEntryLength(machine, current, eLineAddress);
                    if (entryLength <= 0 || current + entryLength > eLineAddress)
                        return;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        WriteSpectrumSmallInteger(machine, (ushort)(current + 1), value);
                        return;
                    }

                    current = (ushort)(current + entryLength);
                }
            }

            private static int GetBasicVariableEntryLength(
                Spectrum128Machine machine,
                ushort address,
                ushort eLineAddress)
            {
                if (address >= eLineAddress)
                    return 0;

                byte header = machine.PeekMemory(address);
                if (header == 0x80)
                    return 1;

                switch (header & 0xE0)
                {
                    case 0x40:
                    {
                        if (address + 2 >= eLineAddress)
                            return 0;
                        ushort length = ReadWord(machine, (ushort)(address + 1));
                        return 3 + length;
                    }

                    case 0x60:
                        return 6;

                    case 0x80:
                    case 0xC0:
                    {
                        if (address + 2 >= eLineAddress)
                            return 0;
                        ushort length = ReadWord(machine, (ushort)(address + 1));
                        return 3 + length;
                    }

                    case 0xA0:
                    {
                        ushort current = (ushort)(address + 1);
                        while (current < eLineAddress)
                        {
                            byte nameByte = machine.PeekMemory(current);
                            current++;
                            if ((nameByte & 0x80) != 0)
                            {
                                if (current + 5 > eLineAddress)
                                    return 0;
                                return current - address + 5;
                            }
                        }

                        return 0;
                    }

                    case 0xE0:
                        return 19;

                    default:
                        return 0;
                }
            }

            private static void WriteSpectrumSmallInteger(
                Spectrum128Machine machine,
                ushort address,
                int value)
            {
                short signedValue = (short)value;
                ushort magnitude = (ushort)signedValue;
                byte sign = signedValue < 0 ? (byte)0xFF : (byte)0x00;

                machine.PokeMemory(address, 0x00);
                machine.PokeMemory((ushort)(address + 1), sign);
                machine.PokeMemory((ushort)(address + 2), (byte)(magnitude & 0xFF));
                machine.PokeMemory((ushort)(address + 3), (byte)(magnitude >> 8));
                machine.PokeMemory((ushort)(address + 4), 0x00);
            }

            private static bool TryReadMountedContinuationNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                if (TryReadLiveBasicNumericVariable(machine, variableName, out value))
                    return true;

                return TryReadPreservedMountedLoadBasicNumericVariable(machine, variableName, out value);
            }

            private static bool TryReadLiveBasicNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return false;

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return false;

                ushort varsAddress = ReadWord(machine, VarsAddress);
                ushort eLineAddress = ReadWord(machine, EditLineAddress);
                ushort current = varsAddress;
                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));

                while (current < eLineAddress)
                {
                    byte header = machine.PeekMemory(current);
                    if (header == 0x80)
                        return false;

                    int entryLength = GetBasicVariableEntryLength(machine, current, eLineAddress);
                    if (entryLength <= 0 || current + entryLength > eLineAddress)
                        return false;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        return TryReadSpectrumSmallInteger(machine, (ushort)(current + 1), out value);
                    }

                    current = (ushort)(current + entryLength);
                }

                return false;
            }

            private static bool TryReadPreservedMountedLoadBasicNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return false;

                if (!machine.TryGetPendingMountedLoadBasicVariableSnapshot(out _, out byte[] data) ||
                    data.Length == 0)
                {
                    return false;
                }

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return false;

                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));
                int index = 0;
                while (index < data.Length)
                {
                    byte header = data[index];
                    if (header == 0x80)
                        return false;

                    int entryLength = GetBasicVariableEntryLength(data, index);
                    if (entryLength <= 0 || index + entryLength > data.Length)
                        return false;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        return TryReadSpectrumSmallInteger(data, index + 1, out value);
                    }

                    index += entryLength;
                }

                return false;
            }

            private static bool TryReadSpectrumSmallInteger(
                Spectrum128Machine machine,
                ushort address,
                out int value)
            {
                value = 0;
                byte exponent = machine.PeekMemory(address);
                byte sign = machine.PeekMemory((ushort)(address + 1));
                byte low = machine.PeekMemory((ushort)(address + 2));
                byte high = machine.PeekMemory((ushort)(address + 3));
                byte trailing = machine.PeekMemory((ushort)(address + 4));
                return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
            }

            private static bool TryReadSpectrumSmallInteger(
                byte[] data,
                int index,
                out int value)
            {
                value = 0;
                if (index < 0 || index + 4 >= data.Length)
                    return false;

                byte exponent = data[index];
                byte sign = data[index + 1];
                byte low = data[index + 2];
                byte high = data[index + 3];
                byte trailing = data[index + 4];
                return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
            }

            private static bool TryDecodeSpectrumNumericValue(
                byte exponent,
                byte signOrMantissa,
                byte third,
                byte fourth,
                byte fifth,
                out int value)
            {
                value = 0;

                if (exponent == 0x00)
                {
                    if (fifth != 0x00)
                        return false;

                    short signedValue = unchecked((short)(third | (fourth << 8)));
                    value = signOrMantissa == 0xFF ? -Math.Abs(signedValue) : signedValue;
                    return true;
                }

                uint mantissa =
                    (uint)(((signOrMantissa & 0x7F) | 0x80) << 24) |
                    (uint)(third << 16) |
                    (uint)(fourth << 8) |
                    fifth;
                int sign = (signOrMantissa & 0x80) != 0 ? -1 : 1;
                double numericValue = sign * mantissa * Math.Pow(2.0, exponent - 160);
                double roundedValue = Math.Round(numericValue);
                if (roundedValue < int.MinValue || roundedValue > int.MaxValue)
                    return false;

                if (Math.Abs(numericValue - roundedValue) > 1e-6)
                    return false;

                value = (int)roundedValue;
                return true;
            }

            private static int GetBasicVariableEntryLength(byte[] data, int index)
            {
                if ((uint)index >= (uint)data.Length)
                    return 0;

                byte header = data[index];
                if (header == 0x80)
                    return 1;

                switch (header & 0xE0)
                {
                    case 0x40:
                    case 0x80:
                    case 0xC0:
                    {
                        if (index + 2 >= data.Length)
                            return 0;

                        ushort length = (ushort)(data[index + 1] | (data[index + 2] << 8));
                        return 3 + length;
                    }

                    case 0x60:
                        return 6;

                    case 0xA0:
                    {
                        int current = index + 1;
                        while (current < data.Length)
                        {
                            byte nameByte = data[current++];
                            if ((nameByte & 0x80) != 0)
                            {
                                if (current + 5 > data.Length)
                                    return 0;

                                return current - index + 5;
                            }
                        }

                        return 0;
                    }

                    case 0xE0:
                        return 19;

                    default:
                        return 0;
                }
            }

            private sealed record MountedUsrContinuationStep(
                ushort LineNumber,
                int StatementIndex,
                int UsrClauseOrdinal,
                int StatementStartByteOffset,
                int ClauseStartByteOffset,
                int ExpressionByteOffset,
                ushort LineDataAddress,
                string[][] PreambleStatements,
                string[] ExpressionTokens);

        }
    }
}
