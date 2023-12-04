using FantasyCritic.Lib.Domain.Conferences;
using FantasyCritic.Lib.Identity;

namespace FantasyCritic.Web.Models.Responses.Conferences;

public class ConferencePlayerViewModel
{
    public ConferencePlayerViewModel(Conference conference, FantasyCriticUser user)
    {
        ConferenceID = conference.ConferenceID;
        ConferenceName = conference.ConferenceName;
        UserID = user.Id;
        DisplayName = user.UserName;
    }

    public Guid ConferenceID { get; }
    public string ConferenceName { get; }
    public Guid UserID { get; }
    public string DisplayName { get; }
}
