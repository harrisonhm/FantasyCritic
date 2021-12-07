using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FantasyCritic.Lib.Domain.Requests;
using FantasyCritic.Lib.Domain.Results;
using FantasyCritic.Lib.Domain.ScoringSystems;
using FantasyCritic.Lib.Enums;

namespace FantasyCritic.Lib.Domain
{
    public class LeagueOptions
    {
        private readonly Dictionary<int, SpecialGameSlot> _specialSlotDictionary;

        public LeagueOptions(int standardGames, int gamesToDraft, int counterPicks, int counterPicksToDraft, int freeDroppableGames, int willNotReleaseDroppableGames, int willReleaseDroppableGames,
            bool dropOnlyDraftGames, bool counterPicksBlockDrops, int minimumBidAmount, IEnumerable<LeagueTagStatus> leagueTags, IEnumerable<SpecialGameSlot> specialGameSlots, 
            DraftSystem draftSystem, PickupSystem pickupSystem, ScoringSystem scoringSystem, TradingSystem tradingSystem, bool publicLeague)
        {
            StandardGames = standardGames;
            GamesToDraft = gamesToDraft;
            CounterPicks = counterPicks;
            CounterPicksToDraft = counterPicksToDraft;
            FreeDroppableGames = freeDroppableGames;
            WillNotReleaseDroppableGames = willNotReleaseDroppableGames;
            WillReleaseDroppableGames = willReleaseDroppableGames;
            DropOnlyDraftGames = dropOnlyDraftGames;
            CounterPicksBlockDrops = counterPicksBlockDrops;
            MinimumBidAmount = minimumBidAmount;
            LeagueTags = leagueTags.ToList();
            SpecialGameSlots = specialGameSlots.OrderBy(x => x.SpecialSlotPosition).ToList();
            DraftSystem = draftSystem;
            PickupSystem = pickupSystem;
            ScoringSystem = scoringSystem;
            TradingSystem = tradingSystem;
            PublicLeague = publicLeague;

            _specialSlotDictionary = SpecialGameSlots.ToDictionary(specialGameSlot => StandardGames - SpecialGameSlots.Count + specialGameSlot.SpecialSlotPosition);
        }

        public LeagueOptions(LeagueCreationParameters parameters)
        {
            StandardGames = parameters.StandardGames;
            GamesToDraft = parameters.GamesToDraft;
            CounterPicks = parameters.CounterPicks;
            CounterPicksToDraft = parameters.CounterPicksToDraft;
            FreeDroppableGames = parameters.FreeDroppableGames;
            WillNotReleaseDroppableGames = parameters.WillNotReleaseDroppableGames;
            WillReleaseDroppableGames = parameters.WillReleaseDroppableGames;
            DropOnlyDraftGames = parameters.DropOnlyDraftGames;
            CounterPicksBlockDrops = parameters.CounterPicksBlockDrops;
            MinimumBidAmount = parameters.MinimumBidAmount;
            LeagueTags = parameters.LeagueTags;
            SpecialGameSlots = parameters.SpecialGameSlots.OrderBy(x => x.SpecialSlotPosition).ToList();
            DraftSystem = parameters.DraftSystem;
            PickupSystem = parameters.PickupSystem;
            ScoringSystem = parameters.ScoringSystem;
            TradingSystem = parameters.TradingSystem;
            PublicLeague = parameters.PublicLeague;

            _specialSlotDictionary = SpecialGameSlots.ToDictionary(specialGameSlot => StandardGames - SpecialGameSlots.Count + specialGameSlot.SpecialSlotPosition);
        }

