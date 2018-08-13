using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry.Scripts
{
    public interface IScriptCommand
    {
        string Command { get; }

        string[] Arguments { get; }

        int LineNumber { get; }
    }

    public class Script
    {
        class ScriptCommand : IScriptCommand
        {
            public int LineNumber { get; private set; }

            public string Command { get; private set; }

            public string[] Arguments { get; private set; }

            public ScriptCommand(int lineNumber, string command, string[] arguments)
            {
                LineNumber = lineNumber;
                Command = command;
                Arguments = arguments;
            }

            public static ScriptCommand FromLine(int lineNumber, string line)
            {
                var args = new List<string>();
                var sb = new StringBuilder();
                var is_string = false;

                for (int i = 0; i < line.Length; ++i)
                {
                    if (char.IsWhiteSpace(line[i]) &&
                        !is_string)
                    {
                        if (sb.Length > 0)
                        {
                            args.Add(sb.ToString());
                            sb.Clear();
                        }
                    }
                    else if (line[i] == '"' &&
                        (i == 0 || line[i - 1] != '\\'))
                    {
                        if (is_string)
                        {
                            args.Add(sb.ToString());
                            sb.Clear();
                        }
                        else if (sb.Length > 0)
                        {
                            args.Add(sb.ToString());
                            sb.Clear();
                        }

                        is_string = !is_string;
                    }
                    else if (is_string &&
                        line[i] == '\\' &&
                        i + 1 < line.Length &&
                        line[i + 1] == '\\')
                    {
                        sb.Append('\\');
                        ++i;
                    }
                    else if (is_string &&
                        line[i] == '\\' &&
                        i + 1 < line.Length &&
                        line[i + 1] == '"')
                    {
                        sb.Append('"');
                        ++i;
                    }
                    else if (is_string &&
                        line[i] == '\\' &&
                        i + 1 < line.Length &&
                        line[i + 1] == 't')
                    {
                        sb.Append('\t');
                        ++i;
                    }
                    else if (is_string &&
                        line[i] == '\\' &&
                        i + 1 < line.Length &&
                        line[i + 1] == 'r')
                    {
                        sb.Append('\r');
                        ++i;
                    }
                    else if (is_string &&
                        line[i] == '\\' &&
                        i + 1 < line.Length &&
                        line[i + 1] == 'n')
                    {
                        sb.Append('\n');
                        ++i;
                    }
                    else
                        sb.Append(line[i]);
                }

                if (sb.Length > 0)
                    args.Add(sb.ToString());

                if (args.Count == 0)
                    throw new ArgumentException("Could not find command in line: " + line);

                return new ScriptCommand(
                    lineNumber,
                    args[0],
                    args.Skip(1).ToArray());
            }
        }

        private readonly List<IScriptCommand> _commands = new List<IScriptCommand>();
        public IEnumerable<IScriptCommand> Commands => _commands;

        private Script()
        {
        }

        private static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length;)
            {
                if (text[i] == ';')
                {
                    while (i < text.Length &&
                        text[i] != '\n')
                        ++i;

                    if (i < text.Length &&
                        text[i] == '\n' &&
                        i - 1 > 0 &&
                        text[i - 1] == '\r')
                        sb.Append('\r');
                }
                else
                    sb.Append(text[i++]);
            }

            return sb.ToString();
        }

        public static Script Load(string content)
        {
            var script = new Script();

            var lines = StripComments(content).Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; ++i)
            {
                lines[i] = lines[i].Trim();
                if (lines[i].Length == 0)
                    continue;

                script._commands.Add(ScriptCommand.FromLine(i + 1, lines[i]));
            }
            
            return script;
        }

        public static Script LoadFile(string filename)
            => Load(System.IO.File.ReadAllText(filename));
    }
}
