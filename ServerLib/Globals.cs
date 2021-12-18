using CommonLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ServerLib
{
    internal static class Globals
    {
        public static string[] Args { get; private set; } = { };
        public static List<Assembly> Assemblies { get; private set; } = new();

        public static void Init(string[] args, IEnumerable<Assembly> assemblies)
        {
            Args = args;
            Assemblies = assemblies.ToList();
        }

        public static IEnumerable<Type> GetTypesFromAssemblies<T>()
        {
            return ReflectionEx.GetTypesBasedOnInterface<T>(Assemblies);
        }
    }
}
