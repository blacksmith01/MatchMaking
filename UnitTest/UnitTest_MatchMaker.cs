using CommonLib;
using ServerLib;
using ServerLib.Services.Content;
using System;
using System.Threading;
using Xunit;

namespace UnitTest
{
    public class UnitTest_MatchMaker
    {
        [Fact]
        public static void Test_Add()
        {
            GameTimeService gameTime = new();
            MatchMakerService matchMaker = new(gameTime, new EmptyLogger<MatchMakerService>());
            Int64 matchedGame = 0;
            bool directDeleted = false;

            // 서비스 시작 전 추가
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // 서비스 시작
            {
                var cts = new CancellationTokenSource();
                matchMaker.OnServerStartAsync(cts.Token).Wait();
                Assert.False(cts.IsCancellationRequested);
            }
            matchMaker.OnServerStarted();
            for (int i = 0; i < 2; i++)
            {
                matchMaker.OnTimer();
            }

            // 기본 추가
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // 중복 추가
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // 삭제 후 재추가
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted);
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // 매칭큐에 업데이트 후 삭제,재추가
            matchMaker.OnTimer();
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted == false);
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);
            matchMaker.OnTimer();
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);


            // 매칭 완료 후 재추가
            for (int i = 1; i < 10; i++)
            {
                Assert.True(matchMaker.AddPlayer(1 + i, 0) == ErrNo.OK);
            }
            matchMaker.OnTimer();
            Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
            matchedGame++;
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // 서비스 종료
            matchMaker.OnServerStopAsync().Wait();
            matchMaker.OnServerStopped();
        }

        [Fact]
        public static void Test_Del()
        {
            GameTimeService gameTime = new();
            MatchMakerService matchMaker = new(gameTime, new EmptyLogger<MatchMakerService>());
            Int64 matchedGame = 0;
            bool directDeleted = false;

            // 서비스 시작 전 삭제
            Assert.False(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK);

            // 서비스 시작
            {
                var cts = new CancellationTokenSource();
                matchMaker.OnServerStartAsync(cts.Token).Wait();
                Assert.False(cts.IsCancellationRequested);
            }
            matchMaker.OnServerStarted();
            for (int i = 0; i < 2; i++)
            {
                matchMaker.OnTimer();
            }

            // 삭제 실패
            Assert.False(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK);

            // 즉시 삭제
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted);

            // 삭제 재시도
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.Matching_Del_NotRequested);

            // 매칭큐에 업데이트 후 삭제
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);
            matchMaker.OnTimer();
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted == false);
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.Matching_Del_Duplicated);
            matchMaker.OnTimer();
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.Matching_Del_NotRequested);

            // 매칭 완료 후 삭제
            for (int i = 0; i < 10; i++)
            {
                Assert.True(matchMaker.AddPlayer(1 + i, 0) == ErrNo.OK);
            }
            matchMaker.OnTimer();
            Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
            matchedGame++;
            Assert.False(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK);
            matchMaker.OnTimer();
            Assert.False(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK);

            // 서비스 종료
            matchMaker.OnServerStopAsync().Wait();
            matchMaker.OnServerStopped();
        }


        [Fact]
        public static void Test_Matching()
        {
            GameTimeService gameTime = new();
            MatchMakerService matchMaker = new(gameTime, new EmptyLogger<MatchMakerService>());
            Int64 matchedGame = 0;
            bool directDeleted = false;

            // 서비스 시작
            {
                var cts = new CancellationTokenSource();
                matchMaker.OnServerStartAsync(cts.Token).Wait();
                Assert.False(cts.IsCancellationRequested);
            }
            matchMaker.OnServerStarted();
            matchMaker.OnTimer();

            // 동일 점수 매칭
            for (int i = 0; i < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; i++)
            {
                Assert.True(matchMaker.AddPlayer(i + 1, 0) == ErrNo.OK);
            }
            matchMaker.OnTimer();
            Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
            matchedGame++;
            matchMaker.OnTimer();

            // 범위 내 점수 매칭
            for (int iPhase = 0; iPhase < GameConstants.MATCHING_PHASE_COUNT; iPhase++)
            {
                gameTime.UpdateTimeMode(0);
                GamePoint ptMin = 1;
                GamePoint ptMax = 1 + GameConstants.MATCHING_PHASE_POINT_BOUND_ARR[iPhase];
                Random rand = new Random();
                Assert.True(matchMaker.AddPlayer(1, ptMin - 1) == ErrNo.OK);
                Assert.True(matchMaker.AddPlayer(2, ptMax + 1) == ErrNo.OK);
                for (int iPlayer = 2; iPlayer < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; iPlayer++)
                {
                    Assert.True(matchMaker.AddPlayer(iPlayer + 1, rand.Next(ptMin, ptMax)) == ErrNo.OK);
                }
                if (iPhase > 0)
                {
                    gameTime.UpdateTimeMode(GameConstants.MATCHING_PHASE_TIME_SEC_ARR[iPhase - 1] * TimeEx.Duration_Sec_To_Ms);
                }
                matchMaker.OnTimer();
                Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame);

                // 범위 내 점수로 조정 후 매칭
                for (int iPlayer = 0; iPlayer < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; iPlayer++)
                {
                    Assert.True(matchMaker.DelPlayer(iPlayer + 1, out directDeleted) == ErrNo.OK && directDeleted == false);
                }
                gameTime.UpdateTimeMode(0);
                matchMaker.OnTimer();
                Assert.True(matchMaker.AddPlayer(1, ptMin) == ErrNo.OK);
                Assert.True(matchMaker.AddPlayer(2, ptMax) == ErrNo.OK);
                for (int iPlayer = 2; iPlayer < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; iPlayer++)
                {
                    Assert.True(matchMaker.AddPlayer(iPlayer + 1, rand.Next(ptMin, ptMax)) == ErrNo.OK);
                }
                if (iPhase > 0)
                {
                    gameTime.UpdateTimeMode(GameConstants.MATCHING_PHASE_TIME_SEC_ARR[iPhase - 1] * TimeEx.Duration_Sec_To_Ms);
                }
                matchMaker.OnTimer();
                Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
                matchedGame++;
                matchMaker.OnTimer();
            }

            // 시간이 지남에 따라 매칭 변화
            for (int iPhase = 1; iPhase < GameConstants.MATCHING_PHASE_COUNT; iPhase++)
            {
                gameTime.UpdateTimeMode(0);
                GamePoint ptMin = 1;
                GamePoint ptMax = 1 + GameConstants.MATCHING_PHASE_POINT_BOUND_ARR[0];
                Random rand = new Random();
                for (int iPlayer = 0; iPlayer < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; iPlayer++)
                {
                    // 테스트할 범위의 점수를 하나 넣어준다.
                    var point = iPlayer == 0 ? GameConstants.MATCHING_PHASE_POINT_BOUND_ARR[iPhase] : rand.Next(ptMin, ptMax);
                    Assert.True(matchMaker.AddPlayer(iPlayer + 1, point) == ErrNo.OK);
                }
                matchMaker.OnTimer();
                Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame);

                // 시간 조정 후 매칭
                gameTime.UpdateTimeMode(GameConstants.MATCHING_PHASE_TIME_SEC_ARR[iPhase - 1] * TimeEx.Duration_Sec_To_Ms);
                matchMaker.OnTimer();
                Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
                matchedGame++;
                matchMaker.OnTimer();
            }

            // 서비스 종료
            matchMaker.OnServerStopAsync().Wait();
            matchMaker.OnServerStopped();
        }
    }
}