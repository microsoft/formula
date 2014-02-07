using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Sahvy
{
    public class Log
    {
        static public TextWriter Debug = Console.Error;
        static public TextWriter Output = Console.Out;
        static public void SetDebugLogFile(string filename)
        {
            Debug = File.CreateText(filename);
        }
        static public void SetOutputLogFile(string filename)
        {
            Output = File.CreateText(filename);
        }
        static public void Write(string value)
        {
            Output.Write(value);
        }
        static public void WriteLine(string value)
        {
            Output.WriteLine(value);
        }
        static public void Write(string format, params object[] paramList)
        {
            Output.Write(format, paramList);
        }
        static public void WriteLine(string format, params object[] paramList)
        {
            Output.WriteLine(format, paramList);
        }
    }
}
