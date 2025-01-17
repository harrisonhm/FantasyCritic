using FantasyCritic.Lib.DependencyInjection;
using FantasyCritic.Lib.Domain.Conferences;
using FantasyCritic.Lib.Extensions;
using FantasyCritic.Lib.Identity;
using FantasyCritic.Lib.Interfaces;
using FantasyCritic.MySQL.Entities;
using FantasyCritic.MySQL.Entities.Conferences;
using FantasyCritic.MySQL.Entities.Identity;
using Serilog;

namespace FantasyCritic.MySQL;
public class MySQLConferenceRepo : IConferenceRepo
{
    private static readonly ILogger _logger = Log.ForContext<MySQLConferenceRepo>();
    
    private readonly string _connectionString;
    private readonly MySQLFantasyCriticRepo _fantasyCriticRepo;
    private readonly IReadOnlyFantasyCriticUserStore _userStore;

    public MySQLConferenceRepo(RepositoryConfiguration configuration, IReadOnlyFantasyCriticUserStore userStore, IMasterGameRepo masterGameRepo)
    {
        _connectionString = configuration.ConnectionString;
        _fantasyCriticRepo = new MySQLFantasyCriticRepo(configuration, userStore, masterGameRepo);
        _userStore = userStore;
    }

    public async Task<IReadOnlyList<Conference>> GetConferencesForUser(FantasyCriticUser user)
    {
        const string conferenceSQL =
            """
            select tbl_conference.* 
            from tbl_conference join tbl_conference_hasuser on tbl_conference.ConferenceID = tbl_conference_hasuser.ConferenceID
            where UserID = @userID and IsDeleted = 0;
            """;

        var queryObject = new
        {
            userID = user.Id
        };

        await using var connection = new MySqlConnection(_connectionString);

        var conferenceEntities = (await connection.QueryAsync<ConferenceEntity>(conferenceSQL, queryObject)).ToList();

        var managerUserIDs = conferenceEntities.Select(x => x.ConferenceManager).ToList();
        var managers = await _userStore.GetUsers(managerUserIDs);
        var managerDictionary = managers.ToDictionary(x => x.Id);

        var conferenceQueryObject = new
        {
            conferenceIDs = conferenceEntities.Select(x => x.ConferenceID).ToList(),
        };

        const string conferenceYearSQL = "select ConferenceID, Year from tbl_conference_year where ConferenceID in @conferenceIDs;";
        const string leaguesInConferenceSQL = "select ConferenceID, LeagueID from tbl_league where ConferenceID in @conferenceIDs;";

        IEnumerable<(Guid ConferenceID, int Year)> conferenceYears = await connection.QueryAsync<(Guid ConferenceID, int Year)>(conferenceYearSQL, conferenceQueryObject);
        IEnumerable<(Guid ConferenceID, Guid LeagueID)> leagues = await connection.QueryAsync<(Guid ConferenceID, Guid LeagueID)>(leaguesInConferenceSQL, conferenceQueryObject);

        var conferenceYearLookup = conferenceYears.ToLookup(x => x.ConferenceID);
        var leagueLookup = leagues.ToLookup(x => x.ConferenceID);

        List<Conference> conferences = new List<Conference>();
        foreach (var conferenceEntity in conferenceEntities)
        {
            FantasyCriticUser conferenceManager = managerDictionary[conferenceEntity.ConferenceManager];
            var yearsForConference = conferenceYearLookup[conferenceEntity.ConferenceID].Select(x => x.Year);
            var leaguesForConference = leagueLookup[conferenceEntity.ConferenceID].Select(x => x.LeagueID);

            Conference conference = conferenceEntity.ToDomain(conferenceManager, yearsForConference, leaguesForConference);
            conferences.Add(conference);
        }

        return conferences;
    }

