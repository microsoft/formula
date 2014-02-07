using System;
using Microsoft.Z3;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Sahvy
{    
    class Program
    {
        static void calc(Plot3d plotter, string filename)
        {
            try
            {
                var sys = new FormulaSystem(filename, plotter);
                DateTime start = DateTime.Now;
                Log.Debug.WriteLine("Started at {0}", start);
                sys.Reach();
                DateTime end = DateTime.Now;
                Log.Debug.WriteLine("Finished at {0}", end);
                Log.Debug.WriteLine("Total time: {0}s", (end - start).TotalSeconds);
                Log.Debug.Flush();
                Log.WriteLine("Total time: {0}s", (end - start).TotalSeconds);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }                       

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Missing first argument");
                Console.WriteLine("Please specify a model file");
                return;
            }

            Log.SetDebugLogFile("debug.txt");
            try
            {
                using (Plot3d plotter = new Plot3d())
                {
                    Thread calcThread = new Thread(() => calc(plotter, args[0]));
                    calcThread.Start();
                    plotter.Run(30.0);
                    calcThread.Abort();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            finally
            {
                Log.Debug.Close();
            }
        }
    }
}
