﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FantasyCritic.Lib.Domain;
using FantasyCritic.Lib.Enums;
using FantasyCritic.Lib.Extensions;
using NodaTime;
using NodaTime.Testing;
using NodaTime.Text;
using NUnit.Framework;

namespace FantasyCritic.Test
{
    [TestFixture]
    public class EligibilityTests
    {
        private static Dictionary<string, MasterGameTag> _tagDictionary = new List<MasterGameTag>()
        {
            new MasterGameTag("Cancelled", "Cancelled", "CNCL", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("CurrentlyInEarlyAccess", "Currently in Early Access", "C-EA",null, true, false, "", new List<string>(), ""),
            new MasterGameTag("DirectorsCut", "Director's Cut", "DC", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("ExpansionPack", "Expansion Pack", "EXP", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("FreeToPlay", "Free to Play", "FTP", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("NewGame", "New Game", "NG", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("NewGamingFranchise", "New Gaming Franchise", "NGF", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("PartialRemake", "Partial Remake", "P-RMKE", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("PlannedForEarlyAccess", "Planned for Early Access", "P-EA",null, true, false, "", new List<string>(), ""),
            new MasterGameTag("Port", "Port", "PRT", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("Reimagining", "Reimagining", "RIMG", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("ReleasedInternationally", "Released Internationally", "R-INT",null, true, false, "", new List<string>(), ""),
            new MasterGameTag("Remake", "Remake", "RMKE", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("Remaster", "Remaster", "RMSTR", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("UnannouncedGame", "Unannounced Game", "UNA", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("VirtualReality", "Virtual Reality", "VR", null, false, false, "", new List<string>(), ""),
            new MasterGameTag("WillReleaseInternationallyFirst", "Will Release Internationally First", "W-INT",null, true, false, "", new List<string>(), ""),
            new MasterGameTag("YearlyInstallment", "Yearly Installment", "YI", null, false, false, "", new List<string>(), ""),
        }.ToDictionary(x => x.ShortName);

        private static MasterGame CreateBasicMasterGame(string name, LocalDate releaseDate, MasterGameTag tag)
        {
            return new MasterGame(Guid.NewGuid(), name, releaseDate.ToISOString(), releaseDate, releaseDate, null, null,
                releaseDate, null, null, "", null, null, false, false, false, Instant.MinValue,
                new List<MasterSubGame>(), new List<MasterGameTag>(){ tag });
        }

        private static MasterGame CreateComplexMasterGame(string name, LocalDate minimumReleaseDate, LocalDate? maximumReleaseDate, 
            LocalDate? earlyAccessReleaseDate, LocalDate? internationalReleaseDate, IEnumerable<MasterGameTag> tags)
        {
            return new MasterGame(Guid.NewGuid(), name, "TBA", minimumReleaseDate, maximumReleaseDate,
                earlyAccessReleaseDate, internationalReleaseDate, null, null, null, "", null, null, false, false, false,
                Instant.MinValue, new List<MasterSubGame>(), tags);

        }

        [Test]
        public void SimpleEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateBasicMasterGame("Elden Ring", new LocalDate(2022, 2, 25), _tagDictionary["NGF"]);

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned)
            };

            var slotTags = new List<LeagueTagStatus>();

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }

        [Test]
        public void SimpleInEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateBasicMasterGame("GTA 5 (PS5)", new LocalDate(2022, 2, 25), _tagDictionary["PRT"]);

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned)
            };

            var slotTags = new List<LeagueTagStatus>();

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(1, claimErrors.Count);
            Assert.AreEqual("That game is not eligible because the Port tag has been banned.", claimErrors[0].Error);
        }

        [Test]
        public void SlotEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateBasicMasterGame("Elden Ring", new LocalDate(2022, 2, 25), _tagDictionary["NGF"]);

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned)
            };

            var slotTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["NGF"], TagStatus.Required)
            };

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }

        [Test]
        public void SlotInEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateBasicMasterGame("Horizon Forbidden West", new LocalDate(2022, 2, 25), _tagDictionary["NG"]);

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned)
            };

