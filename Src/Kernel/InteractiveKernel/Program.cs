using System.Collections.Generic;
using static Microsoft.Jupyter.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using McMaster.Extensions.CommandLineUtils;

namespace Microsoft.Jupyter.Core
{
    class InteractiveKernel
    {
        public static void Init(ServiceCollection serviceCollection)
        {
            serviceCollection
                .AddSingleton<IExecutionEngine, KernelEngine>();
        }
            
        public static int Main(string[] args)
        {
            var app = new KernelApplication(
                PROPERTIES,
                Init
            );

            return app
                .WithDefaultCommands()
                .WithKernelSpecResources<InteractiveKernel>(
                    new Dictionary<string, string>
                    {
                        ["4ml-icon.png"] = "InteractiveKernel.res.4ml-icon.png"
                    }
                )
                .Execute(args);
        }
    }
}

