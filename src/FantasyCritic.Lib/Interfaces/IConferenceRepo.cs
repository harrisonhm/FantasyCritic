using FantasyCritic.Lib.Domain.Conferences;
using FantasyCritic.Lib.Identity;

namespace FantasyCritic.Lib.Interfaces;
public interface IConferenceRepo
{
    Task<IReadOnlyList<Conference>> GetConferencesForUser(FantasyCriticUser user);
    Task CreateConference(Conference conference, League primaryLeague, int year, LeagueOptions options);
    Task AddLeagueToConference(Conference conference, LeagueYear primaryLeagueYear, League newLeague);
    Task<Conference?> GetConference(Guid conferenceID);
    Task<ConferenceYear?> GetConferenceYear(Guid conferenceID, int year);
    Task<IReadOnlyList<FantasyCriticUser>> GetUsersInConference(Conference conference);
    Task<IReadOnlyList<ConferencePlayer>> GetPlayersInConference(Conference conference);
    Task RemovePlayerFromConference(Conference conference, FantasyCriticUser removeUser);
    Task<IReadOnlyList<ConferenceLeague>> GetLeaguesInConference(Conference conference);
    Task<IReadOnlyList<ConferenceLeagueYear>> GetLeagueYearsInConferenceYear(ConferenceYear conferenceYear);
    Task EditConference(Conference conference, string newConferenceName, bool newCustomRulesConference);

    Task<IReadOnlyList<ConferenceInviteLink>> GetInviteLinks(Conference conference);
    Task SaveInviteLink(ConferenceInviteLink inviteLink);
    Task DeactivateInviteLink(ConferenceInviteLink inviteLink);
    Task<ConferenceInviteLink?> GetInviteLinkByInviteCode(Guid inviteCode);
    Task AddPlayerToConference(Conference conference, FantasyCriticUser inviteUser);
    Task TransferConferenceManager(Conference conference, FantasyCriticUser newManager);
    Task EditDraftStatusForConferenceYear(ConferenceYear conferenceYear, bool openForDrafting);
    Task<Result> AssignLeaguePlayers(ConferenceYear conferenceYear, IReadOnlyList<ConferenceLeague> conferenceLeagues, IReadOnlyDictionary<ConferenceLeague, IReadOnlyList<FantasyCriticUser>> userAssignments);
}