    public async Task CreateConference(Conference conference, League primaryLeague, int year, LeagueOptions options)
    {
        ConferenceEntity conferenceEntity = new ConferenceEntity(conference);
        ConferenceYearEntity conferenceYearEntity = new ConferenceYearEntity(conference, year, false);

        const string createConferenceSQL =
            """
            insert into tbl_conference(ConferenceID,ConferenceName,ConferenceManager,PrimaryLeagueID,CustomRulesConference) VALUES
            (@ConferenceID,@ConferenceName,@ConferenceManager,@PrimaryLeagueID,@CustomRulesConference);
            """;
        const string createConferenceYearSQL =
            """
            insert into tbl_conference_year(ConferenceID,Year,OpenForDrafting) VALUES
            (@ConferenceID,@Year,0)
            """;
        const string setConferenceIDSQL =
            """
            update tbl_league set ConferenceID = @conferenceID where LeagueID = @leagueID;
            """;

        var setConferenceIDParameters = new { conferenceID = conference.ConferenceID, leagueID = primaryLeague.LeagueID };

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await _fantasyCriticRepo.CreateLeagueInTransaction(primaryLeague, year, options, connection, transaction);
        await connection.ExecuteAsync(createConferenceSQL, conferenceEntity, transaction);
        await connection.ExecuteAsync(setConferenceIDSQL, setConferenceIDParameters, transaction);
        await connection.ExecuteAsync(createConferenceYearSQL, conferenceYearEntity, transaction);
        await AddPlayerToConferenceInternal(conference, conference.ConferenceManager, connection, transaction);
        await transaction.CommitAsync();
    }

