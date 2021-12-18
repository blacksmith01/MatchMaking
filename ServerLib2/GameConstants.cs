global using PlayerId = System.Int32;
global using GamePoint = System.Int32;

using System;
using System.Collections.Generic;
using System.Text;

namespace ServerLib
{
    public enum ErrNo
    {
        OK = 0,

        Category_Matching = 1000,
        Matching_Add_InvalidRequest,
        Matching_Add_NotServiceState,
        Matching_Add_AlreadyRequested,

        Matching_Del_InvalidRequest,
        Matching_Del_NotServiceState,
        Matching_Del_InvalidState,
        Matching_Del_NotRequested,
        Matching_Del_Duplicated,
        Matching_Del_AlreadyMatched,
    }

    public enum PlayerGrade
    {
        None = 0,
        Bronze,
        Silver,
        Gold,
    }

    public class GameConstants
    {
        public static bool IsValidPlayerId(PlayerId id)
        {
            return id > 0;
        }

        public const GamePoint GAMEPOINT_BRONZE_START = 0;
        public const GamePoint GAMEPOINT_SILVER_START = 5000;
        public const GamePoint GAMEPOINT_GOLD_START = 10000;

        public static PlayerGrade GetPlayerGrade_ByGamePoint(GamePoint pt)
        {
            if (pt < GAMEPOINT_SILVER_START)
            {
                return PlayerGrade.Bronze;
            }
            else if (pt < GAMEPOINT_GOLD_START)
            {
                return PlayerGrade.Silver;
            }
            else
            {
                return PlayerGrade.Gold;
            }
        }

        public const Int32 MATCHING_PHASE_1_TIME_SEC = 10;
        public const Int32 MATCHING_PHASE_2_TIME_SEC = 20;
        public const Int32 MATCHING_PHASE_3_TIME_SEC = 30;
        public const Int32 MATCHING_PHASE_4_TIME_SEC = 40;

        public const GamePoint MATCHING_PHASE_1_POINT_BOUND = 500;
        public const GamePoint MATCHING_PHASE_2_POINT_BOUND = 1000;
        public const GamePoint MATCHING_PHASE_3_POINT_BOUND = 1500;
        public const GamePoint MATCHING_PHASE_4_POINT_BOUND = 3000;

        public static GamePoint GetPointBound_ByRegistTime(TimeT elapsed)
        {
            if (elapsed < MATCHING_PHASE_1_TIME_SEC)
            {
                return MATCHING_PHASE_1_POINT_BOUND;
            }
            else if (elapsed < MATCHING_PHASE_2_TIME_SEC)
            {
                return MATCHING_PHASE_2_POINT_BOUND;
            }
            else if (elapsed < MATCHING_PHASE_3_TIME_SEC)
            {
                return MATCHING_PHASE_3_POINT_BOUND;
            }
            else
            {
                return MATCHING_PHASE_4_POINT_BOUND;
            }
        }

        public const Int32 MAX_PLAYER_COUNT_IN_ROOM = 8;
    }

    public class MatchingNode
    {
        public PlayerId Id;
        public GamePoint Point;
        public TimeT RegTime;
        public Int32 IdxPoint;
        public Int32 IdxRegtime;
        public bool IsInMatching;
        public bool IsDelReserved;
        public bool IsGameMatched;

        public void Init(PlayerId id, GamePoint point, TimeT regTime)
        {
            Id = id;
            Point = point;
            RegTime = regTime;
            IdxPoint = 0;
            IdxRegtime = 0;
            IsInMatching = false;
            IsDelReserved = false;
            IsGameMatched = false;
        }

        public class ComparerByPoint : IComparer<MatchingNode>
        {
            public int Compare(MatchingNode? x, MatchingNode? y)
            {
                if (x == null)
                {
                    if (y == null)
                    {
                        return 0;
                    }
                    else
                    {
                        return -1;
                    }
                }
                else if (y == null)
                {
                    if (x == null)
                    {
                        return 0;
                    }
                    else
                    {
                        return 1;
                    }
                }
                else
                {
                    var diff = x.Point - y.Point;
                    if (diff == 0)
                    {
                        return x.Id - y.Id;
                    }
                    else
                    {
                        return diff;
                    }
                }
            }
        }
        public static readonly ComparerByPoint DefaultComparerByPoint = new();
    }

}
