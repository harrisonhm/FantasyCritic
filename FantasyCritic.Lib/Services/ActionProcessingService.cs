﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FantasyCritic.Lib.Domain;
using FantasyCritic.Lib.Domain.LeagueActions;
using FantasyCritic.Lib.Domain.Requests;
using FantasyCritic.Lib.Extensions;
using FantasyCritic.Lib.Utilities;
using MoreLinq;
using NodaTime;

namespace FantasyCritic.Lib.Services
{
    public class ActionProcessingService
    {
        private readonly GameAcquisitionService _gameAcquisitionService;
        private readonly IClock _clock;

        public ActionProcessingService(GameAcquisitionService gameAcquisitionService, IClock clock)
        {
            _gameAcquisitionService = gameAcquisitionService;
            _clock = clock;
        }

        public ActionProcessingResults ProcessActions(SystemWideValues systemWideValues, IReadOnlyDictionary<LeagueYear, IReadOnlyList<PickupBid>> allActiveBids, 
            IReadOnlyDictionary<LeagueYear, IReadOnlyList<DropRequest>> allActiveDrops, IEnumerable<Publisher> currentPublisherStates, IClock clock)
        {
            if (!allActiveBids.Any() && !allActiveDrops.Any())
            {
                return ActionProcessingResults.GetEmptyResultsSet(currentPublisherStates);
            }

            ActionProcessingResults dropResults = ProcessDrops(allActiveDrops, currentPublisherStates, clock);
            if (!allActiveBids.Any())
            {
                return dropResults;
            }

            ActionProcessingResults bidResults = ProcessPickupsIteration(systemWideValues, allActiveBids, dropResults, clock);
            return bidResults;
        }

        private ActionProcessingResults ProcessDrops(IReadOnlyDictionary<LeagueYear, IReadOnlyList<DropRequest>> allDropRequests, 
            IEnumerable<Publisher> currentPublisherStates, IClock clock)
        {
            List<Publisher> updatedPublisherStates = currentPublisherStates.ToList();
            List<PublisherGame> gamesToDelete = new List<PublisherGame>();
            List<LeagueAction> leagueActions = new List<LeagueAction>();
            List<DropRequest> successDrops = new List<DropRequest>();
            List<DropRequest> failedDrops = new List<DropRequest>();

            foreach (var leagueYearGroup in allDropRequests)
            {
                foreach (var dropRequest in leagueYearGroup.Value)
                {
                    var affectedPublisher = updatedPublisherStates.Single(x => x.PublisherID == dropRequest.Publisher.PublisherID);
                    var publishersInLeague = updatedPublisherStates.Where(x => x.LeagueYear.Equals(affectedPublisher.LeagueYear));
                    var otherPublishersInLeague = publishersInLeague.Except(new List<Publisher>() { affectedPublisher });

                    var dropResult = _gameAcquisitionService.CanDropGame(dropRequest, leagueYearGroup.Key, affectedPublisher, otherPublishersInLeague);
                    if (dropResult.Result.IsSuccess)
                    {
                        successDrops.Add(dropRequest);
                        var publisherGame = dropRequest.Publisher.GetPublisherGame(dropRequest.MasterGame);
                        gamesToDelete.Add(publisherGame.Value);
                        LeagueAction leagueAction = new LeagueAction(dropRequest, dropResult, clock.GetCurrentInstant());
                        affectedPublisher.DropGame(publisherGame.Value);

                        leagueActions.Add(leagueAction);
                    }
                    else
                    {
                        failedDrops.Add(dropRequest);
                        LeagueAction leagueAction = new LeagueAction(dropRequest, dropResult, clock.GetCurrentInstant());
                        leagueActions.Add(leagueAction);
                    }
                }
            }

            ActionProcessingResults dropProcessingResults = ActionProcessingResults.GetResultsSetFromDropResults(successDrops, failedDrops, leagueActions, updatedPublisherStates, gamesToDelete);
            return dropProcessingResults;
        }

        private ActionProcessingResults ProcessPickupsIteration(SystemWideValues systemWideValues, IReadOnlyDictionary<LeagueYear, IReadOnlyList<PickupBid>> allActiveBids,
            ActionProcessingResults existingResults, IClock clock)
        {
            IEnumerable<PickupBid> flatAllBids = allActiveBids.SelectMany(x => x.Value);

            var currentPublisherStates = existingResults.UpdatedPublishers;
            var processedBids = new ProcessedBidSet();
            foreach (var leagueYear in allActiveBids)
            {
                if (!leagueYear.Value.Any())
                {
                    continue;
                }

                var processedBidsForLeagueYear = ProcessPickupsForLeagueYear(leagueYear.Key, leagueYear.Value, currentPublisherStates, systemWideValues);
                processedBids = processedBids.AppendSet(processedBidsForLeagueYear);
            }

            ActionProcessingResults bidResults = GetBidProcessingResults(processedBids.SuccessBids, processedBids.FailedBids, currentPublisherStates, clock);
            var newResults = existingResults.Combine(bidResults);
            var remainingBids = flatAllBids.Except(processedBids.ProcessedBids);
            if (remainingBids.Any())
            {
                IReadOnlyDictionary<LeagueYear, IReadOnlyList<PickupBid>> remainingBidDictionary = remainingBids.GroupToDictionary(x => x.LeagueYear);
                var subProcessingResults = ProcessPickupsIteration(systemWideValues, remainingBidDictionary, newResults, clock);
                ActionProcessingResults combinedResults = newResults.Combine(subProcessingResults);
                return combinedResults;
            }

            return newResults;
        }

