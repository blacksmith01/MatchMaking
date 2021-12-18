using System;
using System.Collections.Generic;
using System.Text;

namespace ServerLib
{
    public interface ILogger
    {
        void Msg(string msg);
        void Wrn(string msg);
        void Err(string msg);
    }

    public class EmptyLogger : ILogger
    {
        public void Msg(string msg)
        {

        }
        public void Wrn(string msg)
        {

        }
        public void Err(string msg)
        {

        }
    }
}
