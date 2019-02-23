﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FantasyCritic.Lib.Domain;
using FantasyCritic.Lib.Domain.Results;
using FantasyCritic.Lib.Enums;
using FantasyCritic.Lib.Extensions;
using FantasyCritic.Lib.Interfaces;
using MoreLinq;
using NodaTime;

namespace FantasyCritic.Lib.Services
{
    public class DraftService
    {
        private readonly IFantasyCriticRepo _fantasyCriticRepo;
        private readonly IClock _clock;
        private readonly GameAquisitionService _gameAquisitionService;
        private readonly LeagueMemberService _leagueMemberService;
        private readonly PublisherService _publisherService;
        private readonly InterLeagueService _interLeagueService;

        public DraftService(GameAquisitionService gameAquisitionService, LeagueMemberService leagueMemberService,
            PublisherService publisherService, InterLeagueService interLeagueService, IFantasyCriticRepo fantasyCriticRepo, IClock clock)
        {
            _fantasyCriticRepo = fantasyCriticRepo;
            _clock = clock;

            _leagueMemberService = leagueMemberService;
            _publisherService = publisherService;
            _interLeagueService = interLeagueService;
            _gameAquisitionService = gameAquisitionService;
        }

        public async Task<StartDraftResult> GetStartDraftResult(LeagueYear leagueYear, IReadOnlyList<Publisher> publishersInLeague, IReadOnlyList<FantasyCriticUser> usersInLeague)
        {
            if (leagueYear.PlayStatus.PlayStarted)
            {
                return new StartDraftResult(true, new List<string>());
            }

            var supportedYears = await _fantasyCriticRepo.GetSupportedYears();
            var supportedYear = supportedYears.Single(x => x.Year == leagueYear.Year);

            List<string> errors = new List<string>();

            if (usersInLeague.Count() < 2)
            {
                errors.Add("You need to have at least two players in the league.");
            }

            if (publishersInLeague.Count() != usersInLeague.Count())
            {
                errors.Add("Not every player has created a publisher.");
            }

            if (!supportedYear.OpenForPlay)
            {
                errors.Add($"This year is not yet open for play. It will become available on {supportedYear.StartDate}.");
            }

            return new StartDraftResult(!errors.Any(), errors);
        }

        public bool LeagueIsReadyToSetDraftOrder(IEnumerable<Publisher> publishersInLeague, IEnumerable<FantasyCriticUser> usersInLeague)
        {
            if (publishersInLeague.Count() != usersInLeague.Count())
            {
                return false;
            }

            if (publishersInLeague.Count() < 2)
            {
                return false;
            }

            return true;
        }

        public bool LeagueIsReadyToPlay(SupportedYear supportedYear, IEnumerable<Publisher> publishersInLeague, IEnumerable<FantasyCriticUser> usersInLeague)
        {
            if (!LeagueIsReadyToSetDraftOrder(publishersInLeague, usersInLeague))
            {
                return false;
            }

            if (!supportedYear.OpenForPlay)
            {
                return false;
            }

            return true;
        }

        public Task StartDraft(LeagueYear leagueYear)
        {
            return _fantasyCriticRepo.StartDraft(leagueYear);
        }

        public Task SetDraftPause(LeagueYear leagueYear, bool pause)
        {
            return _fantasyCriticRepo.SetDraftPause(leagueYear, pause);
        }

        public async Task UndoLastDraftAction(LeagueYear leagueYear, IReadOnlyList<Publisher> publishers)
        {
            var publisherGames = publishers.SelectMany(x => x.PublisherGames);
            var newestGame = publisherGames.MaxBy(x => x.Timestamp).First();

            var publisher = publishers.Single(x => x.PublisherGames.Select(y => y.PublisherGameID).Contains(newestGame.PublisherGameID));

            await _publisherService.RemovePublisherGame(leagueYear, publisher, newestGame);
        }

        public async Task<Result> SetDraftOrder(LeagueYear leagueYear, IEnumerable<KeyValuePair<Publisher, int>> draftPositions)
        {
            var publishersInLeague = await _publisherService.GetPublishersInLeagueForYear(leagueYear.League, leagueYear.Year);
            int publishersCount = publishersInLeague.Count;
            if (publishersCount != draftPositions.Count())
            {
                return Result.Fail("Not setting all publishers.");
            }

            var requiredNumbers = Enumerable.Range(1, publishersCount).ToList();
            var requestedDraftNumbers = draftPositions.Select(x => x.Value);
            bool allRequiredPresent = new HashSet<int>(requiredNumbers).SetEquals(requestedDraftNumbers);
            if (!allRequiredPresent)
            {
                return Result.Fail("Some of the positions are not valid.");
            }

            await _fantasyCriticRepo.SetDraftOrder(draftPositions);
            return Result.Ok();
        }