        private ProcessedBidSet ProcessPickupsForLeagueYear(LeagueYear leagueYear, IReadOnlyList<PickupBid> activeBidsForLeague,
            IReadOnlyList<Publisher> currentPublisherStates, SystemWideValues systemWideValues)
        {
            List<PickupBid> noSpaceLeftBids = new List<PickupBid>();
            List<PickupBid> insufficientFundsBids = new List<PickupBid>();
            List<PickupBid> belowMinimumBids = new List<PickupBid>();
            List<KeyValuePair<PickupBid, string>> invalidGameBids = new List<KeyValuePair<PickupBid, string>>();

            foreach (var activeBid in activeBidsForLeague)
            {
                Publisher publisher = currentPublisherStates.Single(x => x.PublisherID == activeBid.Publisher.PublisherID);

                var gameRequest = new ClaimGameDomainRequest(publisher, activeBid.MasterGame.GameName, false, false, false, activeBid.MasterGame, null, null);
                var publishersForLeagueAndYear = currentPublisherStates.Where(x => x.LeagueYear.League.LeagueID == leagueYear.League.LeagueID && x.LeagueYear.Year == leagueYear.Year);

                var hasValidConditionalDrop = false;
                if (activeBid.ConditionalDropPublisherGame.HasValue)
                {
                    var otherPublishersInLeague = publishersForLeagueAndYear.Except(new List<Publisher>() { publisher });
                    activeBid.ConditionalDropResult = _gameAcquisitionService.CanConditionallyDropGame(activeBid, leagueYear, publisher, otherPublishersInLeague);
                    hasValidConditionalDrop = activeBid.ConditionalDropResult.Result.IsSuccess;
                }

                var allowIfFull = activeBid.ConditionalDropPublisherGame.HasValue && activeBid.ConditionalDropResult.Result.IsSuccess;
                var claimResult = _gameAcquisitionService.CanClaimGame(gameRequest, leagueYear, publishersForLeagueAndYear, null, allowIfFull);

                if (!publisher.HasRemainingGameSpot(leagueYear.Options.StandardGames) && !hasValidConditionalDrop)
                {
                    noSpaceLeftBids.Add(activeBid);
                    continue;
                }

                if (!claimResult.Success)
                {
                    invalidGameBids.Add(new KeyValuePair<PickupBid, string>(activeBid, string.Join(" AND ", claimResult.Errors.Select(x => x.Error))));
                    continue;
                }

                if (activeBid.BidAmount > publisher.Budget)
                {
                    insufficientFundsBids.Add(activeBid);
                }

                if (activeBid.BidAmount < leagueYear.Options.MinimumBidAmount)
                {
                    belowMinimumBids.Add(activeBid);
                }
            }

            var validBids = activeBidsForLeague
                .Except(noSpaceLeftBids)
                .Except(insufficientFundsBids)
                .Except(belowMinimumBids)
                .Except(invalidGameBids.Select(x => x.Key))
                .ToList();
            var winnableBids = GetWinnableBids(validBids, leagueYear.Options, systemWideValues);
            var winningBids = GetWinningBids(winnableBids);

            var takenGames = winningBids.Select(x => x.MasterGame);
            var losingBids = activeBidsForLeague
                .Except(winningBids)
                .Except(noSpaceLeftBids)
                .Except(insufficientFundsBids)
                .Except(belowMinimumBids)
                .Except(invalidGameBids.Select(x => x.Key))
                .Where(x => takenGames.Contains(x.MasterGame))
                .Select(x => new FailedPickupBid(x, "Publisher was outbid."));

            var invalidGameBidFailures = invalidGameBids.Select(x => new FailedPickupBid(x.Key, "Game is no longer eligible: " + x.Value));
            var insufficientFundsBidFailures = insufficientFundsBids.Select(x => new FailedPickupBid(x, "Not enough budget."));
            var belowMinimumBidFailures = belowMinimumBids.Select(x => new FailedPickupBid(x, "Bid is below the minimum bid amount."));
            List<FailedPickupBid> noSpaceLeftBidFailures = new List<FailedPickupBid>();
            foreach (var noSpaceLeftBid in noSpaceLeftBids)
            {
                FailedPickupBid failedBid;
                if (noSpaceLeftBid.ConditionalDropPublisherGame.HasValue && noSpaceLeftBid.ConditionalDropResult.Result.IsFailure)
                {
                    failedBid = new FailedPickupBid(noSpaceLeftBid, "No roster spots available. Attempted to conditionally drop game: " +
                                                                    $"{noSpaceLeftBid.ConditionalDropPublisherGame.Value.MasterGame.Value.MasterGame.GameName} but failed because: {noSpaceLeftBid.ConditionalDropResult.Result.Error}");
                }
                else
                {
                    failedBid = new FailedPickupBid(noSpaceLeftBid, "No roster spots available.");
                }
                noSpaceLeftBidFailures.Add(failedBid);
            }

            var failedBids = losingBids
                .Concat(insufficientFundsBidFailures)
                .Concat(belowMinimumBidFailures)
                .Concat(noSpaceLeftBidFailures)
                .Concat(invalidGameBidFailures);

            var processedSet = new ProcessedBidSet(winningBids, failedBids);
            return processedSet;
        }

