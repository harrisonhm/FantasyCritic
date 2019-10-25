﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using FantasyCritic.Lib.Domain;
using FantasyCritic.Lib.Extensions;
using FantasyCritic.Lib.Interfaces;
using FantasyCritic.Lib.OpenCritic;
using FantasyCritic.Lib.Statistics;
using FantasyCritic.Lib.Utilities;
using Microsoft.Extensions.Logging;
using NodaTime;
using RDotNet;

namespace FantasyCritic.Lib.Services
{
    public class AdminService
    {
        private readonly IRDSManager _rdsManager;
        private readonly RoyaleService _royaleService;
        private readonly FantasyCriticService _fantasyCriticService;
        private readonly IFantasyCriticRepo _fantasyCriticRepo;
        private readonly IMasterGameRepo _masterGameRepo;
        private readonly InterLeagueService _interLeagueService;
        private readonly IOpenCriticService _openCriticService;
        private readonly IClock _clock;
        private readonly ILogger<OpenCriticService> _logger;

        public AdminService(FantasyCriticService fantasyCriticService, IFantasyCriticRepo fantasyCriticRepo, IMasterGameRepo masterGameRepo,
            InterLeagueService interLeagueService, IOpenCriticService openCriticService, IClock clock, ILogger<OpenCriticService> logger, IRDSManager rdsManager,
            RoyaleService royaleService)
        {
            _fantasyCriticService = fantasyCriticService;
            _fantasyCriticRepo = fantasyCriticRepo;
            _masterGameRepo = masterGameRepo;
            _interLeagueService = interLeagueService;
            _openCriticService = openCriticService;
            _clock = clock;
            _logger = logger;
            _rdsManager = rdsManager;
            _royaleService = royaleService;
        }

        public async Task FullDataRefresh()
        {
            await RefreshCriticInfo();

            await Task.Delay(1000);
            await RefreshCaches();
            await Task.Delay(1000);

            await UpdateFantasyPoints();
        }

        public async Task RefreshCriticInfo()
        {
            _logger.LogInformation("Refreshing critic scores");
            var supportedYears = await _interLeagueService.GetSupportedYears();
            var masterGames = await _interLeagueService.GetMasterGames();
            foreach (var masterGame in masterGames)
            {
                if (!masterGame.OpenCriticID.HasValue)
                {
                    continue;
                }

                if (masterGame.DoNotRefreshAnything)
                {
                    continue;
                }

                if (masterGame.IsReleased(_clock) && masterGame.ReleaseDate.HasValue)
                {
                    var year = masterGame.ReleaseDate.Value.Year;
                    var supportedYear = supportedYears.SingleOrDefault(x => x.Year == year);
                    if (supportedYear != null && supportedYear.Finished)
                    {
                        continue;
                    }
                }

                var openCriticGame = await _openCriticService.GetOpenCriticGame(masterGame.OpenCriticID.Value);
                if (openCriticGame.HasValue)
                {
                    await _interLeagueService.UpdateCriticStats(masterGame, openCriticGame.Value);
                }
                else
                {
                    _logger.LogWarning($"Getting an open critic game failed (empty return): {masterGame.GameName} | [{masterGame.OpenCriticID.Value}]");
                }

                foreach (var subGame in masterGame.SubGames)
                {
                    if (!subGame.OpenCriticID.HasValue)
                    {
                        continue;
                    }

                    var subGameOpenCriticGame = await _openCriticService.GetOpenCriticGame(subGame.OpenCriticID.Value);

                    if (subGameOpenCriticGame.HasValue)
                    {
                        await _interLeagueService.UpdateCriticStats(subGame, subGameOpenCriticGame.Value);
                    }
                }
            }

            _logger.LogInformation("Done refreshing critic scores");
        }

        public async Task UpdateFantasyPoints()
        {
            _logger.LogInformation("Updating fantasy points");

            var supportedYears = await _interLeagueService.GetSupportedYears();
            foreach (var supportedYear in supportedYears)
            {
                if (supportedYear.Finished || !supportedYear.OpenForPlay)
                {
                    continue;
                }

                await _fantasyCriticService.UpdateFantasyPoints(supportedYear.Year);
            }

            _logger.LogInformation("Done updating fantasy points");
            _logger.LogInformation("Updating royale fantasy points");

            var supportedQuarters = await _royaleService.GetYearQuarters();
            foreach (var supportedQuarter in supportedQuarters)
            {
                if (supportedQuarter.Finished || !supportedQuarter.OpenForPlay)
                {
                    continue;
                }

                await _royaleService.UpdateFantasyPoints(supportedQuarter.YearQuarter);
            }

            _logger.LogInformation("Done updating royale fantasy points");
        }

        public async Task RefreshCaches()
        {
            _logger.LogInformation("Refreshing caches");

            LocalDate tomorrow = _clock.GetToday().PlusDays(1);
            await _masterGameRepo.UpdateReleaseDateEstimates(tomorrow);

            await UpdateSystemWideValues();
            var hypeConstants = await GetHypeConstants();
            await UpdateHypeFactor(hypeConstants);
            _logger.LogInformation("Done refreshing caches");
        }

        public Task SnapshotDatabase()
        {
            Instant time = _clock.GetCurrentInstant();
            return _rdsManager.SnapshotRDS(time);
        }

        public Task<IReadOnlyList<DatabaseSnapshotInfo>> GetRecentDatabaseSnapshots()
        {
            return _rdsManager.GetRecentSnapshots();
        }