            var slotTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["NGF"], TagStatus.Required)
            };

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(1, claimErrors.Count);
            Assert.AreEqual("That game is not eligible because it does not have any of the following required tags: (New Gaming Franchise)", claimErrors[0].Error);
        }

        [Test]
        public void EarlyAccessHasGameBeforeEarlyAccessEligible()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-05T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateComplexMasterGame("Have a Nice Death", new LocalDate(2022, 1, 3), null,
                new LocalDate(2022, 3, 6), null, new List<MasterGameTag>()
                {
                    _tagDictionary["NG"],
                    _tagDictionary["C-EA"],
                });

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned),
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Banned),
            };

            var slotTags = new List<LeagueTagStatus>();

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }

        [Test]
        public void EarlyAccessHasGameAfterEarlyAccessInEligible()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-03-10T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateComplexMasterGame("Have a Nice Death", new LocalDate(2022, 1, 3), null,
                new LocalDate(2022, 3, 6), null, new List<MasterGameTag>()
                {
                    _tagDictionary["NG"],
                    _tagDictionary["C-EA"],
                });

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned),
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Banned),
            };

            var slotTags = new List<LeagueTagStatus>();

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(1, claimErrors.Count);
            Assert.AreEqual("That game is not eligible because it has the tag: Currently in Early Access", claimErrors[0].Error);
        }

        [Test]
        public void EarlyAccessAllowedEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateComplexMasterGame("Baldur's Gate 3", new LocalDate(2022, 1, 3), null,
                new LocalDate(2020, 10, 6), null, new List<MasterGameTag>()
                {
                    _tagDictionary["NG"],
                    _tagDictionary["C-EA"],
                });

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned),
            };

            var slotTags = new List<LeagueTagStatus>();

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }

        [Test]
        public void EarlyAccessNormalAllowedSlotRequiredEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateComplexMasterGame("Baldur's Gate 3", new LocalDate(2022, 1, 3), null,
                new LocalDate(2020, 10, 6), null, new List<MasterGameTag>()
                {
                    _tagDictionary["NG"],
                    _tagDictionary["C-EA"],
                });

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned),
            };

            var slotTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["P-EA"], TagStatus.Required),
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Required)
            };

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }

        [Test]
        public void EarlyAccessNormalBannedSlotRequiredEligibleTest()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateComplexMasterGame("Baldur's Gate 3", new LocalDate(2022, 1, 3), null,
                new LocalDate(2020, 10, 6), null, new List<MasterGameTag>()
                {
                    _tagDictionary["NG"],
                    _tagDictionary["C-EA"],
                });

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned),
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Banned),
                new LeagueTagStatus(_tagDictionary["R-INT"], TagStatus.Banned),
            };

            var slotTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["P-EA"], TagStatus.Required),
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Required)
            };

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }

        [Test]
        public void EarlyAccessReleasedInternationallyComplexSlotEligible()
        {
            Instant acquisitionTime = InstantPattern.ExtendedIso.Parse("2022-01-31T20:49:24Z").GetValueOrThrow();
            var acquisitionDate = acquisitionTime.ToEasternDate();

            MasterGame masterGame = CreateComplexMasterGame("Baldur's Gate 3", new LocalDate(2022, 1, 3), null,
                new LocalDate(2020, 10, 6), null, new List<MasterGameTag>()
                {
                    _tagDictionary["NG"],
                    _tagDictionary["C-EA"],
                });

            var leagueTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["PRT"], TagStatus.Banned),
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Banned),
                new LeagueTagStatus(_tagDictionary["R-INT"], TagStatus.Banned),
            };

            var slotTags = new List<LeagueTagStatus>()
            {
                new LeagueTagStatus(_tagDictionary["C-EA"], TagStatus.Required),
                new LeagueTagStatus(_tagDictionary["R-INT"], TagStatus.Required),
                new LeagueTagStatus(_tagDictionary["NG"], TagStatus.Required),
                new LeagueTagStatus(_tagDictionary["NGF"], TagStatus.Required),
            };

            var claimErrors = LeagueTagExtensions.GameHasValidTags(leagueTags, slotTags, masterGame, masterGame.Tags, acquisitionDate);
            Assert.AreEqual(0, claimErrors.Count);
        }
    }
}
