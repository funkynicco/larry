using Larry.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Larry.Scripts
{
    public class ScriptRunner : IDisposable
    {
        private readonly BuildClient _buildClient = new BuildClient(true);
        private readonly Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>();
        private readonly ScriptRunnerDispatcher _dispatcher;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public ScriptRunner()
        {
            _dispatcher = new ScriptRunnerDispatcher(_buildClient);

            foreach (var method in _dispatcher.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var attribute in method.GetCustomAttributes<ScriptCommandAttribute>())
                {
                    var command = attribute.Command.ToLowerInvariant().Trim();

                    if (_methods.ContainsKey(command))
                        throw new InvalidProgramException($"ScriptCommand handler for '{attribute.Command}' is already registered.");

                    _methods.Add(command, method);
                }
            }
        }

        public void Dispose()
        {
            _buildClient.Dispose();
            _cancellationTokenSource.Dispose();
        }

        private void InternalRun(string filename)
        {
            var script = Script.LoadFile(filename);
            foreach (var command in script.Commands)
            {
                var cmd = command.Command.ToLowerInvariant().Trim();
                if (!_methods.TryGetValue(cmd, out MethodInfo method))
                    throw new ScriptCommandNotFoundException(filename, command.LineNumber, command.Command);

                ExecuteScriptCommand(command, method);
            }

            while (true)
            {
                _buildClient.Process(_cancellationTokenSource.Token);
                Thread.Sleep(100);
            }
        }

        private MethodInfo GetParseMethod(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "Parse")
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(string))
                    return method;
            }

            return null;
        }

        private void ExecuteScriptCommand(IScriptCommand command, MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (parameters.Length == 1 &&
                parameters[0].ParameterType == typeof(IScriptCommand))
            {
                try
                {
                    method.Invoke(_dispatcher, new object[] { command });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException;
                }

                return;
            }

            if (command.Arguments.Length != parameters.Length)
                throw new ArgumentException($"Argument count mismatch for script function '{command.Command}'.");

            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; ++i)
            {
                MethodInfo parseMethod;

                if (parameters[i].ParameterType == typeof(string))
                {
                    args[i] = command.Arguments[i];
                }
                else if ((parseMethod = GetParseMethod(parameters[i].ParameterType)) != null)
                {
                    args[i] = parseMethod.Invoke(null, new object[] { command.Arguments[i] });
                }
                else
                    throw new Exception($"Invalid script command function argument type: {parameters[i].ParameterType.FullName}");
            }

            try
            {
                method.Invoke(_dispatcher, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        public void Run(string filename)
        {
            try
            {
                InternalRun(filename);
            }
            catch (ScriptCommandNotFoundException ex)
            {
                Logger.Log(LogType.Error, ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Log(LogType.Error, ex.Message);
            }
        }
    }

    public class ScriptCommandNotFoundException : Exception
    {
        public ScriptCommandNotFoundException(string filename, int lineNumber, string command) :
            base($"The script command '{command}' in {filename}:{lineNumber} was not recognized.")
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ScriptCommandAttribute : Attribute
    {
        public string Command { get; private set; }

        public ScriptCommandAttribute(string command)
        {
            Command = command;
        }
    }
}