        private IReadOnlyList<PickupBid> GetWinnableBids(IReadOnlyList<PickupBid> activeBidsForLeagueYear, LeagueOptions options, SystemWideValues systemWideValues)
        {
            List<PickupBid> winnableBids = new List<PickupBid>();

            var enoughBudgetBids = activeBidsForLeagueYear.Where(x => x.BidAmount <= x.Publisher.Budget);
            var groupedByGame = enoughBudgetBids.GroupBy(x => x.MasterGame);
            var currentDate = _clock.GetToday();
            foreach (var gameGroup in groupedByGame)
            {
                PickupBid bestBid;
                if (gameGroup.Count() == 1)
                {
                    bestBid = gameGroup.First();
                }
                else
                {
                    var bestBids = gameGroup.MaxBy(x => x.BidAmount);
                    var bestBidsByProjectedScore = bestBids.MinBy(x => x.Publisher.GetProjectedFantasyPoints(options, systemWideValues, false, false, currentDate));
                    bestBid = bestBidsByProjectedScore.OrderBy(x => x.Timestamp).ThenByDescending(x => x.Publisher.DraftPosition).First();
                }

                winnableBids.Add(bestBid);
            }

            return winnableBids;
        }

        private static IReadOnlyList<PickupBid> GetWinningBids(IReadOnlyList<PickupBid> winnableBids)
        {
            List<PickupBid> winningBids = new List<PickupBid>();
            var groupedByPublisher = winnableBids.GroupBy(x => x.Publisher);
            foreach (var publisherGroup in groupedByPublisher)
            {
                PickupBid winningBid = publisherGroup.MinBy(x => x.Priority).First();
                winningBids.Add(winningBid);
            }

            return winningBids;
        }

        private static ActionProcessingResults GetBidProcessingResults(IReadOnlyList<PickupBid> successBids, IReadOnlyList<FailedPickupBid> failedBids, IReadOnlyList<Publisher> publishers, IClock clock)
        {
            Dictionary<Guid, Publisher> publisherDictionary = publishers.ToDictionary(x => x.PublisherID);
            List<PublisherGame> gamesToAdd = new List<PublisherGame>();
            List<LeagueAction> leagueActions = new List<LeagueAction>();
            foreach (var successBid in successBids)
            {
                PublisherGame newPublisherGame = new PublisherGame(successBid.Publisher.PublisherID, Guid.NewGuid(), successBid.MasterGame.GameName, clock.GetCurrentInstant(),
                    false, null, false, null, new MasterGameYear(successBid.MasterGame, successBid.Publisher.LeagueYear.Year), null, null, false);
                gamesToAdd.Add(newPublisherGame);
                var affectedPublisher = publisherDictionary[successBid.Publisher.PublisherID];
                affectedPublisher.AcquireGame(newPublisherGame, successBid.BidAmount);

                LeagueAction leagueAction = new LeagueAction(successBid, clock.GetCurrentInstant());
                leagueActions.Add(leagueAction);
            }

            foreach (var failedBid in failedBids)
            {
                LeagueAction leagueAction = new LeagueAction(failedBid, clock.GetCurrentInstant());
                leagueActions.Add(leagueAction);
            }

            var simpleFailedBids = failedBids.Select(x => x.PickupBid);

            List<PublisherGame> conditionalDroppedGames = new List<PublisherGame>();
            var successfulConditionalDrops = successBids.Where(x => x.ConditionalDropPublisherGame.HasValue && x.ConditionalDropResult.Result.IsSuccess);
            foreach (var successfulConditionalDrop in successfulConditionalDrops)
            {
                var affectedPublisher = publisherDictionary[successfulConditionalDrop.Publisher.PublisherID];
                affectedPublisher.DropGame(successfulConditionalDrop.ConditionalDropPublisherGame.Value);
                conditionalDroppedGames.Add(successfulConditionalDrop.ConditionalDropPublisherGame.Value);
            }

            var updatedPublishers = publisherDictionary.Values.ToList();
            ActionProcessingResults bidProcessingResults = ActionProcessingResults.GetResultsSetFromBidResults(successBids, simpleFailedBids, leagueActions, updatedPublishers, gamesToAdd, conditionalDroppedGames);
            return bidProcessingResults;
        }
    }
}
