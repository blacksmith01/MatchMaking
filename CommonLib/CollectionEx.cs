using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CommonLib
{
    public static class CollectionEx
    {
        public static T[] CreateArray<T>(int size, Func<T> func)
        {
            T[] array = new T[size];
            for (int i = 0; i < size; i++)
            {
                array[i] = func();
            }
            return array;
        }

        public static T? PopBack<T>(this List<T> list) where T : class
        {
            if(list.Any())
            {
                var elm = list.Last();
                list.RemoveAt(list.Count-1);
                return elm;
            }
            else
            {
                return default(T);
            }
        }
    }
}
