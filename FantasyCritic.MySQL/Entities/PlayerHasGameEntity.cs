using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FantasyCritic.Lib.Domain;
using NodaTime;

namespace FantasyCritic.MySQL.Entities
{
    internal class PlayerHasGameEntity
    {
        public PlayerHasGameEntity()
        {

        }

        public PlayerHasGameEntity(League requestLeague, PlayerGame playerGame)
        {
            LeagueID = requestLeague.LeagueID;
            Year = playerGame.Year;
            UserID = playerGame.User.UserID;
            GameName = playerGame.GameName;
            Timestamp = playerGame.Timestamp.ToDateTimeUtc();
            Waiver = playerGame.Waiver;
            AntiPick = playerGame.AntiPick;
            FantasyScore = playerGame.FantasyScore;

            if (playerGame.MasterGame.HasValue)
            {
                MasterGameID = playerGame.MasterGame.Value.MasterGameID;
            }
        }

        public Guid LeagueID { get; set; }
        public int Year { get; set; }
        public Guid UserID { get; set; }
        public string GameName { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Waiver { get; set; }
        public bool AntiPick { get; set; }
        public decimal? FantasyScore { get; set; }
        public Guid? MasterGameID { get; set; }

        public PlayerGame ToDomain(FantasyCriticUser user, Maybe<MasterGame> masterGame)
        {
            PlayerGame domain = new PlayerGame(user, Year, GameName, Instant.FromDateTimeUtc(Timestamp), Waiver, AntiPick, FantasyScore, masterGame);
            return domain;
        }
    }
}
