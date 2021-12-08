﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FantasyCritic.Lib.Domain.LeagueActions;
using FantasyCritic.Lib.Domain.ScoringSystems;
using FantasyCritic.Lib.Identity;
using FantasyCritic.Lib.Services;
using NodaTime;

namespace FantasyCritic.Lib.Domain
{
    public class Publisher : IEquatable<Publisher>
    {
        public Publisher(Guid publisherID, LeagueYear leagueYear, FantasyCriticUser user, string publisherName, int draftPosition, 
            IEnumerable<PublisherGame> publisherGames, uint budget, int freeGamesDropped, int willNotReleaseGamesDropped, int willReleaseGamesDropped,
            bool autoDraft)
        {
            PublisherID = publisherID;
            LeagueYear = leagueYear;
            User = user;
            PublisherName = publisherName;
            DraftPosition = draftPosition;
            PublisherGames = publisherGames.ToList();
            Budget = budget;
            FreeGamesDropped = freeGamesDropped;
            WillNotReleaseGamesDropped = willNotReleaseGamesDropped;
            WillReleaseGamesDropped = willReleaseGamesDropped;
            AutoDraft = autoDraft;
        }

        public Guid PublisherID { get; }
        public LeagueYear LeagueYear { get; }
        public FantasyCriticUser User { get; }
        public string PublisherName { get; }
        public int DraftPosition { get; }
        public IReadOnlyList<PublisherGame> PublisherGames { get; private set; }
        public uint Budget { get; private set; }
        public int FreeGamesDropped { get; private set; }
        public int WillNotReleaseGamesDropped { get; private set; }
        public int WillReleaseGamesDropped { get; private set; }
        public bool AutoDraft { get; }

        public decimal? AverageCriticScore
        {
            get
            {
                List<decimal> gamesWithCriticScores = PublisherGames
                    .Where(x => !x.CounterPick)
                    .Where(x => x.MasterGame.HasValue)
                    .Where(x => x.MasterGame.Value.MasterGame.CriticScore.HasValue)
                    .Select(x => x.MasterGame.Value.MasterGame.CriticScore.Value)
                    .ToList();

                if (gamesWithCriticScores.Count == 0)
                {
                    return null;
                }

                decimal average = gamesWithCriticScores.Sum(x => x) / gamesWithCriticScores.Count;
                return average;
            }
        }

        public decimal TotalFantasyPoints
        {
            get
            {
                var emptyCounterPickSlotPoints = GetEmptyCounterPickSlotPoints() ?? 0m;
                var score = PublisherGames.Sum(x => x.FantasyPoints);
                if (!score.HasValue)
                {
                    return emptyCounterPickSlotPoints;
                }

                return score.Value + emptyCounterPickSlotPoints;
            }
        }

        public decimal GetProjectedFantasyPoints(SystemWideValues systemWideValues, bool simpleProjections, LocalDate currentDate)
        {
            var currentGamesScore = GetPublisherSlots()
                .Sum(x => x.GetProjectedOrRealFantasyPoints(x.SlotIsValid(LeagueYear), LeagueYear.Options.ScoringSystem, systemWideValues, simpleProjections, currentDate));

            var availableStandardSlots = GetNumberAvailableSlots(false, LeagueYear.SupportedYear.Finished);
            var emptyStandardSlotsScore = availableStandardSlots * systemWideValues.AverageStandardGamePoints;

            var availableCounterPickSlots = GetNumberAvailableSlots(true, LeagueYear.SupportedYear.Finished);
            var projectedEmptyCounterPickScore = availableCounterPickSlots * systemWideValues.AverageCounterPickPoints;
            var emptyCounterPickSlotPoints = GetEmptyCounterPickSlotPoints() ?? projectedEmptyCounterPickScore;

            return currentGamesScore + emptyStandardSlotsScore + emptyCounterPickSlotPoints;
        }

        private decimal? GetEmptyCounterPickSlotPoints()
        {
            if (!LeagueYear.SupportedYear.Finished)
            {
                return null;
            }

            var expectedNumberOfCounterPicks = LeagueYear.Options.CounterPicks;
            var numberCounterPicks = PublisherGames.Count(x => x.CounterPick);
            var emptySlots = expectedNumberOfCounterPicks - numberCounterPicks;
            var points = emptySlots * -15m;
            return points;
        }

        public int GetNumberAvailableSlots(bool counterPick, bool yearFinished)
        {
            if (yearFinished)
            {
                return 0;
            }

            var numberSlots = counterPick ? LeagueYear.Options.CounterPicks : LeagueYear.Options.StandardGames;
            var games = PublisherGames.Where(x => x.CounterPick == counterPick);
            var numberAvailableSlots = numberSlots - games.Count();
            return numberAvailableSlots;
        }

