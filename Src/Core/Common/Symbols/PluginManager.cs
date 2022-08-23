using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Microsoft.Formula.Common.Terms;

public class PluginManager
{
    private static bool loaded = false;
    private static object pluginLock = new object();
    private static List<OpPluginFunc> pluginFunctionsList = new();
    private static OpPluginFunc[] pluginFunctions = null;
    
    private static void LoadPlugins()
    {
        try
        {

            string pluginsPath = Environment.GetEnvironmentVariable("FORMULA_PLUGINS_PATH");
            if (pluginsPath == null)
            {

                string formulaDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                pluginsPath = Path.Join(formulaDir, "Plugins");
            }

            string[] pluginDirs = pluginsPath.Split(";");
            foreach (string pluginDir in pluginDirs)
            {
                string[] plugins = Directory.GetFiles(pluginDir.Trim(), "*.dll");
                foreach (string plugin in plugins)
                {
                    LoadPlugin(plugin);
                }
            }
        }
        catch (Exception exc)
        {
            Console.WriteLine(exc.StackTrace);
        }

        pluginFunctions = pluginFunctionsList.ToArray();
    }

    private static void LoadPlugin(string path)
    {
        try
        {
            Assembly ass = Assembly.LoadFrom(path);
            foreach (Module module in ass.Modules)
            {
                foreach (Type type in module.GetTypes())
                {
                    if (type.IsSubclassOf(typeof(OpPluginFunc)))
                    {
                        var cons = type.GetConstructor(Type.EmptyTypes);
                        if (cons != null)
                        {
                            object funcObj = cons.Invoke(null);
                            
                            if (funcObj != null)
                            {
                                OpPluginFunc func = (OpPluginFunc) funcObj;
                                pluginFunctionsList.Add(func);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exc)
        {
            Console.WriteLine(exc.StackTrace);
        }
    }

    public static OpPluginFunc[] GetPluginFunctions()
    {
        lock (pluginLock)
        {
            if (!loaded)
            {
                LoadPlugins();
                loaded = true;
            }

            return pluginFunctions;
        }
    }
}