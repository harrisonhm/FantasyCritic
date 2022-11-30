using Discord;
using Discord.WebSocket;
using DiscordDotNetUtilities;
using DiscordDotNetUtilities.Interfaces;
using FantasyCritic.Lib.DependencyInjection;
using FantasyCritic.Lib.Discord.UrlBuilders;
using FantasyCritic.Lib.Domain.Combinations;
using FantasyCritic.Lib.Domain.LeagueActions;
using FantasyCritic.Lib.Domain.Trades;
using FantasyCritic.Lib.Extensions;
using FantasyCritic.Lib.Interfaces;

namespace FantasyCritic.Lib.Discord;
public class DiscordPushService
{
    private const int MaxAttempts = 4;
    private const int MaxMessageLength = 2000;
    private readonly string _botToken;
    private readonly IDiscordRepo _discordRepo;
    private readonly IDiscordSupplementalDataRepo _supplementalDataRepo;
    private readonly IDiscordFormatter _discordFormatter;
    private readonly DiscordSocketClient _client;
    private bool _botIsReady;
    private readonly bool _enabled;
    private readonly string _baseAddress;

    public DiscordPushService(
        FantasyCriticDiscordConfiguration configuration,
        IDiscordRepo discordRepo,
        IDiscordSupplementalDataRepo supplementalDataRepo,
        IDiscordFormatter discordFormatter)
    {
        _enabled = !string.IsNullOrEmpty(configuration.BotToken) && configuration.BotToken != "secret";
        _botToken = configuration.BotToken;
        _baseAddress = configuration.BaseAddress;
        _discordRepo = discordRepo;
        _supplementalDataRepo = supplementalDataRepo;
        _discordFormatter = discordFormatter;
        DiscordSocketConfig socketConfig = new()
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
            AlwaysDownloadUsers = true,
        };
        _client = new DiscordSocketClient(socketConfig);
        _botIsReady = false;
    }

    private async Task<bool> StartBot()
    {
        if (!_enabled)
        {
            return false;
        }

        if (_botIsReady)
        {
            return true;
        }
        _client.Ready += Client_Ready;
        _client.Log += Log;

        await _client.LoginAsync(TokenType.Bot, _botToken);
        await _client.StartAsync();

        var attempts = 0;
        while (!_botIsReady)
        {
            if (attempts > MaxAttempts)
            {
                break;
            }

            await Task.Delay(1000);
            attempts++;
        }

        if (!_botIsReady)
        {
            Serilog.Log.Warning("Discord bot is not ready, cannot send message.");
            return false;
        }

        return true;
    }

    public async Task SendGameCriticScoreUpdateMessage(MasterGame game, decimal? oldCriticScore, decimal? newCriticScore)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var allChannels = await _discordRepo.GetAllLeagueChannels();
        var newsEnabledChannels = allChannels.Where(x => x.GameNewsSetting != DiscordGameNewsSetting.Off).ToList();

        var messageToSend = "";

        var newCriticScoreRounded = newCriticScore != null ? (decimal?)Math.Round(newCriticScore.Value, 1) : null;
        var oldCriticScoreRounded = oldCriticScore != null ? (decimal?)Math.Round(oldCriticScore.Value, 1) : null;

        if (newCriticScoreRounded == null)
        {
            return;
        }
        if (oldCriticScoreRounded == null)
        {
            messageToSend = $"**{game.GameName}** now has a critic score of **{newCriticScoreRounded}**";
        }
        else
        {
            var scoreDiff = oldCriticScoreRounded.Value - newCriticScoreRounded.Value;
            if (scoreDiff != 0 && Math.Abs(scoreDiff) >= 1)
            {
                var direction = scoreDiff < 0 ? "UP" : "DOWN";
                messageToSend =
                    $"The critic score for **{game.GameName}** has gone **{direction}** from **{oldCriticScoreRounded}** to **{newCriticScoreRounded}**";
            }
        }

        if (!string.IsNullOrEmpty(messageToSend))
        {
            foreach (var leagueChannel in newsEnabledChannels)
            {
                var guild = _client.GetGuild(leagueChannel.GuildID);
                var channel = guild?.GetChannel(leagueChannel.ChannelID);
                if (channel is not SocketTextChannel textChannel)
                {
                    continue;
                }
                await textChannel.TrySendMessageAsync(messageToSend);
            }
        }
    }

    public async Task SendMasterGameEditMessage(MasterGameYear existingGame, MasterGameYear editedGame, IReadOnlyList<string> changes)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        bool releaseStatusChanged = existingGame.WillRelease() != editedGame.WillRelease();

        var leaguesWithGame = await _supplementalDataRepo.GetLeaguesWithOrFormerlyWithGame(editedGame);
        var allChannels = await _discordRepo.GetAllLeagueChannels();
        var newsEnabledChannels = allChannels.Where(x => x.GameNewsSetting != DiscordGameNewsSetting.Off).ToList();
        foreach (var leagueChannel in newsEnabledChannels)
        {
            if (leagueChannel.GameNewsSetting == DiscordGameNewsSetting.Relevant)
            {
                var gameIsRelevant = GameIsRelevant(existingGame, releaseStatusChanged, leaguesWithGame, leagueChannel);
                if (!gameIsRelevant)
                {
                    continue;
                }
            }

            var guild = _client.GetGuild(leagueChannel.GuildID);
            var channel = guild?.GetChannel(leagueChannel.ChannelID);
            if (channel is not SocketTextChannel textChannel)
            {
                continue;
            }

            var editableChanges = changes.ToList();
            if (releaseStatusChanged)
            {
                if (editedGame.WillRelease())
                {
                    editableChanges.Add($"**{editedGame.MasterGame.GameName}** will release this year!");
                }
                else
                {
                    editableChanges.Add($"**{editedGame.MasterGame.GameName}** will NOT release this year!");
                }
            }

            var changesMessage = string.Join("\n", editableChanges);
            await textChannel.TrySendMessageAsync($"Game Update: **{editedGame.MasterGame.GameName}**\n{changesMessage}");
        }
    }

    public async Task SendGameReleaseUpdates(IEnumerable<MasterGameYear> masterGamesReleasingToday, int year)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var leagueHasGameLookup = await _supplementalDataRepo.GetLeaguesWithOrFormerlyWithGames(masterGamesReleasingToday, year);
        var allChannels = await _discordRepo.GetAllLeagueChannels();
        var newsEnabledChannels = allChannels.Where(x => x.GameNewsSetting != DiscordGameNewsSetting.Off).ToList();
        foreach (var leagueChannel in newsEnabledChannels)
        {
            var guild = _client.GetGuild(leagueChannel.GuildID);
            var channel = guild?.GetChannel(leagueChannel.ChannelID);
            if (channel is not SocketTextChannel textChannel)
            {
                continue;
            }

            IReadOnlyList<MasterGameYear> relevantGamesForLeague;
            if (leagueChannel.GameNewsSetting == DiscordGameNewsSetting.Relevant)
            {
                relevantGamesForLeague = masterGamesReleasingToday.Where(x => ReleasedGameIsRelevant(x, leagueHasGameLookup, leagueChannel)).ToList();
            }
            else
            {
                relevantGamesForLeague = masterGamesReleasingToday.ToList();
            }

            if (!relevantGamesForLeague.Any())
            {
                continue;
            }

            var releaseMessages = relevantGamesForLeague.Select(x => $"**{x.MasterGame.GameName}** has released!");
            var releaseMessage = string.Join("\n", releaseMessages);
            await textChannel.TrySendMessageAsync(releaseMessage);
        }
    }

    private static bool GameIsRelevant(MasterGameYear masterGameYear, bool releaseStatusChanged, IReadOnlySet<Guid> leaguesWithGame, MinimalLeagueChannel channel)
    {
        if (releaseStatusChanged)
        {
            return true;
        }

        return masterGameYear.IsRelevantInYear(true) || leaguesWithGame.Contains(channel.LeagueID);
    }

    private static bool ReleasedGameIsRelevant(MasterGameYear masterGameYear, ILookup<Guid, Guid> leagueHasGameLookup, MinimalLeagueChannel leagueChannel)
    {
        if (masterGameYear.IsRelevantInYear(true))
        {
            return true;
        }

        var gameIsOrWasInLeague = leagueHasGameLookup[leagueChannel.LeagueID].Contains(masterGameYear.MasterGame.MasterGameID);
        return gameIsOrWasInLeague;
    }

    public async Task SendLeagueActionMessage(LeagueAction action)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var leagueId = action.Publisher.LeagueYearKey.LeagueID;
        var leagueChannels = await _discordRepo.GetLeagueChannels(leagueId);

        if (leagueChannels is null)
        {
            return;
        }

        foreach (var leagueChannel in leagueChannels)
        {
            var guild = _client.GetGuild(leagueChannel.GuildID);
            SocketTextChannel? channel = guild?.GetTextChannel(leagueChannel.ChannelID);
            if (channel is null)
            {
                continue;
            }

            await channel.TrySendMessageAsync($"**{action.Publisher.GetPublisherAndUserDisplayName()}** {action.Description} (at {action.Timestamp.ToEasternDate()})");
        }
    }

    public async Task SendLeagueYearScoreUpdateMessage(LeagueYearScoreChanges scoreChanges)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var changeList = scoreChanges.GetScoreChanges();
        if (!changeList.AnyChanges)
        {
            return;
        }

        var channel = await GetChannelForLeague(scoreChanges.LeagueYear.League.LeagueID);
        if (channel is null)
        {
            return;
        }

        var embedFieldBuilders = new List<EmbedFieldBuilder>();

        foreach (var change in changeList.Changes.Where(c => c.RankChanged || c.ScoreChanged))
        {
            var changeText = "";
            if (change.ScoreChanged)
            {
                changeText = $"> Score has gone **{change.Direction}** from **{change.RoundedOldScore}** to **{change.RoundedNewScore}**";
            }
            if (change.RankChanged)
            {
                changeText += $"\n> Moved from **{change.FormattedOldRank}** place to **{change.FormattedNewRank}** place";
            }

            if (!string.IsNullOrEmpty(changeText))
            {
                embedFieldBuilders.Add(new EmbedFieldBuilder
                {
                    Name = change.Publisher.GetPublisherAndUserDisplayName(),
                    Value = changeText,
                    IsInline = false
                });
            }
        }

        if (embedFieldBuilders.Any())
        {
            await channel.TrySendMessageAsync(embed: _discordFormatter.BuildRegularEmbed(
            "Publisher Score Updates",
            "",
            null,
            embedFieldBuilders));
        }
    }

    public async Task SendPublisherNameUpdateMessage(Publisher publisher, string oldPublisherName, string newPublisherName)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var channel = await GetChannelForLeague(publisher.LeagueYearKey.LeagueID);
        if (channel is null)
        {
            return;
        }

        var messageToSend = $"Publisher **{oldPublisherName}** ({publisher.User.UserName}) is now known as **{newPublisherName}**";
        await channel.TrySendMessageAsync(messageToSend);
    }

    public async Task SendNewPublisherMessage(Publisher publisher)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var channel = await GetChannelForLeague(publisher.LeagueYearKey.LeagueID);
        if (channel is null)
        {
            return;
        }

        var messageToSend = $"Publisher **{publisher.GetPublisherAndUserDisplayName()}** has joined the league!";
        await channel.TrySendMessageAsync(messageToSend);
    }

    public async Task SendPublicBiddingSummary(IEnumerable<LeagueYearPublicBiddingSet> publicBiddingSets)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var allChannels = await _discordRepo.GetAllLeagueChannels();
        var channelLookup = allChannels.ToLookup(c => c.LeagueID);

        foreach (var publicBiddingSet in publicBiddingSets)
        {
            var leagueChannels = channelLookup[publicBiddingSet.LeagueYear.League.LeagueID].ToList();
            if (!leagueChannels.Any())
            {
                continue;
            }

            var gameMessages = new List<string>();
            foreach (var publicBid in publicBiddingSet.MasterGames)
            {
                var gameMessage = "";
                var releaseDate = publicBid.MasterGameYear.MasterGame.EstimatedReleaseDate;
                gameMessage += $"**{publicBid.MasterGameYear.MasterGame.GameName}**";

                if (publicBid.CounterPick)
                {
                    gameMessage += " (🎯 Counter Pick Bid)";
                }

                gameMessage += $"\n> Release Date: {releaseDate}";

                var roundedHypeFactor = Math.Round(publicBid.MasterGameYear.HypeFactor, 1);
                gameMessage += $"\n> Hype Factor: {roundedHypeFactor}\n";
                gameMessages.Add(gameMessage);
            }

            var leagueLink = new LeagueUrlBuilder(_baseAddress, publicBiddingSet.LeagueYear.League.LeagueID, publicBiddingSet.LeagueYear.Year).BuildUrl();
            var finalMessage = string.Join("\n", gameMessages);
            var lastSunday = GetLastSunday();
            var header = $"Public Bids (Week of {lastSunday:MMMM dd, yyyy})";

            foreach (var leagueChannel in leagueChannels)
            {
                var guild = _client.GetGuild(leagueChannel.GuildID);
                var channel = guild?.GetTextChannel(leagueChannel.ChannelID);
                if (channel is null)
                {
                    continue;
                }

                await channel.TrySendMessageAsync(embed: _discordFormatter.BuildRegularEmbed(
                    header,
                    finalMessage,
                    url: leagueLink));
            }
        }
    }

    private static DateTime GetLastSunday()
    {
        var currentDate = DateTime.Now;
        var currentDayOfWeek = (int)currentDate.DayOfWeek;
        var lastSundayDate = currentDate.AddDays(-currentDayOfWeek);
        return lastSundayDate;
    }

    public async Task SendActionProcessingSummary(IEnumerable<LeagueActionProcessingSet> leagueActionSets)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var allChannels = await _discordRepo.GetAllLeagueChannels();
        var channelLookup = allChannels.ToLookup(c => c.LeagueID);

        foreach (var leagueAction in leagueActionSets)
        {
            var leagueChannels = channelLookup[leagueAction.LeagueYear.League.LeagueID].ToList();
            if (!leagueChannels.Any())
            {
                continue;
            }

            await SendAllDropMessages(leagueAction, leagueChannels);
            await SendAllBidMessages(leagueAction, leagueChannels);
        }
    }

    private async Task SendAllBidMessages(LeagueActionProcessingSet leagueAction, List<MinimalLeagueChannel> leagueChannels)
    {
        var bidMessages = new List<string>();
        var leagueActionDictionaryByGame = new Dictionary<string, List<PickupBid>>();

        foreach (var leagueActionBid in leagueAction.Bids)
        {
            if (leagueActionDictionaryByGame.ContainsKey(leagueActionBid.MasterGame.GameName))
            {
                leagueActionDictionaryByGame[leagueActionBid.MasterGame.GameName].Add(leagueActionBid);
            }
            else
            {
                leagueActionDictionaryByGame.Add(leagueActionBid.MasterGame.GameName,
                    new List<PickupBid> { leagueActionBid });
            }
        }

        foreach (var bidGameAction in leagueActionDictionaryByGame)
        {
            var winningBidAmount = bidGameAction.Value.OrderByDescending(b => b.BidAmount).First().BidAmount;

            var messageToAdd = $"**{bidGameAction.Key}**\n";
            foreach (var bid in bidGameAction.Value)
            {
                if (!bid.Successful.HasValue)
                {
                    throw new Exception($"Bid {bid.BidID} Successful property is null");
                }

                if (bid.Successful.Value)
                {
                    var counterPickMessage = bid.CounterPick ? "(🎯 Counter Pick)" : "";
                    messageToAdd += $"- Won by {bid.Publisher.GetPublisherAndUserDisplayName()} with a bid of ${bid.BidAmount} {counterPickMessage}\n";
                }
                else
                {
                    var lossReason = bid.BidAmount == winningBidAmount
                        ? "lost on tiebreakers"
                        : "was outbid";
                    messageToAdd += $"- {bid.Publisher.GetPublisherAndUserDisplayName()}'s bid of ${bid.BidAmount} {lossReason}\n";
                }
            }

            bidMessages.Add($"{messageToAdd}");
        }

        var messageListToSend = new MessageListBuilder(bidMessages,
                MaxMessageLength)
            .WithTitle("Bids", new[] { TextStyleOption.Bold }, "\n", 2)
            .WithDivider("\n")
            .Build();
        await SendAllMessagesToAllLeagueChannels(leagueChannels, messageListToSend);
    }

    private async Task SendAllDropMessages(LeagueActionProcessingSet leagueAction, List<MinimalLeagueChannel> leagueChannels)
    {
        var dropMessages = new List<string>();
        foreach (var drop in leagueAction.Drops)
        {
            if (!drop.Successful.HasValue)
            {
                throw new Exception($"Drop {drop.DropRequestID} Successful property is null");
            }

            var statusMessage = drop.Successful.Value ? "Successful" : "Failed";
            var messageToAdd = $"**{drop.Publisher.GetPublisherAndUserDisplayName()}**: {drop.MasterGame.GameName} (Drop {statusMessage})";
            dropMessages.Add(messageToAdd);
        }

        var dropMessageListToSend = new MessageListBuilder(dropMessages,
                MaxMessageLength)
            .WithTitle("Drops", new[] { TextStyleOption.Bold }, "\n", 2)
            .WithDivider("\n")
            .Build();
        await SendAllMessagesToAllLeagueChannels(leagueChannels, dropMessageListToSend);
    }

    private async Task SendAllMessagesToAllLeagueChannels(List<MinimalLeagueChannel> leagueChannels, IReadOnlyList<string> messageListToSend)
    {
        foreach (var leagueChannel in leagueChannels)
        {
            foreach (var messageToSend in messageListToSend)
            {
                var guild = _client.GetGuild(leagueChannel.GuildID);
                var channel = guild?.GetTextChannel(leagueChannel.ChannelID);
                if (channel is null)
                {
                    continue;
                }

                await channel.TrySendMessageAsync(messageToSend);
            }
        }
    }

    public async Task SendTradeUpdateMessage(Trade trade)
    {
        bool shouldRun = await StartBot();
        if (!shouldRun)
        {
            return;
        }

        var channel = await GetChannelForLeague(trade.LeagueYear.League.LeagueID);
        if (channel is null)
        {
            return;
        }

        var header = $"The following trade has been **{trade.Status.Value.ToUpper()}**";

        var embedFieldBuilder = new EmbedFieldBuilder
        {
            Name = "Trade Details",
            Value = BuildTradeMessage(trade, trade.Status.Equals(TradeStatus.Proposed)),
            IsInline = false
        };
        var embedFieldBuilders = new List<EmbedFieldBuilder>
        {
            embedFieldBuilder
        };

        await channel.TrySendMessageAsync(embed: _discordFormatter.BuildRegularEmbed(
            "Trade Update",
            header,
            null,
            embedFieldBuilders));
    }

    private static string BuildTradeMessage(Trade trade, bool includeMessage)
    {
        var message = $"**{trade.Proposer.GetPublisherAndUserDisplayName()}** will receive: ";

        var counterPartySendGames = BuildGameListText(trade.CounterPartyMasterGames);
        var hasCounterPartySendGames = !string.IsNullOrEmpty(counterPartySendGames);

        if (hasCounterPartySendGames)
        {
            message += counterPartySendGames;
        }

        if (trade.CounterPartyBudgetSendAmount != default)
        {
            if (hasCounterPartySendGames)
            {
                message += " and ";
            }
            message += $"**${trade.CounterPartyBudgetSendAmount}** of budget**";
        }

        message += $"\n**{trade.CounterParty.GetPublisherAndUserDisplayName()}** will receive: ";

        var proposerSendGames = BuildGameListText(trade.ProposerMasterGames);
        var hasProposerSendGames = !string.IsNullOrEmpty(proposerSendGames);
        if (hasProposerSendGames)
        {
            message += proposerSendGames;
        }

        if (trade.ProposerBudgetSendAmount != default)
        {
            if (hasProposerSendGames)
            {
                message += " and ";
            }

            message += $"**${trade.ProposerBudgetSendAmount}** of budget";
        }

        if (includeMessage)
        {
            message += $"\nMessage from **{trade.Proposer.GetPublisherAndUserDisplayName()}**: **{trade.Message}**";
        }

        return message;
    }

    //private static string MakePublisherAndUserDisplayName(Publisher publisher, bool boldPublisherName = false, bool boldUserName = false)
    //{
    //    var publisherName = boldPublisherName
    //        ? $"**{publisher.PublisherName}**"
    //        : publisher.PublisherName;
    //    var userName = boldUserName
    //        ? $"**{publisher.User.UserName}**"
    //        : publisher.User.UserName;
    //    return $"{publisherName} ({userName})";
    //}

    private async Task<SocketTextChannel?> GetChannelForLeague(Guid leagueID)
    {
        var allChannels = await _discordRepo.GetAllLeagueChannels();
        var leagueChannel = allChannels.FirstOrDefault(c => c.LeagueID == leagueID);
        if (leagueChannel is null)
        {
            return null;
        }

        var guild = _client.GetGuild(leagueChannel.GuildID);
        var channel = guild?.GetTextChannel(leagueChannel.ChannelID);
        return channel;
    }

    private static string BuildGameListText(IEnumerable<MasterGameYearWithCounterPick> games)
    {
        var gameNames = games.Select(g => g.MasterGameYear.MasterGame.GameName);
        var gameNameString = string.Join(" and ", gameNames);
        return gameNameString;
    }

    private Task Client_Ready()
    {
        _botIsReady = true;
        return Task.CompletedTask;
    }

    private static Task Log(LogMessage msg)
    {
        Serilog.Log.Information(msg.ToString());
        return Task.CompletedTask;
    }
}