    public async Task AddLeagueToConference(Conference conference, LeagueYear primaryLeagueYear, League newLeague)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await _fantasyCriticRepo.CreateLeagueInTransaction(newLeague, primaryLeagueYear.Year, primaryLeagueYear.Options, connection, transaction);
        await transaction.CommitAsync();
    }

    private async Task AddPlayerToConferenceInternal(Conference conference, FantasyCriticUser user, MySqlConnection connection, MySqlTransaction transaction)
    {
        var userAddObject = new
        {
            conferenceID = conference.ConferenceID,
            userID = user.Id,
        };

        await connection.ExecuteAsync("insert into tbl_conference_hasuser(ConferenceID,UserID) VALUES (@conferenceID,@userID);", userAddObject, transaction);
    }

    public async Task<Conference?> GetConference(Guid conferenceID)
    {
        const string conferenceSQL = "select * from tbl_conference where ConferenceID = @conferenceID and IsDeleted = 0;";
        var queryObject = new
        {
            conferenceID
        };

        await using var connection = new MySqlConnection(_connectionString);
        
        ConferenceEntity? conferenceEntity = await connection.QuerySingleOrDefaultAsync<ConferenceEntity?>(conferenceSQL, queryObject);
        if (conferenceEntity is null)
        {
            return null;
        }

        FantasyCriticUser manager = await _userStore.FindByIdOrThrowAsync(conferenceEntity.ConferenceManager, CancellationToken.None);

        const string conferenceYearSQL = "select Year from tbl_conference_year where ConferenceID = @conferenceID;";
        IEnumerable<int> years = await connection.QueryAsync<int>(conferenceYearSQL, queryObject);
        
        const string leaguesInConferenceSQL = "select LeagueID from tbl_league where ConferenceID = @conferenceID";
        IEnumerable<Guid> leagueIDs = await connection.QueryAsync<Guid>(leaguesInConferenceSQL, queryObject);

        Conference conference = conferenceEntity.ToDomain(manager, years, leagueIDs);
        return conference;
    }

    public async Task<ConferenceYear?> GetConferenceYear(Guid conferenceID, int year)
    {
        var conference = await GetConference(conferenceID);
        if (conference is null)
        {
            return null;
        }

        const string conferenceYearSQL = "select * from tbl_conference_year where ConferenceID = @conferenceID and Year = @year;";
        var queryObject = new
        {
            conferenceID,
            year
        };

        await using var connection = new MySqlConnection(_connectionString);
        ConferenceYearEntity? conferenceYearEntity = await connection.QuerySingleOrDefaultAsync<ConferenceYearEntity?>(conferenceYearSQL, queryObject);
        if (conferenceYearEntity is null)
        {
            return null;
        }

        var supportedYear = await _fantasyCriticRepo.GetSupportedYear(year);
        ConferenceYear conferenceYear = conferenceYearEntity.ToDomain(conference, supportedYear);
        return conferenceYear;
    }

    public async Task<IReadOnlyList<FantasyCriticUser>> GetUsersInConference(Conference conference)
    {
        const string userSQL = "select tbl_user.* from tbl_user join tbl_conference_hasuser on tbl_conference_hasuser.UserID = tbl_user.UserID where ConferenceID = @conferenceID;";
        var queryObject = new
        {
            conferenceID = conference.ConferenceID
        };

        await using var connection = new MySqlConnection(_connectionString);

        var userEntities = (await connection.QueryAsync<FantasyCriticUserEntity>(userSQL, queryObject)).ToList();
        return userEntities.Select(x => x.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<ConferencePlayer>> GetPlayersInConference(Conference conference)
    {
        const string leagueManagerSQL = "select LeagueID, LeagueManager from tbl_league where ConferenceID = @conferenceID;";
        const string leagueUserSQL = """
                                     select tbl_league_hasuser.LeagueID, tbl_league_hasuser.UserID
                                     from tbl_league_hasuser join tbl_league on tbl_league_hasuser.LeagueID = tbl_league.LeagueID
                                     where tbl_league.ConferenceID = @conferenceID;
                                     """;
        const string activePlayerSQL = """
                                       SELECT tbl_league_activeplayer.LeagueID, tbl_league_activeplayer.Year, tbl_league_activeplayer.UserID FROM tbl_league_activeplayer
                                       JOIN tbl_league ON tbl_league_activeplayer.LeagueID = tbl_league.LeagueID
                                       WHERE tbl_league.ConferenceID = @conferenceID;
                                       """;

        var queryObject = new
        {
            conferenceID = conference.ConferenceID
        };

        var usersInConference = await GetUsersInConference(conference);

        await using var connection = new MySqlConnection(_connectionString);
        var leagueManagers = await connection.QueryAsync<LeagueManagerEntity>(leagueManagerSQL, queryObject);
        var leagueUsers = await connection.QueryAsync<LeagueUserEntity>(leagueUserSQL, queryObject);
        var leagueActivePlayers = await connection.QueryAsync<LeagueActivePlayerEntity>(activePlayerSQL, queryObject);

        var leagueManagerLookup = leagueManagers.ToLookup(x => x.LeagueManager);
        var leagueUserLookup = leagueUsers.ToLookup(x => x.UserID);
        var leagueActivePlayerLookup = leagueActivePlayers.ToLookup(x => x.UserID);

        List<ConferencePlayer> conferencePlayers = new List<ConferencePlayer>();
        foreach (var user in usersInConference)
        {
            var leaguesManaged = leagueManagerLookup[user.Id].Select(x => x.LeagueID).ToHashSet();
            var leaguesIn = leagueUserLookup[user.Id].Select(x => x.LeagueID).ToHashSet();
            var leagueYearsActiveIn = leagueActivePlayerLookup[user.Id].Select(x => new LeagueYearKey(x.LeagueID, x.Year)).ToHashSet();
            var player = new ConferencePlayer(user, leaguesIn, leaguesManaged, leagueYearsActiveIn);
            conferencePlayers.Add(player);
        }
        
        return conferencePlayers;
    }

    public async Task RemovePlayerFromConference(Conference conference, FantasyCriticUser removeUser)
    {
        const string sql = """
                           delete from tbl_conference_hasuser
                           where ConferenceID = @conferenceID and UserID = @userID;
                           """;
        
        var deleteParam = new
        {
            conferenceID = conference.ConferenceID,
            userID = removeUser.Id
        };

        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, deleteParam);
    }

    public async Task<IReadOnlyList<ConferenceLeague>> GetLeaguesInConference(Conference conference)
    {
        const string leagueSQL = "select LeagueID, LeagueName, LeagueManager from tbl_league where ConferenceID = @conferenceID;";
        var queryObject = new
        {
            conferenceID = conference.ConferenceID
        };

        await using var connection = new MySqlConnection(_connectionString);
        var leagueEntities = (await connection.QueryAsync<ConferenceLeagueEntity>(leagueSQL, queryObject)).ToList();
        
        var leagueManagerIDs = leagueEntities.Select(x => x.LeagueManager).ToList();
        var leagueManagers = await _userStore.GetUsers(leagueManagerIDs);
        var leagueManagerDictionary = leagueManagers.ToDictionary(x => x.Id);

        List<ConferenceLeague> leaguesInConference = new List<ConferenceLeague>();
        foreach (var leagueEntity in leagueEntities)
        {
            var leagueManager = leagueManagerDictionary[leagueEntity.LeagueManager];
            ConferenceLeague conferenceLeague = leagueEntity.ToDomain(leagueManager);
            leaguesInConference.Add(conferenceLeague);
        }

        return leaguesInConference;
    }

    public async Task<IReadOnlyList<ConferenceLeagueYear>> GetLeagueYearsInConferenceYear(ConferenceYear conferenceYear)
    {
        const string leagueYearSQL = """
                                     select tbl_league.LeagueID, tbl_league.LeagueName, tbl_league.LeagueManager, tbl_league_year.Year,
                                     tbl_league_year.PlayStatus <> "NotStartedDraft" AS DraftStarted,
                                     tbl_league_year.PlayStatus = "DraftFinal" AS DraftFinished
                                     from tbl_league_year join tbl_league on tbl_league.LeagueID = tbl_league_year.LeagueID 
                                     where ConferenceID = @conferenceID and Year = @year;
                                     """;
        var queryObject = new
        {
            conferenceID = conferenceYear.Conference.ConferenceID,
            year = conferenceYear.Year
        };

        await using var connection = new MySqlConnection(_connectionString);
        var leagueYearEntities = (await connection.QueryAsync<ConferenceLeagueYearEntity>(leagueYearSQL, queryObject)).ToList();

        var leagueManagerIDs = leagueYearEntities.Select(x => x.LeagueManager).ToList();
        var leagueManagers = await _userStore.GetUsers(leagueManagerIDs);
        var leagueManagerDictionary = leagueManagers.ToDictionary(x => x.Id);

        List<ConferenceLeagueYear> leaguesInConference = new List<ConferenceLeagueYear>();
        foreach (var leagueYearEntity in leagueYearEntities)
        {
            var leagueManager = leagueManagerDictionary[leagueYearEntity.LeagueManager];
            ConferenceLeagueYear conferenceLeague = leagueYearEntity.ToDomain(leagueManager);
            leaguesInConference.Add(conferenceLeague);
        }

        return leaguesInConference;
    }

    public async Task EditConference(Conference conference, string newConferenceName, bool newCustomRulesConference)
    {
        const string conferenceSQL = "update tbl_conference set ConferenceName = @conferenceName, CustomRulesConference = @customRulesConference where ConferenceID = @conferenceID;";
        const string leagueSQL = "update tbl_league set CustomRulesLeague = @customRulesConference where ConferenceID = @conferenceID;";
        var queryObject = new
        {
            conferenceID = conference.ConferenceID,
            conferenceName = newConferenceName,
            customRulesConference = newCustomRulesConference
        };

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(conferenceSQL, queryObject, transaction);
        await connection.ExecuteAsync(leagueSQL, queryObject, transaction);
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<ConferenceInviteLink>> GetInviteLinks(Conference conference)
    {
        var query = new
        {
            conferenceID = conference.ConferenceID
        };

        await using var connection = new MySqlConnection(_connectionString);
        var results = await connection.QueryAsync<ConferenceInviteLinkEntity>("select * from tbl_conference_invitelink where ConferenceID = @conferenceID;", query);

        var inviteLinks = results.Select(x => x.ToDomain(conference)).ToList();
        return inviteLinks;
    }

    public async Task SaveInviteLink(ConferenceInviteLink inviteLink)
    {
        const string sql = """
                           insert into tbl_conference_invitelink(InviteID,ConferenceID,InviteCode,Active) VALUES 
                           (@InviteID,@ConferenceID,@InviteCode,@Active);
                           """;
        ConferenceInviteLinkEntity entity = new ConferenceInviteLinkEntity(inviteLink);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, entity);
    }

    public async Task DeactivateInviteLink(ConferenceInviteLink inviteLink)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.ExecuteAsync("update tbl_conference_invitelink SET Active = 0 where InviteID = @inviteID;", new { inviteID = inviteLink.InviteID });
    }

    public async Task<ConferenceInviteLink?> GetInviteLinkByInviteCode(Guid inviteCode)
    {
        await using var connection = new MySqlConnection(_connectionString);
        var result = await connection.QuerySingleOrDefaultAsync<ConferenceInviteLinkEntity>("select * from tbl_conference_invitelink where InviteCode = @inviteCode and Active = 1;", new { inviteCode });

        if (result is null)
        {
            return null;
        }

        var conference = await this.GetConferenceOrThrow(result.ConferenceID);
        return result.ToDomain(conference);
    }

    public async Task AddPlayerToConference(Conference conference, FantasyCriticUser inviteUser)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await AddPlayerToConferenceInternal(conference, inviteUser, connection, transaction);
        await transaction.CommitAsync();
    }

    public async Task TransferConferenceManager(Conference conference, FantasyCriticUser newManager)
    {
        const string sql = "UPDATE tbl_conference SET ConferenceManager = @newManagerUserID WHERE ConferenceID = @conferenceID;";

        var transferObject = new
        {
            conferenceID = conference.ConferenceID,
            newManagerUserID = newManager.Id
        };

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, transferObject);
    }

    public async Task EditDraftStatusForConferenceYear(ConferenceYear conferenceYear, bool openForDrafting)
    {
        const string sql = "UPDATE tbl_conference_year SET OpenForDrafting = @openForDrafting WHERE ConferenceID = @conferenceID AND Year = @year;";

        var param = new
        {
            conferenceID = conferenceYear.Conference.ConferenceID,
            year = conferenceYear.Year,
            openForDrafting
        };

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, param);
    }

    public async Task<Result> AssignLeaguePlayers(ConferenceYear conferenceYear, IReadOnlyList<ConferenceLeague> conferenceLeagues, IReadOnlyDictionary<ConferenceLeague, IReadOnlyList<FantasyCriticUser>> userAssignments)
    {
        const string currentLeagueUserSQL = """
                                            select tbl_league_hasuser.LeagueID, tbl_league_hasuser.UserID
                                            from tbl_league_hasuser 
                                            join tbl_league on tbl_league_hasuser.LeagueID = tbl_league.LeagueID
                                            where tbl_league.ConferenceID = @conferenceID;
                                            """;

        const string publisherEntitiesSQL = """
                                            select tbl_league_publisher.PublisherID, tbl_league_publisher.LeagueID, tbl_league_publisher.Year, tbl_league_publisher.UserID, tbl_league_publisher.DraftPosition
                                            from tbl_league_publisher
                                            join tbl_league on tbl_league_publisher.LeagueID = tbl_league.LeagueID 
                                            where tbl_league.ConferenceID = @conferenceID AND tbl_league_publisher.Year = @year;
                                            """;

        const string publisherUpdateSQL = "UPDATE tbl_league_publisher SET LeagueID = @LeagueID WHERE PublisherID = @PublisherID;";
        const string deleteExistingLeagueUserSQL = "delete from tbl_league_hasuser where LeagueID = @LeagueID AND UserID = @UserID;";
        const string deleteExistingLeagueYearActivePlayerSQL = "delete from tbl_league_activeplayer where LeagueID = @LeagueID AND Year = @Year AND UserID = @UserID;";
        const string fixDraftOrderSQL = "update tbl_league_publisher SET DraftPosition = @draftPosition where PublisherID = @publisherID;";

        var conferenceParam = new
        {
            conferenceID = conferenceYear.Conference.ConferenceID,
            year = conferenceYear.Year
        };

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var leagueHasPlayerInPreviousYear = new Dictionary<ConferenceLeague, List<FantasyCriticUser>>();
        var previousYears = conferenceYear.Conference.Years.Where(x => x < conferenceYear.Year).ToList();
        foreach (var previousYear in previousYears)
        {
            var previousConferenceYear = await GetConferenceYear(conferenceYear.Conference.ConferenceID, previousYear);
            var leaguesInConferenceYear = await GetLeagueYearsInConferenceYear(previousConferenceYear!);
            foreach (var conferenceLeagueYear in leaguesInConferenceYear)
            {
                if (!leagueHasPlayerInPreviousYear.ContainsKey(conferenceLeagueYear.League))
                {
                    leagueHasPlayerInPreviousYear.Add(conferenceLeagueYear.League, new List<FantasyCriticUser>());
                }

                var fullLeagueYear = await _fantasyCriticRepo.GetLeagueYear(conferenceLeagueYear.League.LeagueID, conferenceLeagueYear.Year);
                foreach (var publisher in fullLeagueYear!.Publishers)
                {
                    leagueHasPlayerInPreviousYear[conferenceLeagueYear.League].Add(publisher.User);
                }
            }
        }
        
        var leagueHasPlayerInPreviousYearLookup = leagueHasPlayerInPreviousYear
            .SelectMany(kv => kv.Value.Select(v => new { Key = kv.Key, Value = v }))
            .ToLookup(item => item.Key, item => item.Value);
        
        try
        {
            var currentLeagueUsers = (await connection.QueryAsync<LeagueHasUserEntity>(currentLeagueUserSQL, conferenceParam, transaction)).ToList();
            var currentPublisherEntities = (await connection.QueryAsync<PublisherEntity>(publisherEntitiesSQL, conferenceParam, transaction)).ToList();

            var leagueUserLookup = currentLeagueUsers.ToLookup(x => x.LeagueID);

            List<LeagueHasUserEntity> usersThatCanBeSafelyRemovedFromLeague = new List<LeagueHasUserEntity>();
            List<LeagueYearActivePlayer> activePlayersToRemove = new List<LeagueYearActivePlayer>();
            foreach (var leagueUser in currentLeagueUsers)
            {
                var conferenceLeague = conferenceLeagues.Single(x => x.LeagueID == leagueUser.LeagueID);
                bool wantToDelete = !userAssignments.ContainsKey(conferenceLeague) || userAssignments[conferenceLeague].All(x => x.Id != leagueUser.UserID);
                if (!wantToDelete)
                {
                    continue;
                }

                if (leagueHasPlayerInPreviousYearLookup[conferenceLeague].Any(x => x.Id == leagueUser.UserID))
                {
                    continue;
                }

                usersThatCanBeSafelyRemovedFromLeague.Add(new LeagueHasUserEntity() { LeagueID = leagueUser.LeagueID, UserID = leagueUser.UserID });
                activePlayersToRemove.Add(new LeagueYearActivePlayer()
                {
                    LeagueID = leagueUser.LeagueID,
                    Year = conferenceYear.Year,
                    UserID = leagueUser.UserID
                });
            }

            List<LeagueHasUserEntity> newUsersToAdd = new List<LeagueHasUserEntity>();
            List<LeagueYearActivePlayer> newActivePlayersToAdd = new List<LeagueYearActivePlayer>();

            foreach (var leagueUsers in userAssignments)
            {
                var usersCurrentlyInLeague = leagueUserLookup[leagueUsers.Key.LeagueID];
                var userIDsCurrentlyInLeague = usersCurrentlyInLeague.Select(x => x.UserID).ToList();
                var userIDsRequestedToBeInLeague = leagueUsers.Value.Select(x => x.Id).ToList();
                var usersThatShouldBeAdded = userIDsRequestedToBeInLeague.Except(userIDsCurrentlyInLeague).ToList();

                newUsersToAdd.AddRange(usersThatShouldBeAdded.Select(x => new LeagueHasUserEntity() { LeagueID = leagueUsers.Key.LeagueID, UserID = x }));
                newActivePlayersToAdd.AddRange(usersThatShouldBeAdded.Select(x => new LeagueYearActivePlayer()
                {
                    LeagueID = leagueUsers.Key.LeagueID,
                    Year = conferenceYear.Year,
                    UserID = x
                }));
            }

            List<LeagueYearKey> leagueYearsToFixDraftOrders = new List<LeagueYearKey>();
            List<PublisherEntity> publishersToUpdate = new List<PublisherEntity>();
            foreach (var publisher in currentPublisherEntities)
            {
                var userNewLeague = newUsersToAdd.FirstOrDefault(x => x.UserID == publisher.UserID);
                if (userNewLeague is null)
                {
                    continue;
                }

                leagueYearsToFixDraftOrders.Add(new LeagueYearKey(publisher.LeagueID, publisher.Year));
                leagueYearsToFixDraftOrders.Add(new LeagueYearKey(userNewLeague.LeagueID, publisher.Year));

                publishersToUpdate.Add(new PublisherEntity()
                {
                    PublisherID = publisher.PublisherID,
                    LeagueID = userNewLeague.LeagueID,
                    Year = publisher.Year,
                    UserID = publisher.UserID
                });
            }
            leagueYearsToFixDraftOrders = leagueYearsToFixDraftOrders.Distinct().ToList();

            var publisherLookup = currentPublisherEntities.ToLookup(x => new LeagueYearKey(x.LeagueID, x.Year));
            List<SetDraftOrderEntity> tempDraftOrderEntities = new List<SetDraftOrderEntity>();
            List<SetDraftOrderEntity> finalDraftOrderEntities = new List<SetDraftOrderEntity>();
            for (var i = 0; i < leagueYearsToFixDraftOrders.Count; i++)
            {
                var leagueYear = leagueYearsToFixDraftOrders[i];
                var existingPublishersInLeagueYear = publisherLookup[leagueYear].ToList();
                var existingPublisherIDsInLeagueYear = existingPublishersInLeagueYear.Select(x => x.PublisherID).ToHashSet();

                var tempEntities = existingPublishersInLeagueYear.Select(pub => new SetDraftOrderEntity(pub.PublisherID, null));
                tempDraftOrderEntities.AddRange(tempEntities);

                var publishersMovedOutOfThisLeagueYearIDs = publishersToUpdate
                    .Where(x => existingPublisherIDsInLeagueYear.Contains(x.PublisherID) && x.LeagueID != leagueYear.LeagueID)
                    .Select(x => x.PublisherID)
                    .ToHashSet();
                var publishersMovedIntoThisLeagueYear = publishersToUpdate.Where(x => !existingPublisherIDsInLeagueYear.Contains(x.PublisherID) && x.LeagueID == leagueYear.LeagueID).ToList();

                var existingPublishersPlusMoveIns = existingPublishersInLeagueYear.Concat(publishersMovedIntoThisLeagueYear).ToList();
                var finalNewPublishers = existingPublishersPlusMoveIns.Where(x => !publishersMovedOutOfThisLeagueYearIDs.Contains(x.PublisherID)).ToList();

                var finalEntities = finalNewPublishers.OrderBy(x => x.DraftPosition).Select((pub, index) => new SetDraftOrderEntity(pub.PublisherID, index + 1));
                finalDraftOrderEntities.AddRange(finalEntities);
            }

            await connection.ExecuteAsync(fixDraftOrderSQL, tempDraftOrderEntities, transaction);

            //Add users to new leagues
            await connection.BulkInsertAsync(newUsersToAdd, "tbl_league_hasuser", 500, transaction);
            await connection.BulkInsertAsync(newActivePlayersToAdd, "tbl_league_activeplayer", 500, transaction, insertIgnore: true);

            //Update any existing publishers to the new league
            foreach (var publisher in publishersToUpdate)
            {
                await connection.ExecuteAsync(publisherUpdateSQL, publisher, transaction);
            }

            //Delete users from old leagues
            foreach (var activePlayerToRemove in activePlayersToRemove)
            {
                await connection.ExecuteAsync(deleteExistingLeagueYearActivePlayerSQL, activePlayerToRemove, transaction);
            }
            foreach (var userToRemove in usersThatCanBeSafelyRemovedFromLeague)
            {
                await connection.ExecuteAsync(deleteExistingLeagueUserSQL, userToRemove, transaction);
            }

            await connection.ExecuteAsync(fixDraftOrderSQL, finalDraftOrderEntities, transaction);

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error assigning league players.");
            await transaction.RollbackAsync();
            return Result.Failure("Something went wrong when re-assigning users.");
        }

        return Result.Success();
    }
}
