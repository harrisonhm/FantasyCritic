using System.ComponentModel.DataAnnotations;

namespace FantasyCritic.Web.Models.Requests.Royale;

public class ChangeRoyalePublisherIconRequest
{
    [Required]
    public Guid PublisherID { get; set; }
    public string? PublisherIcon { get; set; }
}