        public IReadOnlyList<PublisherSlot> GetPublisherSlots()
        {
            List<PublisherSlot> publisherSlots = new List<PublisherSlot>();

            int overallSlotNumber = 0;
            var standardGamesBySlot = PublisherGames.Where(x => !x.CounterPick).ToDictionary(x => x.SlotNumber);
            for (int standardGameIndex = 0; standardGameIndex < LeagueYear.Options.StandardGames; standardGameIndex++)
            {
                Maybe<PublisherGame> standardGame = Maybe<PublisherGame>.None;
                if (standardGamesBySlot.TryGetValue(standardGameIndex, out var foundGame))
                {
                    standardGame = foundGame;
                }
                Maybe<SpecialGameSlot> specialSlot = LeagueYear.Options.GetSpecialGameSlotByOverallSlotNumber(standardGameIndex);

                publisherSlots.Add(new PublisherSlot(standardGameIndex, overallSlotNumber, false, specialSlot, standardGame));
                overallSlotNumber++;
            }

            var counterPicksBySlot = PublisherGames.Where(x => x.CounterPick).ToDictionary(x => x.SlotNumber);
            for (int counterPickIndex = 0; counterPickIndex < LeagueYear.Options.CounterPicks; counterPickIndex++)
            {
                Maybe<PublisherGame> counterPick = Maybe<PublisherGame>.None;
                if (counterPicksBySlot.TryGetValue(counterPickIndex, out var foundGame))
                {
                    counterPick = foundGame;
                }

                publisherSlots.Add(new PublisherSlot(counterPickIndex, overallSlotNumber, true, Maybe<SpecialGameSlot>.None, counterPick));
                overallSlotNumber++;
            }

            return publisherSlots;
        }

        public Maybe<PublisherGame> GetPublisherGame(MasterGame masterGame) => GetPublisherGameByMasterGameID(masterGame.MasterGameID);

        public Maybe<PublisherGame> GetPublisherGameByMasterGameID(Guid masterGameID)
        {
            return PublisherGames.SingleOrDefault(x => x.MasterGame.HasValue && x.MasterGame.Value.MasterGame.MasterGameID == masterGameID);
        }

        public Maybe<PublisherGame> GetPublisherGameByPublisherGameID(Guid publisherGameID)
        {
            return PublisherGames.SingleOrDefault(x => x.PublisherGameID == publisherGameID);
        }

        public HashSet<MasterGame> MyMasterGames => PublisherGames
            .Where(x => x.MasterGame.HasValue)
            .Select(x => x.MasterGame.Value.MasterGame)
            .Distinct()
            .ToHashSet();

        public bool Equals(Publisher other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return PublisherID.Equals(other.PublisherID);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Publisher) obj);
        }

        public override int GetHashCode()
        {
            return PublisherID.GetHashCode();
        }

        public void AcquireGame(PublisherGame game, uint bidAmount)
        {
            PublisherGames = PublisherGames.Concat(new []{ game }).ToList();
            Budget -= bidAmount;
        }

        public Result CanDropGame(bool willRelease)
        {
            var leagueOptions = LeagueYear.Options;
            if (willRelease)
            {
                if (leagueOptions.WillReleaseDroppableGames == -1 || leagueOptions.WillReleaseDroppableGames > WillReleaseGamesDropped)
                {
                    return Result.Success();
                }
                if (leagueOptions.FreeDroppableGames == -1 || leagueOptions.FreeDroppableGames > FreeGamesDropped)
                {
                    return Result.Success();
                }
                return Result.Failure("Publisher cannot drop any more 'Will Release' games");
            }

            if (leagueOptions.WillNotReleaseDroppableGames == -1 || leagueOptions.WillNotReleaseDroppableGames > WillNotReleaseGamesDropped)
            {
                return Result.Success();
            }
            if (leagueOptions.FreeDroppableGames == -1 || leagueOptions.FreeDroppableGames > FreeGamesDropped)
            {
                return Result.Success();
            }
            return Result.Failure("Publisher cannot drop any more 'Will Not Release' games");
        }

        public void DropGame(PublisherGame publisherGame)
        {
            var leagueOptions = LeagueYear.Options;
            if (publisherGame.WillRelease())
            {
                if (leagueOptions.WillReleaseDroppableGames == -1 || leagueOptions.WillReleaseDroppableGames > WillReleaseGamesDropped)
                {
                    WillReleaseGamesDropped++;
                    PublisherGames = PublisherGames.Where(x => x.PublisherGameID != publisherGame.PublisherGameID).ToList();
                    return;
                }
                if (leagueOptions.FreeDroppableGames == -1 || leagueOptions.FreeDroppableGames > FreeGamesDropped)
                {
                    FreeGamesDropped++;
                    PublisherGames = PublisherGames.Where(x => x.PublisherGameID != publisherGame.PublisherGameID).ToList();
                    return;
                }
                throw new Exception("Publisher cannot drop any more 'Will Release' games");
            }

            if (leagueOptions.WillNotReleaseDroppableGames == -1 || leagueOptions.WillNotReleaseDroppableGames > WillNotReleaseGamesDropped)
            {
                WillNotReleaseGamesDropped++;
                PublisherGames = PublisherGames.Where(x => x.PublisherGameID != publisherGame.PublisherGameID).ToList();
                return;
            }
            if (leagueOptions.FreeDroppableGames == -1 || leagueOptions.FreeDroppableGames > FreeGamesDropped)
            {
                FreeGamesDropped++;
                PublisherGames = PublisherGames.Where(x => x.PublisherGameID != publisherGame.PublisherGameID).ToList();
                return;
            }
            throw new Exception("Publisher cannot drop any more 'Will Not Release' games");
        }
    }
}
