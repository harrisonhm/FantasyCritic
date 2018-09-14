using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FantasyCritic.Lib.Domain;
using NodaTime;

namespace FantasyCritic.Web.Models.Responses
{
    public class PublisherGameViewModel
    {
        public PublisherGameViewModel(PublisherGame publisherGame, IClock clock)
        {
            GameName = publisherGame.GameName;
            Timestamp = publisherGame.Timestamp.ToDateTimeUtc();

            Waiver = publisherGame.Waiver;
            AntiPick = publisherGame.AntiPick;

            Type = "D";
            if (publisherGame.Waiver)
            {
                Type = "W";
            }
            if (publisherGame.AntiPick)
            {
                Type = "CP";
            }

            FantasyScore = publisherGame.FantasyScore;

            if (publisherGame.MasterGame.HasValue)
            {
                GameName = publisherGame.MasterGame.Value.GameName;
                EstimatedReleaseDate = publisherGame.MasterGame.Value.EstimatedReleaseDate;
                if (publisherGame.MasterGame.Value.ReleaseDate.HasValue)
                {
                    ReleaseDate = publisherGame.MasterGame.Value.ReleaseDate.Value.ToDateTimeUnspecified();
                }
                CriticScore = publisherGame.MasterGame.Value.CriticScore;
            }

            Linked = publisherGame.MasterGame.HasValue;
            if (publisherGame.MasterGame.HasValue)
            {
                Released = publisherGame.MasterGame.Value.IsReleased(clock);
            }

            WillRelease = publisherGame.WillRelease();
        }

        public string GameName { get; }
        public DateTime Timestamp { get; }
        public string Type { get; }
        public bool Waiver { get; }
        public bool AntiPick { get; }
        public string EstimatedReleaseDate { get; }
        public DateTime? ReleaseDate { get; }
        public decimal? FantasyScore { get; }
        public decimal? CriticScore { get; }

        public bool Linked { get; }
        public bool Released { get; }
        public bool WillRelease { get; }
    }
}
