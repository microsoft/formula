using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.Formula.CommandLine;
using System.Text.RegularExpressions;

namespace Microsoft.Jupyter.Core
{
    public class KernelEngine : BaseEngine
    {
        private CommandInterface _ci;
        private Sink _sink;
        private Chooser _chooser;

        public KernelEngine(
            IShellServer shell,
            IShellRouter router,
            IOptions<KernelContext> context,
            ILogger<KernelEngine> logger,
            IServiceProvider serviceProvider
        ) : base(shell, router, context, logger, serviceProvider)
        {
            _sink = new Sink();
            _chooser = new Chooser();
            _ci = new CommandInterface(_sink, _chooser);

            RegisterDefaultEncoders();
        }

        public override async Task<ExecutionResult> ExecuteMundane(string input, IChannel channel)
        {
            var cell_output = input.Split("\n");
            for (int i = 0; i < cell_output.Length; ++i)
            {
                _ci.DoCommand(cell_output[i]);
            }
            
            channel.Display(_sink.GetStdOut());

            channel.Stderr(_sink.GetStdErr());
            
            _sink.Clear();

            if(_sink.PrintedError)
            {
                return ExecuteStatus.Error.ToExecutionResult();
            }
            else
            {
                return ExecuteStatus.Ok.ToExecutionResult();
            }
        }

        [MagicCommand("%set_choice",
            summary: "Allows input of user selection."
        )]
        public async Task<ExecutionResult> ExecuteInput(string input, IChannel channel)
        {
            var cell_output = input.Split("\n");
            Regex rx = new Regex(@"\b^[0-9]+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            for (int i = 0;i < cell_output.Length;++i)
            {
                if (cell_output[i].Length > 0)
                {
                    MatchCollection matches = rx.Matches(cell_output[i]);
                    if (matches.Count > 0)
                    {
                        _chooser.SetChoice(Int32.Parse(cell_output[i]));
                    }
                    else
                    {
                        _ci.DoCommand(cell_output[i]);
                    }
                }
            }
            channel.Display(_sink.GetStdOut());

            channel.Stderr(_sink.GetStdErr());
            
            _sink.Clear();

            _chooser.SetChoice(0);

            if (_sink.PrintedError)
            {
                return ExecuteStatus.Error.ToExecutionResult();
            }
            else
            {
                return ExecuteStatus.Ok.ToExecutionResult();
            }
        }
    }
}