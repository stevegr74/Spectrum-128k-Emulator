using System;
using System.Collections.Generic;
namespace Spectrum128kEmulator.Tap
{
    public static partial class TapLoader
    {
        private sealed class ExpressionParser
        {
            private readonly Spectrum128Machine machine;
            private readonly Dictionary<string, int> variables;
            private readonly List<string> tokens;
            private readonly bool requireVariableResolution;
            private readonly int endIndex;
            private int index;

            public ExpressionParser(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                List<string> tokens,
                int startIndex,
                int endIndex,
                bool requireVariableResolution = false)
            {
                this.machine = machine;
                this.variables = variables;
                this.tokens = tokens;
                this.requireVariableResolution = requireVariableResolution;
                index = startIndex;
                this.endIndex = endIndex;
            }

            public int Parse()
            {
                int value = ParseAdditive();
                if (index <= endIndex)
                    throw new InvalidOperationException($"Unexpected token '{tokens[index]}' in BASIC tape-bootstrap expression.");
                return value;
            }

            private int ParseAdditive()
            {
                int value = ParseMultiplicative();
                while (index <= endIndex)
                {
                    string token = tokens[index];
                    if (token == "+")
                    {
                        index++;
                        value += ParseMultiplicative();
                    }
                    else if (token == "-")
                    {
                        index++;
                        value -= ParseMultiplicative();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private int ParseMultiplicative()
            {
                int value = ParseUnary();
                while (index <= endIndex)
                {
                    string token = tokens[index];
                    if (token == "*")
                    {
                        index++;
                        value *= ParseUnary();
                    }
                    else if (token == "/")
                    {
                        index++;
                        int divisor = ParseUnary();
                        value = (int)Math.Floor((double)value / divisor);
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private int ParseUnary()
            {
                if (index <= endIndex && tokens[index] == "-")
                {
                    index++;
                    return -ParseUnary();
                }

                return ParsePrimary();
            }

            private int ParsePrimary()
            {
                if (index > endIndex)
                    throw new InvalidOperationException("Unexpected end of BASIC expression during tape bootstrap.");

                string token = tokens[index++];
                if (token == "(")
                {
                    int value = ParseAdditive();
                    if (index > endIndex || tokens[index] != ")")
                        throw new InvalidOperationException("Missing closing parenthesis in BASIC tape-bootstrap expression.");
                    index++;
                    return value;
                }

                if (token == "PEEK")
                {
                    int address = ParsePrimary();
                    return machine.PeekMemory((ushort)address);
                }

                if (token == "INT")
                    return ParsePrimary();

                if (int.TryParse(token, out int number))
                    return number;

                if (variables.TryGetValue(token, out int variableValue))
                    return variableValue;

                if (TryReadMountedContinuationNumericVariable(machine, token, out int liveVariableValue))
                    return liveVariableValue;

                // Spectrum BASIC treats an unread numeric variable as zero until it
                // is assigned. Mounted continuation evaluation should follow that
                // model instead of forcing a ROM BASIC resume just because a safe
                // direct IF/THEN or USR expression references an unset variable.
                if (requireVariableResolution)
                    return 0;

                return 0;
            }
        }

    }
}
