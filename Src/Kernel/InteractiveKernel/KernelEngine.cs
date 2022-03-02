using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Formula.CommandLine;
using Microsoft.Jupyter.Core.Protocol;

namespace Microsoft.Jupyter.Core
{
    public class KernelEngine : BaseEngine
    {
        private CommandInterface _ci;
        private Sink _sink;
        private Chooser _chooser;

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
            var cell_output = input.Split("\n");

            if(input.Contains("load") && !input.Contains("unload"))
            {
                _ci.DoCommand("wait on");
                string path = input.Replace("load ","");
                string data = "";
                using (FileStream fs = File.OpenRead(path))  
                {  
                    byte[] b = new byte[1024];  
                    UTF8Encoding temp = new UTF8Encoding(true);  
                    while (fs.Read(b,0,b.Length) > 0)  
                    {  
                        data += temp.GetString(b);  
                    }  
                } 
            }

            for (int i = 0; i < cell_output.Length; ++i)
            {
                _ci.DoCommand(cell_output[i]);
            }

            _sink.ShowOutput();
            _sink.Clear();

            var res = new ExecutionResult();
            res.Status = ExecuteStatus.Ok;
            return res;
        }   
    }
}