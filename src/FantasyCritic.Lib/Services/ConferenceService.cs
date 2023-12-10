using FantasyCritic.Lib.Domain.Conferences;
using FantasyCritic.Lib.Identity;
using FantasyCritic.Lib.Interfaces;

namespace FantasyCritic.Lib.Services;
public class ConferenceService
{
    private readonly IConferenceRepo _conferenceRepo;
    private readonly InterLeagueService _interLeagueService;
    private readonly IClock _clock;

    public ConferenceService(IConferenceRepo conferenceRepo, InterLeagueService interLeagueService, IClock clock)
    {
        _conferenceRepo = conferenceRepo;
        _interLeagueService = interLeagueService;
        _clock = clock;
    }

    public async Task<Result<Conference>> CreateConference(ConferenceCreationParameters parameters)
    {
        LeagueOptions options = new LeagueOptions(parameters.LeagueYearParameters);

        var validateOptions = options.Validate();
        if (validateOptions.IsFailure)
        {
            return Result.Failure<Conference>(validateOptions.Error);
        }

        if (!parameters.LeagueYearParameters.ScoringSystem.SupportedInYear(parameters.LeagueYearParameters.Year))
        {
            return Result.Failure<Conference>("That scoring mode is no longer supported.");
        }

        IEnumerable<int> years = new List<int>() { parameters.LeagueYearParameters.Year };
        //Primary league's conferenceID must start out null so that the database foreign keys work. It'll get set in a moment.
        League primaryLeague = new League(Guid.NewGuid(), parameters.PrimaryLeagueName, parameters.Manager, null, parameters.ConferenceName, years, true, false, parameters.CustomRulesConference, false, 0);
        Conference newConference = new Conference(Guid.NewGuid(), parameters.ConferenceName, parameters.Manager, years, parameters.CustomRulesConference, primaryLeague.LeagueID, new List<Guid>() { primaryLeague.LeagueID });
        await _conferenceRepo.CreateConference(newConference, primaryLeague, parameters.LeagueYearParameters.Year, options);
        return Result.Success(newConference);
    }

    public Task<Conference?> GetConference(Guid conferenceID)
    {
        return _conferenceRepo.GetConference(conferenceID);
    }

    public Task<ConferenceYear?> GetConferenceYear(Guid conferenceID, int year)
    {
        return _conferenceRepo.GetConferenceYear(conferenceID, year);
    }

    public Task<IReadOnlyList<FantasyCriticUser>> GetUsersInConference(Conference conference)
    {
        return _conferenceRepo.GetUsersInConference(conference);
    }

    public Task<IReadOnlyList<ConferenceLeague>> GetLeaguesInConference(Conference conference)
    {
        return _conferenceRepo.GetLeaguesInConference(conference);
    }

    public Task<IReadOnlyList<ConferenceLeagueYear>> GetLeagueYearsInConferenceYear(ConferenceYear conferenceYear)
    {
        return _conferenceRepo.GetLeagueYearsInConferenceYear(conferenceYear);
    }
}
