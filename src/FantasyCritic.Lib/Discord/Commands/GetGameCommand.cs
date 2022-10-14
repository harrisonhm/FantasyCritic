using Discord;
using FantasyCritic.Lib.Extensions;
using FantasyCritic.Lib.Interfaces;
using FantasyCritic.Lib.Services;
using Discord.Interactions;
using FantasyCritic.Lib.Discord.Interfaces;
using FantasyCritic.Lib.Discord.Models;
using FantasyCritic.Lib.Discord.UrlBuilders;

namespace FantasyCritic.Lib.Discord.Commands;
public class GetGameCommand : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDiscordRepo _discordRepo;
    private readonly IClock _clock;
    private readonly IDiscordParameterParser _parameterParser;
    private readonly GameSearchingService _gameSearchingService;
    private readonly IDiscordFormatter _discordFormatter;
    private readonly string _baseAddress;

    public GetGameCommand(IDiscordRepo discordRepo,
        IClock clock,
        IDiscordParameterParser parameterParser,
        GameSearchingService gameSearchingService,
        IDiscordFormatter discordFormatter,
        FantasyCriticSettings fantasyCriticSettings
        )
    {
        _discordRepo = discordRepo;
        _clock = clock;
        _parameterParser = parameterParser;
        _gameSearchingService = gameSearchingService;
        _discordFormatter = discordFormatter;
        _baseAddress = fantasyCriticSettings.BaseAddress;
    }

    [SlashCommand("game", "Get game information. You can search with just a portion of the name.")]
    public async Task GetGame(
        [Summary("game_name", "The game name that you're searching for. You can input only a portion of the name.")] string gameName,
        [Summary("year", "The year for the league (if not entered, defaults to the current year).")] int? year = null)
    {
        var dateToCheck = _parameterParser.GetDateFromProvidedYear(year) ?? _clock.GetToday();

        var leagueChannel = await _discordRepo.GetLeagueChannel(Context.Channel.Id.ToString(), dateToCheck.Year);
        if (leagueChannel == null)
        {
            await RespondAsync(embed: _discordFormatter.BuildErrorEmbed(
                "Error Finding Game",
                "No league configuration found for this channel.",
                Context.User));
            return;
        }

        var leagueYear = leagueChannel.LeagueYear;

        var termToSearch = gameName.ToLower().Trim();

        if (termToSearch.Length < 2)
        {
            await RespondAsync(embed: _discordFormatter.BuildErrorEmbed(
                "Error Finding Game",
                "Please provide at least 3 characters to search with.",
                Context.User));
            return;
        }

        // TODO: remove accented characters from strings, Pokemon for example

        var matchingGames = await _gameSearchingService.SearchGamesWithLeaguePriority(termToSearch, leagueYear, 3);
        if (!matchingGames.Any())
        {
            await RespondAsync(embed: _discordFormatter.BuildErrorEmbed(
                "Error Finding Game",
                "No games found! Please check your search and try again.",
                Context.User));
            return;
        }

        var gamesToDisplay = matchingGames
            .Select(game => new MatchedGameDisplay(game)
            {
                PublisherWhoPicked = FindPublisherWithGame(leagueYear, game, false),
                PublisherWhoCounterPicked = FindPublisherWithGame(leagueYear, game, true)
            }).ToList();

        var gameEmbeds = gamesToDisplay
            .Select(matchedGameDisplay =>
            {
                var masterGameYear = matchedGameDisplay.GameFound;
                return new EmbedFieldBuilder
                {
                    Name = masterGameYear.MasterGame.GameName,
                    Value = BuildGameDisplayText(matchedGameDisplay, leagueChannel.LeagueYear, dateToCheck),
                    IsInline = false
                };
            }).ToList();

        await RespondAsync(embed: _discordFormatter.BuildRegularEmbed(
            gameEmbeds.Count == 0
                ? "No Games Found"
                : $"{gameEmbeds.Count} Game{(gameEmbeds.Count > 1 ? "(s)" : "")} Found",
            "",
            Context.User,
            gameEmbeds));
    }

    private string BuildGameDisplayText(MatchedGameDisplay matchedGameDisplay, LeagueYear leagueYear, LocalDate dateToCheck)
    {
        var gameFound = matchedGameDisplay.GameFound;

        var releaseDate = gameFound.MasterGame.ReleaseDate?.ToString() ??
                          $"{gameFound.MasterGame.EstimatedReleaseDate} (est)";
        var gameDisplayText =
            $"**Release Date:** {releaseDate}";

        var publisherWhoPicked = matchedGameDisplay.PublisherWhoPicked;
        if (publisherWhoPicked != null)
        {
            var score = gameFound.GetFantasyPoints(leagueYear.Options.ScoringSystem, false, dateToCheck);
            gameDisplayText +=
                $"\n**Picked:** {publisherWhoPicked.PublisherName} ({publisherWhoPicked.User.UserName})";

            if (!gameFound.WillRelease())
            {
                gameDisplayText += " (Will Not Release This Year)";
            }
            else if (score.HasValue)
            {
                gameDisplayText += $" ({score.Value})";
            }
        }

        var publisherWhoCounterPicked = matchedGameDisplay.PublisherWhoCounterPicked;
        if (publisherWhoCounterPicked != null)
        {
            var score = gameFound.GetFantasyPoints(leagueYear.Options.ScoringSystem, true, dateToCheck);
            gameDisplayText +=
                $"\n**Counter Picked:** {publisherWhoCounterPicked.PublisherName} ({publisherWhoCounterPicked.User.UserName})";

            if (score.HasValue)
            {
                gameDisplayText += $" ({score.Value})";
            }
        }

        var gameUrlBuilder = new GameUrlBuilder(_baseAddress, gameFound.MasterGame.MasterGameID);
        gameDisplayText += $"\n{gameUrlBuilder.BuildUrl("View Game")}";

        return gameDisplayText;
    }

    private Publisher? FindPublisherWithGame(LeagueYear leagueYear, MasterGameYear game, bool lookingForCounterPick)
    {
        return leagueYear.Publishers.FirstOrDefault(p =>
            p.PublisherGames.Any(publisherGame =>
                publisherGame.MasterGame?.MasterGame.MasterGameID == game.MasterGame.MasterGameID
                && publisherGame.CounterPick == lookingForCounterPick));
    }
}