        public LeagueOptions(EditLeagueYearParameters parameters)
        {
            StandardGames = parameters.StandardGames;
            GamesToDraft = parameters.GamesToDraft;
            CounterPicks = parameters.CounterPicks;
            CounterPicksToDraft = parameters.CounterPicksToDraft;
            FreeDroppableGames = parameters.FreeDroppableGames;
            WillNotReleaseDroppableGames = parameters.WillNotReleaseDroppableGames;
            WillReleaseDroppableGames = parameters.WillReleaseDroppableGames;
            DropOnlyDraftGames = parameters.DropOnlyDraftGames;
            CounterPicksBlockDrops = parameters.CounterPicksBlockDrops;
            MinimumBidAmount = parameters.MinimumBidAmount;
            LeagueTags = parameters.LeagueTags;
            SpecialGameSlots = parameters.SpecialGameSlots.OrderBy(x => x.SpecialSlotPosition).ToList();
            DraftSystem = parameters.DraftSystem;
            PickupSystem = parameters.PickupSystem;
            ScoringSystem = parameters.ScoringSystem;
            TradingSystem = parameters.TradingSystem;
            PublicLeague = parameters.PublicLeague;

            _specialSlotDictionary = SpecialGameSlots.ToDictionary(specialGameSlot => StandardGames - SpecialGameSlots.Count + specialGameSlot.SpecialSlotPosition);
        }

        public int StandardGames { get; }
        public int GamesToDraft { get; }
        public int CounterPicks { get; }
        public int CounterPicksToDraft { get; }
        public int FreeDroppableGames { get; }
        public int WillNotReleaseDroppableGames { get; }
        public int WillReleaseDroppableGames { get; }
        public bool DropOnlyDraftGames { get; }
        public bool CounterPicksBlockDrops { get; }
        public int MinimumBidAmount { get; }
        public IReadOnlyList<LeagueTagStatus> LeagueTags { get; }
        public IReadOnlyList<SpecialGameSlot> SpecialGameSlots { get; }
        public DraftSystem DraftSystem { get; }
        public PickupSystem PickupSystem { get; }
        public ScoringSystem ScoringSystem { get; }
        public TradingSystem TradingSystem { get; }
        public bool PublicLeague { get; }

        public bool HasSpecialSlots() => SpecialGameSlots.Any();

        public IReadOnlyList<LeagueTagStatus> GetTagsForSlot(int slotNumber)
        {
            var specialGameSlot = GetSpecialGameSlotByOverallSlotNumber(slotNumber);
            if (specialGameSlot.HasNoValue)
            {
                return LeagueTags;
            }

            return specialGameSlot.Value.Tags.Select(x => new LeagueTagStatus(x, TagStatus.Required)).ToList();
        }

        public Maybe<SpecialGameSlot> GetSpecialGameSlotByOverallSlotNumber(int slotNumber)
        {
            var hasSpecialSlot = _specialSlotDictionary.TryGetValue(slotNumber, out var specialSlot);
            if (!hasSpecialSlot)
            {
                return Maybe<SpecialGameSlot>.None;
            }

            return specialSlot;
        }

        public Result Validate()
        {
            if (GamesToDraft > StandardGames)
            {
                return Result.Failure("Can't draft more than the total number of standard games.");
            }

            if (CounterPicksToDraft > CounterPicks)
            {
                return Result.Failure("Can't draft more counter picks than the total number of counter picks.");
            }

            if (CounterPicksToDraft > GamesToDraft)
            {
                return Result.Failure("Can't have more drafted counter picks than drafted games.");
            }

            var bannedTagNames = LeagueTags.Where(x => x.Status.Equals(TagStatus.Banned)).Select(x => x.Tag.Name).ToHashSet();
            if (bannedTagNames.Contains("PlannedForEarlyAccess") && !bannedTagNames.Contains("CurrentlyInEarlyAccess"))
            {
                return Result.Failure("If you ban 'Planned for Early Access' you must also ban 'Currently in Early Access'. See the FAQ page for an explanation.");
            }
            if (bannedTagNames.Contains("WillReleaseInternationallyFirst") && !bannedTagNames.Contains("ReleasedInternationally"))
            {
                return Result.Failure("If you ban 'Will Release Internationally First' you must also ban 'Released Internationally'. See the FAQ page for an explanation.");
            }

            return Result.Success();
        }
    }
}
