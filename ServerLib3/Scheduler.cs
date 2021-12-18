using System;
using System.Collections.Generic;
using System.Text;

namespace ServerLib
{
    public interface ISchedulable
    {
        void OnSchedule(TickCnt now);
    }


    public class Scheduler
    {
    }
}
