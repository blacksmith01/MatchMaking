using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CommonLib
{
    public static class ReflectionEx
    {
        public static IEnumerable<Type> GetTypesBasedOnInterface<T>(IEnumerable<Assembly> asms)
        {
            foreach (var asm in asms)
            {
                foreach (var t in asm.GetTypes())
                {
                    if (!t.IsInterface && !t.IsAbstract && t.GetInterface(typeof(T).Name) != null)
                        yield return t;
                }
            }
        }
    }
}
