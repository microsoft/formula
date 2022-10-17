using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.Formula.CommandLine;

namespace Microsoft.Jupyter.Core
{
    public class KernelEngine : BaseEngine
    {
        private CommandInterface _ci;
        private Sink _sink;
        private Chooser _chooser;

        private bool init = false;

        private IShellServer _server;

        public KernelEngine(
            IShellServer shell,
            IShellRouter router,
            IOptions<KernelContext> context,
            ILogger<KernelEngine> logger,
            IServiceProvider serviceProvider
        ) : base(shell, router, context, logger, serviceProvider)
        {
            _sink = new Sink();
            _chooser = new Chooser(shell);
            _ci = new CommandInterface(_sink, _chooser);
            
            _server = shell;

            RegisterDefaultEncoders();
        }

        public override async Task<ExecutionResult> ExecuteMundane(string input, IChannel channel)
        {
            _chooser.setCellMessage(channel.CellMessage);

            _sink.setChannel(channel);

            if(!init)
            {
                _ci.DoCommand("wait on");
                _sink.Clear();
                init = true;
            }

            var regx = new Regex(@"^(load\s|l\s)", RegexOptions.Singleline);
            var match = regx.Match(input);
            if(match.Success)
            {
                var sp = input.Split(" ", 3);
                Environment.CurrentDirectory = sp[2]; 
                input = sp[0] + " " + sp[1];   
            }

            _ci.DoCommand(input);

            _sink.ShowOutput();
            _sink.Clear();

            var res = new ExecutionResult();

            if(_sink.PrintedError)
            {
                res.Status = ExecuteStatus.Error;
            }
            else
            {
                res.Status = ExecuteStatus.Ok;
            }
            return res;
        }   
    }
}