        private async Task UpdateSystemWideValues()
        {
            List<PublisherGame> allGamesWithPoints = new List<PublisherGame>();
            var supportedYears = await _interLeagueService.GetSupportedYears();
            foreach (var supportedYear in supportedYears)
            {
                var publishers = await _fantasyCriticRepo.GetAllPublishersForYear(supportedYear.Year);
                var publisherGames = publishers.SelectMany(x => x.PublisherGames);
                var gamesWithPoints = publisherGames.Where(x => x.FantasyPoints.HasValue).ToList();
                allGamesWithPoints.AddRange(gamesWithPoints);
            }

            var averageStandardPoints = allGamesWithPoints.Where(x => !x.CounterPick).Average(x => x.FantasyPoints.Value);
            var averageCounterPickPoints = allGamesWithPoints.Where(x => x.CounterPick).Average(x => x.FantasyPoints.Value);

            var systemWideValues = new SystemWideValues(averageStandardPoints, averageCounterPickPoints);
            await _interLeagueService.UpdateSystemWideValues(systemWideValues);
        }

        private async Task<HypeConstants> GetHypeConstants()
        {
            _logger.LogInformation("Getting Hype Constants");
            REngine.SetEnvironmentVariables();
            var engine = REngine.GetInstance();

            string rscript = Resource.MasterGameStatisticsScript;

            var masterGames = await _masterGameRepo.GetMasterGameYears(2019, true);

            var gamesToOutput = masterGames
                .Where(x => x.Year == 2019)
                .Where(x => x.DateAdjustedHypeFactor > 0)
                .Where(x => !x.WillRelease() || x.MasterGame.CriticScore.HasValue);

            var outputModels = gamesToOutput.Select(x => new MasterGameYearRInput(x));

            string fileName = Guid.NewGuid() + ".csv";
            using (var writer = new StreamWriter(fileName))
            using (var csv = new CsvWriter(writer))
            {
                csv.WriteRecords(outputModels);
            }

            var args_r = new string[1] { fileName };
            engine.SetCommandLineArguments(args_r);

            CharacterVector vector = engine.Evaluate(rscript).AsCharacter();
            string result = vector[0];
            var splitString = result.Split(' ');

            File.Delete(fileName);

            double baseScore = double.Parse(splitString[2]);
            double standardGameConstant = double.Parse(splitString[4]);
            double counterPickConstant = double.Parse(splitString[8]);
            double hypeFactorConstant = double.Parse(splitString[12]);
            double averageDraftPositionConstant = double.Parse(splitString[16]);
            double totalBidAmountConstant = double.Parse(splitString[20]);
            double bidPercentileConstant = double.Parse(splitString[24]);

            HypeConstants hypeConstants = new HypeConstants(baseScore, standardGameConstant, counterPickConstant,
                hypeFactorConstant, averageDraftPositionConstant, totalBidAmountConstant, bidPercentileConstant);

            _logger.LogInformation($"Hype Constants: {hypeConstants}");

            return hypeConstants;
        }

        private async Task UpdateHypeFactor(HypeConstants hypeConstants)
        {
            var supportedYears = await _interLeagueService.GetSupportedYears();
            foreach (var supportedYear in supportedYears)
            {
                if (supportedYear.Finished)
                {
                    continue;
                }

                List<MasterGameHypeScores> hypeScores = new List<MasterGameHypeScores>();
                var cleanMasterGames = await _masterGameRepo.GetMasterGameYears(supportedYear.Year, false);
                var cachedMasterGames = await _masterGameRepo.GetMasterGameYears(supportedYear.Year, true);

                var masterGameCacheLookup = cachedMasterGames.ToDictionary(x => x.MasterGame.MasterGameID, y => y);

                foreach (var masterGame in cleanMasterGames)
                {
                    if (masterGame.MasterGame.CriticScore.HasValue)
                    {
                        var cachedMasterGame = masterGameCacheLookup[masterGame.MasterGame.MasterGameID];
                        hypeScores.Add(new MasterGameHypeScores(masterGame, cachedMasterGame.HypeFactor, cachedMasterGame.DateAdjustedHypeFactor, cachedMasterGame.LinearRegressionHypeFactor));
                        continue;
                    }

                    double notNullAverageDraftPosition = masterGame.AverageDraftPosition ?? 0;

                    double hypeFactor = (101 - notNullAverageDraftPosition) * masterGame.PercentStandardGame;
                    double dateAdjustedHypeFactor = (101 - notNullAverageDraftPosition) * masterGame.EligiblePercentStandardGame;

                    double standardGameCalculation = masterGame.EligiblePercentStandardGame * hypeConstants.StandardGameConstant;
                    double counterPickCalculation = masterGame.EligiblePercentCounterPick * hypeConstants.CounterPickConstant;
                    double hypeFactorCalculation = dateAdjustedHypeFactor * hypeConstants.HypeFactorConstant;
                    double averageDraftPositionCalculation = notNullAverageDraftPosition * hypeConstants.AverageDraftPositionConstant;
                    double totalBidCalculation = masterGame.TotalBidAmount * hypeConstants.TotalBidAmountConstant;
                    double bidPercentileCalculation = masterGame.BidPercentile * hypeConstants.BidPercentileConstant;

                    double linearRegressionHypeFactor = hypeConstants.BaseScore 
                                                        + standardGameCalculation 
                                                        + counterPickCalculation 
                                                        + hypeFactorCalculation 
                                                        + averageDraftPositionCalculation 
                                                        + totalBidCalculation 
                                                        + bidPercentileCalculation;

                    hypeScores.Add(new MasterGameHypeScores(masterGame, hypeFactor, dateAdjustedHypeFactor, linearRegressionHypeFactor));
                }

                await _masterGameRepo.UpdateHypeFactors(hypeScores, supportedYear.Year);
            }
        }
    }
}
