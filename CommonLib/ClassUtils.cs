using System;
using System.Collections.Generic;
using System.Text;

namespace CommonLib
{
    public static class ClassUtils
    {
        public static void Swap<T>(ref T t1, ref T t2) where T : class
        {
            var temp = t1;
            t1 = t2;
            t2 = temp;
        }
    }
}
