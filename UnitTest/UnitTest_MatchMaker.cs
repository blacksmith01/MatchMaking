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

            // ���� ���� �� �߰�
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // ���� ����
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

            // �⺻ �߰�
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // �ߺ� �߰�
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // ���� �� ���߰�
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted);
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // ��Īť�� ������Ʈ �� ����,���߰�
            matchMaker.OnTimer();
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted == false);
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);
            matchMaker.OnTimer();
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);


            // ��Ī �Ϸ� �� ���߰�
            for (int i = 1; i < 10; i++)
            {
                Assert.True(matchMaker.AddPlayer(1 + i, 0) == ErrNo.OK);
            }
            matchMaker.OnTimer();
            Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
            matchedGame++;
            Assert.False(matchMaker.AddPlayer(1, 0) == ErrNo.OK);

            // ���� ����
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

            // ���� ���� �� ����
            Assert.False(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK);

            // ���� ����
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

            // ���� ����
            Assert.False(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK);

            // ��� ����
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted);

            // ���� ��õ�
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.Matching_Del_NotRequested);

            // ��Īť�� ������Ʈ �� ����
            Assert.True(matchMaker.AddPlayer(1, 0) == ErrNo.OK);
            matchMaker.OnTimer();
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.OK && directDeleted == false);
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.Matching_Del_Duplicated);
            matchMaker.OnTimer();
            Assert.True(matchMaker.DelPlayer(1, out directDeleted) == ErrNo.Matching_Del_NotRequested);

            // ��Ī �Ϸ� �� ����
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

            // ���� ����
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

            // ���� ����
            {
                var cts = new CancellationTokenSource();
                matchMaker.OnServerStartAsync(cts.Token).Wait();
                Assert.False(cts.IsCancellationRequested);
            }
            matchMaker.OnServerStarted();
            matchMaker.OnTimer();

            // ���� ���� ��Ī
            for (int i = 0; i < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; i++)
            {
                Assert.True(matchMaker.AddPlayer(i + 1, 0) == ErrNo.OK);
            }
            matchMaker.OnTimer();
            Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
            matchedGame++;
            matchMaker.OnTimer();

            // ���� �� ���� ��Ī
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

                // ���� �� ������ ���� �� ��Ī
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

            // �ð��� ������ ���� ��Ī ��ȭ
            for (int iPhase = 1; iPhase < GameConstants.MATCHING_PHASE_COUNT; iPhase++)
            {
                gameTime.UpdateTimeMode(0);
                GamePoint ptMin = 1;
                GamePoint ptMax = 1 + GameConstants.MATCHING_PHASE_POINT_BOUND_ARR[0];
                Random rand = new Random();
                for (int iPlayer = 0; iPlayer < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; iPlayer++)
                {
                    // �׽�Ʈ�� ������ ������ �ϳ� �־��ش�.
                    var point = iPlayer == 0 ? GameConstants.MATCHING_PHASE_POINT_BOUND_ARR[iPhase] : rand.Next(ptMin, ptMax);
                    Assert.True(matchMaker.AddPlayer(iPlayer + 1, point) == ErrNo.OK);
                }
                matchMaker.OnTimer();
                Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame);

                // �ð� ���� �� ��Ī
                gameTime.UpdateTimeMode(GameConstants.MATCHING_PHASE_TIME_SEC_ARR[iPhase - 1] * TimeEx.Duration_Sec_To_Ms);
                matchMaker.OnTimer();
                Assert.True(matchMaker.GetStat_GameAllocated() == matchedGame + 1);
                matchedGame++;
                matchMaker.OnTimer();
            }

            // ���� ����
            matchMaker.OnServerStopAsync().Wait();
            matchMaker.OnServerStopped();
        }
    }
}