        public Maybe<Publisher> GetNextDraftPublisher(LeagueYear leagueYear, IReadOnlyList<Publisher> publishersInLeagueForYear)
        {
            if (!leagueYear.PlayStatus.DraftIsActive)
            {
                return Maybe<Publisher>.None;
            }

            DraftPhase phase = GetDraftPhase(leagueYear, publishersInLeagueForYear);
            if (phase.Equals(DraftPhase.StandardGames))
            {
                var publishersWithLowestNumberOfGames = publishersInLeagueForYear.MinBy(x => x.PublisherGames.Count(y => !y.CounterPick));
                var allPlayersHaveSameNumberOfGames = publishersInLeagueForYear.Select(x => x.PublisherGames.Count(y => !y.CounterPick)).Distinct().Count() == 1;
                var maxNumberOfGames = publishersInLeagueForYear.Max(x => x.PublisherGames.Count(y => !y.CounterPick));
                var roundNumber = maxNumberOfGames;
                if (allPlayersHaveSameNumberOfGames)
                {
                    roundNumber++;
                }

                bool roundNumberIsOdd = (roundNumber % 2 != 0);
                if (roundNumberIsOdd)
                {
                    var sortedPublishersOdd = publishersWithLowestNumberOfGames.OrderBy(x => x.DraftPosition);
                    var firstPublisherOdd = sortedPublishersOdd.First();
                    return firstPublisherOdd;
                }
                //Else round is even
                var sortedPublishersEven = publishersWithLowestNumberOfGames.OrderByDescending(x => x.DraftPosition);
                var firstPublisherEven = sortedPublishersEven.First();
                return firstPublisherEven;
            }
            if (phase.Equals(DraftPhase.CounterPicks))
            {
                var publishersWithLowestNumberOfGames = publishersInLeagueForYear.MinBy(x => x.PublisherGames.Count(y => y.CounterPick));
                var allPlayersHaveSameNumberOfGames = publishersInLeagueForYear.Select(x => x.PublisherGames.Count(y => y.CounterPick)).Distinct().Count() == 1;
                var maxNumberOfGames = publishersInLeagueForYear.Max(x => x.PublisherGames.Count(y => y.CounterPick));

                var roundNumber = maxNumberOfGames;
                if (allPlayersHaveSameNumberOfGames)
                {
                    roundNumber++;
                }

                bool roundNumberIsOdd = (roundNumber % 2 != 0);
                if (roundNumberIsOdd)
                {
                    var sortedPublishersOdd = publishersWithLowestNumberOfGames.OrderByDescending(x => x.DraftPosition);
                    var firstPublisherOdd = sortedPublishersOdd.First();
                    return firstPublisherOdd;
                }
                //Else round is even
                var sortedPublishersEven = publishersWithLowestNumberOfGames.OrderBy(x => x.DraftPosition);
                var firstPublisherEven = sortedPublishersEven.First();
                return firstPublisherEven;
            }

            return Maybe<Publisher>.None;
        }

        public async Task<DraftPhase> GetDraftPhase(LeagueYear leagueYear)
        {
            IReadOnlyList<Publisher> publishers = await _publisherService.GetPublishersInLeagueForYear(leagueYear.League, leagueYear.Year);
            return GetDraftPhase(leagueYear, publishers);
        }

        private DraftPhase GetDraftPhase(LeagueYear leagueYear, IReadOnlyList<Publisher> publishers)
        {
            int numberOfStandardGamesToDraft = leagueYear.Options.GamesToDraft * publishers.Count;
            int standardGamesDrafted = publishers.SelectMany(x => x.PublisherGames).Count(x => !x.CounterPick);
            if (standardGamesDrafted < numberOfStandardGamesToDraft)
            {
                return DraftPhase.StandardGames;
            }

            int numberOfCounterPicksToDraft = leagueYear.Options.CounterPicks * publishers.Count;
            int counterPicksDrafted = publishers.SelectMany(x => x.PublisherGames).Count(x => x.CounterPick);
            if (counterPicksDrafted < numberOfCounterPicksToDraft)
            {
                return DraftPhase.CounterPicks;
            }

            return DraftPhase.Complete;
        }

        public IReadOnlyList<PublisherGame> GetAvailableCounterPicks(LeagueYear leagueYear, Publisher nextDraftingPublisher, IReadOnlyList<Publisher> publishersInLeagueForYear)
        {
            IReadOnlyList<Publisher> otherPublishers = publishersInLeagueForYear.Where(x => x.PublisherID != nextDraftingPublisher.PublisherID).ToList();

            IReadOnlyList<PublisherGame> gamesForYear = publishersInLeagueForYear.SelectMany(x => x.PublisherGames).ToList();
            IReadOnlyList<PublisherGame> otherPlayersGames = otherPublishers.SelectMany(x => x.PublisherGames).Where(x => !x.CounterPick).ToList();

            var alreadyCounterPicked = gamesForYear.Where(x => x.CounterPick).ToList();
            List<PublisherGame> availableCounterPicks = new List<PublisherGame>();
            foreach (var otherPlayerGame in otherPlayersGames)
            {
                bool playerHasCounterPick = alreadyCounterPicked.ContainsGame(otherPlayerGame);
                if (playerHasCounterPick)
                {
                    continue;
                }

                availableCounterPicks.Add(otherPlayerGame);
            }

            return availableCounterPicks;
        }

        public async Task<bool> CompleteDraft(LeagueYear leagueYear)
        {
            IReadOnlyList<Publisher> publishers = await _publisherService.GetPublishersInLeagueForYear(leagueYear.League, leagueYear.Year);

            int numberOfStandardGamesToDraft = leagueYear.Options.GamesToDraft * publishers.Count;
            int standardGamesDrafted = publishers.SelectMany(x => x.PublisherGames).Count(x => !x.CounterPick);
            if (standardGamesDrafted < numberOfStandardGamesToDraft)
            {
                return false;
            }

            int numberOfCounterPicksToDraft = leagueYear.Options.CounterPicks * publishers.Count;
            int counterPicksDrafted = publishers.SelectMany(x => x.PublisherGames).Count(x => x.CounterPick);
            if (counterPicksDrafted < numberOfCounterPicksToDraft)
            {
                return false;
            }

            await _fantasyCriticRepo.CompleteDraft(leagueYear);
            return true;
        }
    }
}
