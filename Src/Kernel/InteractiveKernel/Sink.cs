using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Formula.API;
using Microsoft.Formula.Common;
using Microsoft.Formula.CommandLine;

namespace Microsoft.Jupyter.Core
{
    public class Sink : IMessageSink
    {
        private StringBuilder _strBuilder;
        private StringWriter _tw;

        private StringBuilder _estrBuilder;
        private StringWriter _etw;

        private IChannel _channel;

        private IUpdatableDisplay _updateCh;

        private List<(string, string)> _rowList;

        private bool printedErr = false;
        private SpinLock printedErrLock = new SpinLock();
        public bool PrintedError
        {
            get
            {
                bool gotLock = false;
                try
                {
                    //// printedErrLock.Enter(ref gotLock);
                    return printedErr;
                }
                finally
                {
                    if (gotLock)
                    {
                        //// printedErrLock.Exit();
                    }
                }
            }
        }

        enum Level
        {
            INFO,
            ERROR
        }

        public Sink()
        {
            _strBuilder = new StringBuilder();
            _tw = new StringWriter(_strBuilder);

            Console.SetOut(_tw);

            _estrBuilder = new StringBuilder();
            _etw = new StringWriter(_estrBuilder);

            Console.SetError(_etw);

            _rowList = new List<(string, string)>();
        }

        public void setChannel(IChannel channel)
        {
            _channel = channel;
            _updateCh = _channel.DisplayUpdatable("");
        }

        public void ShowOutput()
        {
            _rowList.Clear();
            var o = GetStdOut().Split("\n");
            var e = GetStdErr().Split("\n");
            for(var i = 0;i < o.Length;++i)
            {
                if(o[i].Length < 1)
                {
                    continue;
                }
                UpdateTable(Level.INFO, o[i]);
            }
            for(var i = 0;i < e.Length;++i)
            {
                if(e[i].Length < 1)
                {
                    continue;
                }
                UpdateTable(Level.ERROR, e[i]);
            }
        }

        public string GetStdOut()
        {
            return _strBuilder.ToString();
        }

        public string GetStdErr()
        {
            return _estrBuilder.ToString();
        }

        public void Clear()
        {
            _strBuilder.Clear();
            _estrBuilder.Clear();
            _rowList.Clear();
            SetPrintedError(false);
        }

        public System.IO.TextWriter Writer
        {
            get { return _tw; }
        }

        public void WriteMessage(string msg)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(msg);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void WriteMessage(string msg, SeverityKind severity)
        {
            switch (severity)
            {
                case SeverityKind.Info:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(msg);
                    break;
                case SeverityKind.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.Write(msg);
                    break;
                case SeverityKind.Error:
                    SetPrintedError(true);
                    Console.Error.Write(msg);
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                default:
                    Console.Write(msg);
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
            }

            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void WriteMessageLine(string msg)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(msg);
            UpdateTable(Level.INFO, msg);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void WriteMessageLine(string msg, SeverityKind severity)
        {
            switch (severity)
            {
                case SeverityKind.Info:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(msg);
                    UpdateTable(Level.INFO, msg);
                    break;
                case SeverityKind.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine(msg);
                    UpdateTable(Level.ERROR, msg);
                    break;
                case SeverityKind.Error:
                    SetPrintedError(true);
                    Console.Error.WriteLine(msg);
                    UpdateTable(Level.ERROR, msg);
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(msg);
                    UpdateTable(Level.INFO, msg);
                    break;
            }

            Console.ForegroundColor = ConsoleColor.Gray;
        }

        private void SetPrintedError(bool flag)
        {
            bool gotLock = false;
            try
            {
                printedErrLock.Enter(ref gotLock);
                printedErr = flag;
            }
            finally
            {
                if (gotLock)
                {
                    printedErrLock.Exit();
                }
            }
        }

        private void UpdateTable(Level lvl, string msg)
        {
            _rowList.Clear();
            var o = GetStdOut().Split("\n");
            for(var i = 0;i < o.Length;++i)
            {
                if(o[i].Length < 1)
                {
                    continue;
                }
                if(msg != o[i])
                {
                    _rowList.Add(("INFO", o[i]));
                }
            }
            if(lvl == Level.INFO)
            {
                _rowList.Add(("INFO", msg));
                _updateCh.Update(new Table<(string, string)>
                {
                    Columns = new List<(string, Func<(string, string), string>)>
                    {
                        ("Level", item => item.Item1),
                        ("Output", item => item.Item2)
                    },
                    Rows = _rowList
                });
            }
            else
            {
                _rowList.Add(("ERROR", msg));
                _updateCh.Update(new Table<(string, string)>
                {
                    Columns = new List<(string, Func<(string, string), string>)>
                    {
                        ("Level", item => item.Item1),
                        ("Output", item => item.Item2)
                    },
                    Rows = _rowList
                });
            }
        }
    }
}
